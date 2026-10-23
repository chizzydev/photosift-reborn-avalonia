namespace PhotoSiftReborn;

/// <summary>
/// P6F3C2: selectively reconsider an Undo-restored Copy decision only after a
/// fresh, full terminal native recovery proof. Never a Move/Rename/Reject grant.
/// This only updates the in-memory decision. The GUI persists it through its
/// existing session checkpoint path after the action returns successfully.
/// </summary>
public static class CopyUndoChoiceEditor
{
    public static void ClearRestoredChoice(WorkspaceSessionStore db,
        SortingWorkspace work, Guid photoId)
    {
        if (db is null || work is null || photoId==Guid.Empty)
            throw new InvalidOperationException("Invalid selected Copy recovery decision. No changes.");
        // Read-only native proof protects EVERY historical row, marker, source,
        // archived file and destination. An Undone label alone is not enough.
        var proof=CopyUndoContinuation.Inspect(db,work);
        if (!proof.Verified)
            throw new IOException("Copy recovery is not currently verified: "+proof.Reason);
        var snapshot=work.CaptureSession();
        var selected=snapshot.Photos.SingleOrDefault(p=>p.Id==photoId)
            ?? throw new InvalidOperationException("Selected photo no longer exists. No changes.");
        var assigned=snapshot.Assignments.SingleOrDefault(a=>a.PhotoId==photoId)
            ?? throw new InvalidOperationException("Selected photo has no route to clear.");
        if (snapshot.Decisions.Any(d=>d.PhotoId==photoId) ||
            (snapshot.PendingRejectIds ?? Array.Empty<Guid>()).Contains(photoId))
            throw new InvalidOperationException("This is not a pristine Undo-restored Copy choice. No changes.");
        // Requiring the most recent operation for THIS photo prevents an older
        // Family copy from legitimizing a later, different restored route.
        // The entire journal was already proven by Inspect() above.
        var matching=db.CopyItems(work.SessionId).Where(row=>row.PhotoId==photoId).ToArray();
        if (matching.Length==0)
            throw new IOException("Selected photo has no owned historical Copy Undo.");
        var latest=matching[^1]; // durable journal retrieval ordered by sequence,id
        if (latest.State!="Undone" || latest.Key!=assigned.Key ||
            !SortingWorkspace.PathComparer.Equals(latest.Source,selected.Path) ||
            latest.SourceIdentity!=selected.NativeIdentity ||
            latest.SourceLength!=selected.Length ||
            latest.SourceWriteTicks!=selected.LastWriteUtcTicks)
            throw new IOException("Selected restored route differs from its native-verified terminal Copy history.");
        work.ClearNativeVerifiedRestoredCopyChoice(photoId,assigned.Key);
    }
}
