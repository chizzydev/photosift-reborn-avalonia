using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P6F3C1: fresh, read-only proof for the current saved Copy epoch. This is
/// not a durable grant, not permission to erase archived outputs and not an
/// authority for Move/Rename/Reject. Any change to native ownership, contents,
/// archive ownership marker, source, or journal revokes continuation.
/// </summary>
public sealed record CopyUndoContinuationEvidence(bool Verified, int HistoricalRows, string Reason);

public static class CopyUndoContinuation
{
    private const string OwnerBytes = "PhotoSift Reborn managed reversible output archives v1\n";

    public static CopyUndoContinuationEvidence Inspect(WorkspaceSessionStore db, SortingWorkspace work)
    {
        int count=0;
        CopyUndoContinuationEvidence Refuse(string why) => new(false,count,why);
        try
        {
            if (!NativeFileCapability.IsSupported || db.HasUnreconciledCopy)
                return Refuse("No verified native identity adapter, or an older Copy risk barrier is present.");
            if (db.AllMoveHistoryCount()!=0 || db.AllRenameHistoryCount()!=0 ||
                db.AllRejectHistoryCount()!=0)
                return Refuse("Other native operation history is present. Do not mix epochs.");
            if (work.SessionId==Guid.Empty || work.HasAppliedCopies)
                return Refuse("No complete, current undone Copy session.");
            var items=db.CopyItems(work.SessionId);
            count=items.Count;
            if (count==0 || db.AllCopyHistoryCount()!=count)
                return Refuse("Copy history is empty or belongs to a different saved session.");
            if (items.Any(x=>x.State!="Undone" || x.SessionId!=work.SessionId ||
                    x.Id==Guid.Empty || x.PhotoId==Guid.Empty || x.TargetIdentity is null ||
                    string.IsNullOrWhiteSpace(x.SourceIdentity) ||
                    string.IsNullOrWhiteSpace(x.SourceHash) || x.SourceHash.Length!=64 || x.SourceLength<0 ||
                    !string.IsNullOrEmpty(x.Error) ||
                    string.IsNullOrWhiteSpace(x.Source) ||
                    string.IsNullOrWhiteSpace(x.Destination) ||
                    string.IsNullOrWhiteSpace(x.Recovery) ||
                    string.IsNullOrWhiteSpace(x.Temp)))
                return Refuse("At least one Copy item is unresolved or lacks original native ownership proof.");
            if (items.Select(x=>x.Id).Distinct().Count()!=count)
                return Refuse("Copy operation identities are duplicated.");
            var saved=db.Load();
            var current=work.CaptureSession();
            // Restoring a session intentionally advances its revision by ONE to
            // invalidate any old UI approval. That alone must not make valid,
            // read-only Copy-Undo history impossible to prove after restart.
            // Compare the ENTIRE persisted checkpoint, not just photo identities:
            // unsaved assignment, route, index, collision, reject or source edits
            // must never be accepted through the single-revision exception.
            if (saved is null || saved.SessionId!=current.SessionId ||
                (saved.Revision!=current.Revision &&
                 (saved.Revision>=long.MaxValue-1 || current.Revision!=saved.Revision+1)) ||
                !CheckpointContentsMatch(saved,current))
                return Refuse("The actual saved workspace differs from the live continuation session.");
            foreach (var row in items)
            {
                var photo=current.Photos.SingleOrDefault(p=>p.Id==row.PhotoId);
                if (photo is null || !SortingWorkspace.PathComparer.Equals(photo.Path,row.Source) ||
                    photo.NativeIdentity!=row.SourceIdentity || photo.Length!=row.SourceLength ||
                    photo.LastWriteUtcTicks!=row.SourceWriteTicks)
                    return Refuse("Historical original identity does not match the saved photo checkpoint.");
                if (!Path.IsPathFullyQualified(row.Source) ||
                    !Path.IsPathFullyQualified(row.Destination) ||
                    !Path.IsPathFullyQualified(row.Recovery) ||
                    !Path.IsPathFullyQualified(row.Temp) ||
                    SortingWorkspace.PathComparer.Equals(row.Source,row.Destination) ||
                    SortingWorkspace.PathComparer.Equals(row.Source,row.Recovery) ||
                    File.Exists(row.Destination) || Directory.Exists(row.Destination) ||
                    File.Exists(row.Temp) || Directory.Exists(row.Temp))
                    return Refuse("Saved paths are invalid, or a previously freed target/temp is occupied.");
                var archive=Path.GetFullPath(row.Recovery);
                var itemDir=Path.GetDirectoryName(archive);
                var sessionDir=itemDir is null?null:Path.GetDirectoryName(itemDir);
                var owned=sessionDir is null?null:Path.GetDirectoryName(sessionDir);
                if (owned is null || itemDir is null || sessionDir is null ||
                    !string.Equals(Path.GetFileName(itemDir),row.Id.ToString("N"),StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFileName(sessionDir),row.SessionId.ToString("N"),StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFileName(owned),".photosift-reborn-recovery",StringComparison.Ordinal) ||
                    !string.Equals(Path.GetFileName(archive),Path.GetFileName(row.Destination),StringComparison.Ordinal) ||
                    HasLinkedFileParent(row.Source) ||
                    SortingWorkspace.HasLinkedExistingAncestor(row.Destination) ||
                    HasLinkedFileParent(archive))
                    return Refuse("Archive route or source/destination ancestry does not match native ownership scope.");
                var marker=Path.Combine(owned,".photosift-owner-v1");
                if (!File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
                    !File.ReadAllBytes(marker).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(OwnerBytes)))
                    return Refuse("Protected recovery ownership marker is missing or changed.");
                if (!Prove(row.Source,row.SourceIdentity,row.SourceHash,row.SourceLength) ||
                    !Prove(archive,row.TargetIdentity,row.SourceHash,row.SourceLength))
                    return Refuse("Original or archived copy native identity/content differs from the journal.");
            }
            return new(true,count,"Every prior Copy is fully Undone; original and archived bytes/native identities agree. No other operation history. Evidence is read-only and must be rechecked before any new operation.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        { return Refuse("Evidence unavailable: "+ex.Message); }
    }

    // A restored workspace has revision saved.Revision+1 by design; its
    // externally observable checkpoint must otherwise match ALL persisted
    // fields. A fresh review/approval is volatile and is deliberately absent
    // from CaptureSession. Comparing only photos would grant unsafe stale
    // assignments/routes, so require full serialized snapshot equality.
    private static bool CheckpointContentsMatch(
        WorkspaceSessionSnapshot saved, WorkspaceSessionSnapshot current)
    {
        var adjusted=saved with { Revision=current.Revision };
        return string.Equals(JsonSerializer.Serialize(adjusted),
            JsonSerializer.Serialize(current),StringComparison.Ordinal);
    }

    // HasLinkedExistingAncestor accepts DIRECTORY paths. Calling it on a present
    // original or recovery FILE necessarily throws "A destination ancestor is a
    // file", even when every directory is safe. Check the parent directory chain
    // here; Prove() separately verifies the final file leaf (no link/reparse),
    // its native identity, bytes and length. Never relax either half.
    private static bool HasLinkedFileParent(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var parent = Path.GetDirectoryName(full)
            ?? throw new IOException("A Copy journal file path has no parent directory.");
        return SortingWorkspace.HasLinkedExistingAncestor(parent);
    }

    private static bool Prove(string path,string identity,string expectedHash,long length)
    {
        if (!File.Exists(path) || SortingWorkspace.HasSymlinkOrReparsePoint(path)) return false;
        var info=new FileInfo(path);
        return info.Length==length && NativeFileCapability.Identity(path)==identity &&
            SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==expectedHash;
    }
}
