namespace PhotoSiftReborn;

internal static class OrdinaryFirstRenameGuiGate
{
    internal static bool CanPrepare(WorkspaceSessionStore db,SortingWorkspace work)
        =>OrdinaryFirstRenamePolicy.CanOffer(db,work);
    internal static bool CanUndo(WorkspaceSessionStore db,SortingWorkspace work)
        =>OrdinaryFirstRenamePolicy.CanUndo(db,work);
}
