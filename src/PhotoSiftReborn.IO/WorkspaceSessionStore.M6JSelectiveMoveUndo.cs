using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P2B: exact ONE committed synthetic Copy + ONE committed native Move
/// after atomically consumed review. This is a narrow verification gate for
/// selective Move Undo, NOT approval for general mixed histories or user photos.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    internal DurableMoveItem RequireM6JConsumedCopyMoveForSelectiveUndo(Guid session)
        => RequireM6JConsumedCopyMoveCore(session, null, "");

    // P4-R10: prove the SAME exact consumed Copy/Move review and historical
    // predecessor while ONE generated third-family Rename remains on top.
    // This is not the ordinary or generic selective Move Undo grant.
    internal DurableMoveItem RequireM6JConsumedCopyMoveWithGeneratedRename(
        Guid session, DurableRenameItem rename, string expectedRenameState)
    {
        if (rename is null || expectedRenameState is not ("Committed" or "UndoIntent"))
            throw new IOException("P4-R10 invalid exact generated Rename observation.");
        return RequireM6JConsumedCopyMoveCore(session, rename, expectedRenameState);
    }

    private DurableMoveItem RequireM6JConsumedCopyMoveCore(
        Guid session, DurableRenameItem? activeRename, string expectedRenameState)
    {
        lock (_dbGate)
        {
            if (session==Guid.Empty || HasUnreconciledCopy || AllCopyHistoryCount()!=1 ||
                AllMoveHistoryCount()!=1 || AllRenameHistoryCount()!=(activeRename is null?0:1) ||
                AllRejectHistoryCount()!=0 || CountAuxiliaryHistoryRows()!=0)
                throw new IOException("M6J Undo requires precisely one resolved Copy and one Move.");
            var moves=MoveItems(session);
            if (moves.Count!=1 || moves[0].State!="Committed" ||
                moves[0].Error is not null || moves[0].TargetIdentity is null ||
                moves[0].TempIdentity is null || moves[0].Id==Guid.Empty ||
                File.Exists(moves[0].Temp) || Directory.Exists(moves[0].Temp) ||
                File.Exists(moves[0].OutputArchive) || Directory.Exists(moves[0].OutputArchive) ||
                !NativeFileCapability.IsSingleLinkRegularFile(moves[0].OriginalArchive) ||
                !NativeFileCapability.IsSingleLinkRegularFile(activeRename is null?
                    moves[0].Destination:activeRename.Target))
                throw new IOException("M6J Undo requires an exact committed Move journal.");
            var move=moves[0];
            if (activeRename is not null)
            {
                var renames=RenameItems(session);
                if (renames.Count!=1 || renames[0]!=activeRename ||
                    activeRename.State!=expectedRenameState || activeRename.Error is not null ||
                    activeRename.Id==Guid.Empty || activeRename.SessionId!=session ||
                    activeRename.InstallationId!=MoveInstallationId() ||
                    activeRename.PhotoId!=move.PhotoId ||
                    activeRename.Source!=move.Destination ||
                    activeRename.NativeIdentity!=move.TargetIdentity ||
                    activeRename.Hash!=move.SourceHash ||
                    activeRename.Length!=move.SourceLength ||
                    // P4-R11: the native Move output is written into a NEW
                    // temporary file and can have a different modification time
                    // from the original Move source.  Rename C records the
                    // timestamp of that newly installed output, not the source.
                    // Requiring the two different files' timestamps to match
                    // incorrectly rejected every valid third-family Undo.
                    // Keep checking the Rename's OWN recorded timestamp against
                    // its actual native file below, and require fresh four-pin
                    // hash/identity evidence before each Undo relocation.
                    activeRename.ReviewedDirectoryIdentity is null ||
                    !SortingWorkspace.PathComparer.Equals(
                        Path.GetDirectoryName(activeRename.Source),
                        Path.GetDirectoryName(activeRename.Target)) ||
                    !SortingWorkspace.PathComparer.Equals(
                        NativeFileCapability.DirectoryIdentity(Path.GetDirectoryName(activeRename.Target)!),
                        activeRename.ReviewedDirectoryIdentity) ||
                    File.Exists(move.Destination) || Directory.Exists(move.Destination) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(activeRename.Target) ||
                    NativeFileCapability.Identity(activeRename.Target)!=activeRename.NativeIdentity ||
                    new FileInfo(activeRename.Target).Length!=activeRename.Length ||
                    new FileInfo(activeRename.Target).LastWriteTimeUtc.Ticks!=activeRename.WriteTicks)
                    throw new IOException("P4-R10 active Rename does not exactly own committed Move output.");
            }
            var stored=M6JReadReview(move.Id)
                ?? throw new IOException("M6J consumed review is missing.");
            if (stored.State!="Consumed" || stored.Family!="Move" ||
                stored.Installation!=MoveInstallationId().ToString("N") ||
                stored.Session!=session.ToString("N") ||
                stored.ReviewDigest!=M6JReviewDigest(stored.CandidateDigest,
                    stored.HistoryDigest,stored.CheckpointDigest))
                throw new IOException("M6J selective Undo review seal/installation is not authentic.");
            var candidate=M6JDecodeStored(stored);
            if (candidate.Family!="Move" || candidate.Id!=move.Id ||
                candidate.SessionId!=move.SessionId || candidate.PhotoId!=move.PhotoId ||
                candidate.Source!=move.Source || candidate.Destination!=move.Destination ||
                candidate.SourceIdentity!=move.SourceIdentity || candidate.SourceHash!=move.SourceHash ||
                candidate.SourceLength!=move.SourceLength ||
                candidate.SourceWriteTicks!=move.SourceWriteTicks)
                throw new IOException("M6J consumed candidate and actual Move disagree.");

            string id,copySession,copyJson;
            using(var raw=_connection.CreateCommand())
            {
                raw.CommandText="SELECT id,session,json FROM copy_items LIMIT 2";
                using var rd=raw.ExecuteReader();
                if(!rd.Read()) throw new IOException("M6J Copy predecessor was removed.");
                id=rd.GetString(0);copySession=rd.GetString(1);copyJson=rd.GetString(2);
                if(rd.Read() || copyJson.Length>32768)
                    throw new IOException("M6J Copy predecessor row was duplicated or oversized.");
            }
            // Recompute the exact original raw-history digest as it existed
            // BEFORE P2A atomically inserted this first Move Intent.
            var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
            foreach(var value in new[]{id,copySession,copyJson})
                history.Append(value.Length).Append(':').Append(value);
            history.Append("\nmove_items\nrename_items\nreject_items\n");
            if (M6JHash(history.ToString())!=stored.HistoryDigest)
                throw new IOException("M6J original native Copy history changed after review.");
            var copy=JsonSerializer.Deserialize<DurableCopyItem>(copyJson)
                ?? throw new IOException("M6J predecessor Copy journal was malformed.");
            if (copy.Id==Guid.Empty || copy.Id.ToString("N")!=id ||
                copy.SessionId.ToString("N")!=copySession || copy.PhotoId==move.PhotoId ||
                copy.State!="Committed" || copy.Error is not null ||
                copy.TargetIdentity is null ||
                !NativeFileCapability.IsSingleLinkRegularFile(copy.Source) ||
                !NativeFileCapability.IsSingleLinkRegularFile(copy.Destination) ||
                !CheckCopyOwnedFile(copy.Source,copy.SourceIdentity,copy.SourceHash,copy.SourceLength) ||
                !CheckCopyOwnedFile(copy.Destination,copy.TargetIdentity,copy.SourceHash,copy.SourceLength) ||
                File.Exists(copy.Temp) || Directory.Exists(copy.Temp) ||
                File.Exists(copy.Recovery) || Directory.Exists(copy.Recovery))
                throw new IOException("M6J committed Copy native predecessor is no longer owned.");

            // Unlike a general history waiver, this verifies *all* four native
            // Move claims remain independent of all four preserved Copy claims.
            foreach(string next in new[]{move.Source,move.Destination,move.Temp,
                move.OriginalArchive,move.OutputArchive})
            foreach(string old in new[]{copy.Source,copy.Destination,copy.Temp,copy.Recovery})
            {
                string x=M6JIntentPath(next), y=M6JIntentPath(old);
                if(x==y || x.StartsWith(y+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
                    y.StartsWith(x+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                    throw new IOException("M6J Copy/Move ownership claims now overlap.");
            }
            if (activeRename is not null)
            {
                // The one new pathname may not coincide with ANY earlier
                // original, recovery, temporary or archived native claim.
                foreach (string oldPath in new[]{copy.Source,copy.Destination,
                    copy.Temp,copy.Recovery,move.Source,move.OriginalArchive,
                    move.Temp,move.OutputArchive})
                {
                    string x=M6JIntentPath(activeRename.Target), y=M6JIntentPath(oldPath);
                    if (x==y || x.StartsWith(y+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
                        y.StartsWith(x+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                        throw new IOException("P4-R10 Rename target overlaps preserved native predecessor.");
                }
            }
            // UndoCore independently verifies the committed Move archives,
            // output native identities/hashes and ownership markers BEFORE
            // relocating either file. No new Intent or approval is issued.
            return move;
        }
    }
}
