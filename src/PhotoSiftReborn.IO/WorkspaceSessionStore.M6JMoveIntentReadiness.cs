using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P1: a source-backed, non-mutating preview of the exact Copy -> Move
/// Intent boundary. A positive result is NOT a grant: the final SQLite
/// transaction and native Move coordinator have not been wired here.
/// </summary>
public sealed record M6JMoveIntentReadiness(bool IsExactPreparedShape,
    string Explanation, string? PredecessorDigest = null,
    string? ReviewedCandidateDigest = null)
{
    public bool AuthorizesNativeOperation => false;
    public bool CanWriteMoveIntent => false;
    public bool AuthorizesSelectiveUndo => false;
}

public sealed partial class WorkspaceSessionStore
{
    private static string M6JIntentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.IndexOf('\0') >= 0 || Path.GetFullPath(path)!=path ||
            path.Normalize(NormalizationForm.FormC)!=path)
            throw new IOException("A3 exact Move intent path is not canonical or absolute.");
        return path.ToUpperInvariant();
    }

    // REVIEW ONLY: this must be REPEATED inside a future atomic Intent writer,
    // with row-level native validation, prior to any filesystem operation.
    public M6JMoveIntentReadiness InspectM6JMoveAfterCopyIntent(Guid reviewId,
        DurableMoveItem intent)
    {
        lock (_dbGate)
        {
            try
            {
                if (intent is null || reviewId==Guid.Empty || reviewId!=intent.Id ||
                    intent.Id==Guid.Empty || intent.InstallationId!=MoveInstallationId() ||
                    intent.SessionId==Guid.Empty || intent.PhotoId==Guid.Empty ||
                    intent.PlanId==Guid.Empty || intent.PlanRevision<0 || intent.Sequence<1 ||
                    intent.State!="Intent" || intent.Error is not null ||
                    intent.TempIdentity is not null || intent.TargetIdentity is not null ||
                    intent.SourceHash is null || intent.SourceHash.Length!=64 ||
                    string.IsNullOrWhiteSpace(intent.SourceIdentity) || intent.SourceLength<=0 ||
                    intent.SourceWriteTicks<=0 || intent.RouteKey=='\0' ||
                    HasUnreconciledCopy || AllCopyHistoryCount()!=1 ||
                    AllMoveHistoryCount()!=0 || AllRenameHistoryCount()!=0 ||
                    AllRejectHistoryCount()!=0 || CountAuxiliaryHistoryRows()!=0)
                    throw new IOException("M6J exact Move shape or single-Copy predecessor not established.");

                var proposal=M6JReadReview(reviewId)
                    ?? throw new IOException("M6J exact review record missing.");
                if (proposal.State!="Reviewed" ||
                    proposal.Installation!=intent.InstallationId.ToString("N") ||
                    proposal.ReviewDigest!=M6JReviewDigest(proposal.CandidateDigest,
                        proposal.HistoryDigest,proposal.CheckpointDigest))
                    throw new IOException("M6J grantless reviewed seal absent or invalid.");
                var candidate=M6JDecodeStored(proposal);
                if (candidate.Family!="Move" || candidate.Id!=intent.Id ||
                    candidate.SessionId!=intent.SessionId || candidate.PhotoId!=intent.PhotoId ||
                    candidate.Source!=intent.Source || candidate.Destination!=intent.Destination ||
                    candidate.SourceHash!=intent.SourceHash ||
                    candidate.SourceIdentity!=intent.SourceIdentity ||
                    candidate.SourceLength!=intent.SourceLength ||
                    candidate.SourceWriteTicks!=intent.SourceWriteTicks)
                    throw new IOException("M6J Move differs from the exact reviewed photo/source/target.");

                var saved=M6JSavedCheckpoint();
                if (saved.Snapshot.Revision!=intent.PlanRevision ||
                    saved.Snapshot.SessionId!=intent.SessionId ||
                    !saved.Snapshot.Routes.Any(r=>r.Key==intent.RouteKey) ||
                    !saved.Snapshot.Assignments.Any(a=>a.PhotoId==intent.PhotoId &&
                        a.Key==intent.RouteKey))
                    throw new IOException("M6J saved route, assignment or plan revision changed.");
                var basis=M6JRequireProspectiveBasis(candidate);
                if (basis.HistoryDigest!=proposal.HistoryDigest ||
                    basis.CheckpointDigest!=proposal.CheckpointDigest)
                    throw new IOException("M6J reviewed native basis or saved checkpoint changed.");

                // The preexisting Copy's native identity and output bytes must
                // independently verify NOW, not merely have State=Committed.
                string id,session,json;
                using(var cmd=_connection.CreateCommand())
                {
                    cmd.CommandText="SELECT id,session,json FROM copy_items LIMIT 2";
                    using var reader=cmd.ExecuteReader();
                    if(!reader.Read())throw new IOException("M6J Copy predecessor missing.");
                    id=reader.GetString(0);session=reader.GetString(1);json=reader.GetString(2);
                    if(reader.Read() || json.Length>32768)
                        throw new IOException("M6J predecessor count/size changed.");
                }
                var prior=JsonSerializer.Deserialize<DurableCopyItem>(json)
                    ?? throw new IOException("M6J prior Copy row corrupted.");
                if(prior.Id.ToString("N")!=id || prior.SessionId.ToString("N")!=session ||
                    prior.Id==Guid.Empty || prior.State!="Committed" || prior.Error is not null ||
                    prior.PhotoId==intent.PhotoId || prior.TargetIdentity is null ||
                    prior.SourceIdentity==intent.SourceIdentity ||
                    prior.TargetIdentity==intent.SourceIdentity ||
                    !NativeFileCapability.IsSingleLinkRegularFile(prior.Source) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(prior.Destination) ||
                    !CheckCopyOwnedFile(prior.Source,prior.SourceIdentity,prior.SourceHash,prior.SourceLength) ||
                    !CheckCopyOwnedFile(prior.Destination,prior.TargetIdentity,prior.SourceHash,prior.SourceLength) ||
                    File.Exists(prior.Temp) || Directory.Exists(prior.Temp) ||
                    File.Exists(prior.Recovery) || Directory.Exists(prior.Recovery))
                    throw new IOException("M6J prior Copy ownership is stale, corrupt or dependent.");

                string parent=Path.GetDirectoryName(intent.Destination)!;
                string sourceParent=Path.GetDirectoryName(intent.Source)!;
                string operationRoot=Path.Combine(sourceParent,".photosift-reborn-recovery",
                    intent.InstallationId.ToString("N"),intent.SessionId.ToString("N"),
                    intent.Id.ToString("N"));
                if(M6JIntentPath(intent.Source)==M6JIntentPath(intent.Destination) ||
                    M6JIntentPath(intent.Temp)!=M6JIntentPath(Path.Combine(parent,
                        ".photosift-move-"+intent.Id.ToString("N")+".part")) ||
                    M6JIntentPath(intent.OriginalArchive)!=M6JIntentPath(Path.Combine(
                        operationRoot,Path.GetFileName(intent.Source))) ||
                    M6JIntentPath(intent.OutputArchive)!=M6JIntentPath(Path.Combine(
                        operationRoot,"installed-"+Path.GetFileName(intent.Destination))) ||
                    !Directory.Exists(intent.OutputRoot) ||
                    SortingWorkspace.HasLinkedExistingAncestor(intent.OutputRoot) ||
                    SortingWorkspace.HasLinkedExistingAncestor(parent) ||
                    SortingWorkspace.HasLinkedExistingAncestor(sourceParent) ||
                    File.Exists(intent.Destination) || Directory.Exists(intent.Destination) ||
                    File.Exists(intent.Temp) || Directory.Exists(intent.Temp) ||
                    File.Exists(intent.OriginalArchive) || Directory.Exists(intent.OriginalArchive) ||
                    File.Exists(intent.OutputArchive) || Directory.Exists(intent.OutputArchive) ||
                    HasCaseFoldedNfcTargetAlias(intent.Destination) ||
                    NativeFileCapability.DeviceId(intent.Source)!=
                        NativeFileCapability.DeviceId(intent.OutputRoot))
                    throw new IOException("M6J Move temp/archive/destination ownership or volume invalid.");
                foreach(string next in new[]{intent.Source,intent.Destination,intent.Temp,
                    intent.OriginalArchive,intent.OutputArchive})
                foreach(string old in new[]{prior.Source,prior.Destination,prior.Temp,prior.Recovery})
                {
                    string x=M6JIntentPath(next),y=M6JIntentPath(old);
                    if(x==y || x.StartsWith(y+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
                        y.StartsWith(x+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                        throw new IOException("M6J Move and Copy file/recovery claims intersect.");
                }
                // Fresh OS-native descriptor proof strengthens the EXISTING grantless review
                // using three simultaneously held regular-file handles. Neither
                // the live witness nor the review digest can write an Intent.
                var pinned=M6JP4FreshPinnedPredecessor.Inspect(
                    new(prior.Source,prior.SourceIdentity,prior.SourceHash,prior.SourceLength,prior.SourceWriteTicks),
                    new(prior.Destination,prior.TargetIdentity,prior.SourceHash,prior.SourceLength),
                    new(intent.Source,intent.SourceIdentity,intent.SourceHash,intent.SourceLength,intent.SourceWriteTicks));
                if(!pinned.IsFreshWitness)
                    throw new IOException("P4-R2 fresh predecessor refusal: "+pinned.Explanation);
                if(!M6JNativeSourceMatches(candidate) ||
                    M6JRawHistoryDigest()!=proposal.HistoryDigest)
                    throw new IOException("M6J history or source changed during final observation.");
                return new(true,
                    "Exact disjoint Copy->Move Intent SHAPE and transient pinned handles observed. No grant; no Intent write; no Undo authority.",
                    M6JHash(id+"\n"+session+"\n"+json),proposal.CandidateDigest);
            }
            catch(Exception ex) when(ex is IOException or InvalidOperationException or
                InvalidDataException or ArgumentException or UnauthorizedAccessException or
                NotSupportedException or JsonException or OverflowException or NullReferenceException)
            {
                // R24-R2: retain the fail-closed result and all existing predicates.
                // Generated-only diagnostic: preserve a bounded reason for native triage.
                // This string is NOT an authorization, durable proof, or a retry signal.
                string reason=ex.Message.Replace('\r',' ').Replace('\n',' ');
                if (reason.Length>240) reason=reason.Substring(0,240);
                return new(false,"M6J-A3-P1 nonauthorizing Move boundary refused: "+
                    ex.GetType().Name+": "+reason);
            }
        }
    }
}
