namespace PhotoSiftReborn;

/// <summary>Conservative UI visibility gate for the P6B2 generated-fixture Rename trial.
/// This is NEVER executable authority: DurableRenameCoordinator independently
/// revalidates exact fixture ownership, journal identity and native file hashes
/// at Prepare, Apply and Undo. Not a general-photo permission gate.</summary>
internal static class FixtureRenameGuiGate
{
    private static bool EligibleHistory(WorkspaceSessionStore db, SortingWorkspace work,
        out IReadOnlyList<DurableRenameItem> rows)
    {
        rows=Array.Empty<DurableRenameItem>();
        try
        {
            if (!P5CGuiFixturePolicy.IsMarkedStore(db) || db.HasUnreconciledCopy ||
                db.AllMoveHistoryCount()!=0 || db.CopyItems(work.SessionId).Count!=0)
                return false;
            P5CGuiFixturePolicy.VerifyExactWorkspace(db,work);
            rows=db.RenameItems(work.SessionId);
            if(rows.Count!=db.AllRenameHistoryCount() ||
               rows.Any(r=>r.State is not ("Committed" or "Undone"))) return false;
            // The saved checkpoint must agree with every committed/undone row.
            foreach(var row in rows)
            {
                var photo=work.Photos.SingleOrDefault(p=>p.Id==row.PhotoId);
                var expected=row.State=="Committed"?row.Target:row.Source;
                if(photo is null || !SortingWorkspace.PathComparer.Equals(photo.Path,expected)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    internal static bool CanBrowse(WorkspaceSessionStore db,SortingWorkspace work)
        => EligibleHistory(db,work,out var rows) && rows.Count>0;

    internal static bool CanPrepare(WorkspaceSessionStore db,SortingWorkspace work)
    {
        if(work.Current is not { } current || work.AssignmentCount!=0 ||
           work.RemovedCount!=0 || work.HasAppliedCopies ||
           current.NativeIdentity is null) return false;
        if(!EligibleHistory(db,work,out var rows)) return false;
        // Old journal entries stay immutable after Undo. One photo may be renamed
        // only once; dependency chains/reusing prior path require a later design.
        return rows.All(r=>r.PhotoId!=current.Id && r.NativeIdentity!=current.NativeIdentity);
    }

    internal static bool CanUndo(WorkspaceSessionStore db,SortingWorkspace work)
        => EligibleHistory(db,work,out var rows) && rows.Any(r=>r.State=="Committed");
}
