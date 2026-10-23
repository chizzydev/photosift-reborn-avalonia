namespace PhotoSiftReborn;

/// <summary>Button visibility is never executable authority. The native
/// coordinator separately checks filesystem, identities, history and revision.</summary>
internal static class WindowsRenameGuiGate
{
    private static bool Eligible(WorkspaceSessionStore db,SortingWorkspace work,
        out IReadOnlyList<DurableRenameItem> rows)
    {
        rows=Array.Empty<DurableRenameItem>();
        try
        {
            WindowsLocalRenamePolicy.CheckWorkspace(db,work);
            if(db.HasUnreconciledCopy || db.AllMoveHistoryCount()!=0 ||
               db.CopyItems(work.SessionId).Count!=0) return false;
            rows=db.RenameItems(work.SessionId);
            if(rows.Count!=db.AllRenameHistoryCount() ||
               rows.Any(row=>row.ReviewedDirectoryIdentity is null ||
                   row.State is not ("Committed" or "Undone"))) return false;
            foreach(var group in rows.GroupBy(row=>row.PhotoId))
            {
                var chain=group.OrderBy(row=>row.Sequence).ToArray();
                for(int i=1;i<chain.Length;i++)
                    if(chain[i-1].State!="Undone" ||
                       chain[i].SupersedesRenameId!=chain[i-1].Id ||
                       chain[i-1].ReviewedDirectoryIdentity!=chain[i].ReviewedDirectoryIdentity)
                        return false;
                var tip=chain[^1];
                var photo=work.Photos.SingleOrDefault(p=>p.Id==tip.PhotoId);
                var expected=tip.State=="Committed"?tip.Target:tip.Source;
                if(photo is null || !SortingWorkspace.PathComparer.Equals(photo.Path,expected))
                    return false;
            }
            return true;
        }
        catch{return false;}
    }
    internal static bool CanBrowse(WorkspaceSessionStore db,SortingWorkspace work)
        =>Eligible(db,work,out var rows) && rows.Count>0;
    internal static bool CanPrepare(WorkspaceSessionStore db,SortingWorkspace work)
    {
        if(work.Current is not { } photo || work.AssignmentCount!=0 || work.RemovedCount!=0 ||
           work.HasAppliedCopies || photo.NativeIdentity is null) return false;
        if(!Eligible(db,work,out var rows)) return false;
        var same=rows.Where(row=>row.PhotoId==photo.Id).ToArray();
        return same.Length==0 || same.All(row=>row.State=="Undone");
    }
    internal static bool CanUndo(WorkspaceSessionStore db,SortingWorkspace work)
        =>Eligible(db,work,out var rows) && rows.Any(row=>row.State=="Committed");
}
