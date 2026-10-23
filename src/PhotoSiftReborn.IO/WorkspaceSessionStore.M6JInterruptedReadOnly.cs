using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P2C-P0 read-only evidence for one generated Copy->Move ledger at an
/// interruption boundary. Each value is an observation, NOT a recovery plan.
/// No method in this file changes SQLite, native files or grant state.
/// </summary>
public sealed record M6JInterruptedCopyMoveObservation(
    Guid MoveId, string RecordedMoveState,
    bool ConsumedReviewSealAndCandidateMatch,
    bool OriginalCopyHistoryDigestMatches,
    bool CommittedCopyNativeOwnershipMatches,
    bool NativeClaimsAreDisjoint,
    string MoveSource, string MoveTarget, string MoveTemp,
    string MoveOriginalArchive, string MoveOutputArchive)
{
    public string RecoveryVerdict => "MANUAL_RECONCILIATION_REQUIRED";
    public bool AuthorizesFilesystemMutation => false;
    public bool AuthorizesAutomaticReplay => false;
    public bool AuthorizesAutomaticUndo => false;
    public bool AuthorizesJournalStateRepair => false;
}

public sealed partial class WorkspaceSessionStore
{
    // Output vocabulary: OWNED means expected native identity+full hash+size
    // proved at observation time; ABSENT means no file or directory exists;
    // UNKNOWN means the name is occupied or proof is incomplete. OWNED is
    // deliberately NOT a warrant to relocate, unlink or journal an operation.
    private static string M6JInterruptedFileEvidence(string path, string? identity,
        string hash, long length)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return "ABSENT";
            return CheckCopyOwnedFile(path, identity, hash, length) ? "OWNED" : "UNKNOWN";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return "UNKNOWN";
        }
    }

    /// <summary>
    /// Never infer an execution or Undo step from an interrupted journal.
    /// Strictly one Copy, one Move and no other family/auxiliary history;
    /// even when all proof booleans are true, the return value cannot grant
    /// filesystem mutation or change a persisted row after an interruption.
    /// </summary>
    internal M6JInterruptedCopyMoveObservation InspectM6JInterruptedCopyMoveReadOnly(
        Guid moveSession)
    {
        lock (_dbGate)
        {
            if (moveSession==Guid.Empty || AllCopyHistoryCount()!=1 ||
                AllMoveHistoryCount()!=1 || AllRenameHistoryCount()!=0 ||
                AllRejectHistoryCount()!=0 || CountAuxiliaryHistoryRows()!=0 ||
                HasUnreconciledCopy)
                throw new IOException("Interrupted mixed-family inventory is not precisely one Copy and one Move.");
            var moves=MoveItems(moveSession);
            if (moves.Count!=1 || moves[0].Id==Guid.Empty ||
                moves[0].InstallationId!=MoveInstallationId())
                throw new IOException("Interrupted Move session/installation is not unique.");
            var move=moves[0];
            if (move.State is "Committed" or "Undone")
                throw new IOException("Terminal Move does not belong to interrupted-only inspection.");
            var review=M6JReadReview(move.Id);
            bool sealedReview=false;
            if (review is not null)
            {
                try
                {
                    if (review.State=="Consumed" && review.Family=="Move" &&
                        review.Installation==move.InstallationId.ToString("N") &&
                        review.Session==move.SessionId.ToString("N") &&
                        review.ReviewDigest==M6JReviewDigest(review.CandidateDigest,
                            review.HistoryDigest,review.CheckpointDigest))
                    {
                        var candidate=M6JDecodeStored(review);
                        sealedReview=candidate.Family=="Move" && candidate.Id==move.Id &&
                            candidate.SessionId==move.SessionId && candidate.PhotoId==move.PhotoId &&
                            candidate.Source==move.Source && candidate.Destination==move.Destination &&
                            candidate.SourceHash==move.SourceHash &&
                            candidate.SourceIdentity==move.SourceIdentity &&
                            candidate.SourceLength==move.SourceLength &&
                            candidate.SourceWriteTicks==move.SourceWriteTicks;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or
                    InvalidOperationException or JsonException or ArgumentException)
                {
                    sealedReview=false;
                }
            }
            string id, session, json;
            using(var cmd=_connection.CreateCommand())
            {
                cmd.CommandText="SELECT id,session,json FROM copy_items LIMIT 2";
                using var reader=cmd.ExecuteReader();
                if(!reader.Read())throw new IOException("Interrupted Copy predecessor vanished.");
                id=reader.GetString(0);session=reader.GetString(1);json=reader.GetString(2);
                if(reader.Read() || json.Length>32768)
                    throw new IOException("Interrupted Copy predecessor ambiguous or oversized.");
            }
            bool digestMatch=false,copyOwned=false,disjoint=false;
            try
            {
                var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach(var value in new[]{id,session,json})
                    history.Append(value.Length).Append(':').Append(value);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                digestMatch=review is not null && M6JHash(history.ToString())==review.HistoryDigest;
                var copy=JsonSerializer.Deserialize<DurableCopyItem>(json);
                if(copy is not null && copy.Id!=Guid.Empty &&
                    copy.Id.ToString("N")==id && copy.SessionId.ToString("N")==session &&
                    copy.PhotoId!=move.PhotoId && copy.State=="Committed" &&
                    copy.Error is null && copy.TargetIdentity is not null)
                {
                    copyOwned=NativeFileCapability.IsSingleLinkRegularFile(copy.Source) &&
                        NativeFileCapability.IsSingleLinkRegularFile(copy.Destination) &&
                        CheckCopyOwnedFile(copy.Source,copy.SourceIdentity,copy.SourceHash,copy.SourceLength) &&
                        CheckCopyOwnedFile(copy.Destination,copy.TargetIdentity,copy.SourceHash,copy.SourceLength) &&
                        !File.Exists(copy.Temp) && !Directory.Exists(copy.Temp) &&
                        !File.Exists(copy.Recovery) && !Directory.Exists(copy.Recovery);
                    disjoint=true;
                    foreach(string claim in new[]{move.Source,move.Destination,move.Temp,
                        move.OriginalArchive,move.OutputArchive})
                    foreach(string predecessor in new[]{copy.Source,copy.Destination,copy.Temp,copy.Recovery})
                    {
                        string x=M6JIntentPath(claim),y=M6JIntentPath(predecessor);
                        if(x==y || x.StartsWith(y+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
                            y.StartsWith(x+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                            disjoint=false;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                InvalidOperationException or JsonException or ArgumentException or
                NotSupportedException or UnauthorizedAccessException)
            {
                // Ambiguous native evidence can never become a positive proof.
                digestMatch=false;copyOwned=false;disjoint=false;
            }
            return new M6JInterruptedCopyMoveObservation(move.Id,move.State,
                sealedReview,digestMatch,copyOwned,disjoint,
                M6JInterruptedFileEvidence(move.Source,move.SourceIdentity,move.SourceHash,move.SourceLength),
                M6JInterruptedFileEvidence(move.Destination,
                    move.TargetIdentity??move.TempIdentity,move.SourceHash,move.SourceLength),
                M6JInterruptedFileEvidence(move.Temp,move.TempIdentity,move.SourceHash,move.SourceLength),
                M6JInterruptedFileEvidence(move.OriginalArchive,
                    move.SourceIdentity,move.SourceHash,move.SourceLength),
                M6JInterruptedFileEvidence(move.OutputArchive,
                    move.TargetIdentity,move.SourceHash,move.SourceLength));
        }
    }
}
