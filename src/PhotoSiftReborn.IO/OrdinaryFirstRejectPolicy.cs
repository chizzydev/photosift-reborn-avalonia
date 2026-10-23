using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P6G2D: narrow FIRST ordinary Reject. This grants no journal/relocation itself.
/// The durable Reject coordinator independently checks native ownership, saved
/// mark/revision, no-replace relocation, operation history and verified Undo.
/// This policy denies risky filesystem scopes before presenting native GUI actions.
/// An existing history, including a successfully undone one, is NEVER erased.
/// </summary>
public static class OrdinaryFirstRejectPolicy
{
    private static bool Inside(string parent,string item)
    {
        string rel=Path.GetRelativePath(parent,item);
        return rel=="." || (!Path.IsPathRooted(rel) && rel!=".." &&
            !rel.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
    }
    private static bool Cloud(string path)=>Path.GetFullPath(path)
        .Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries)
        .Any(p=>p.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase) ||
            p.Equals("Dropbox",StringComparison.OrdinalIgnoreCase) ||
            p.Equals("Google Drive",StringComparison.OrdinalIgnoreCase) ||
            p.Equals("iCloudDrive",StringComparison.OrdinalIgnoreCase));

    public static bool CanUseWorkspace(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try
        {
            if(!NativeFileCapability.IsSupported ||
               !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) ||
               w.SourceRoot is null || w.DestinationRoot is null ||
               w.SourceRoots.Count!=1 || w.Photos.Count==0 ||
               !SortingWorkspace.PathComparer.Equals(w.SourceRoots[0],w.SourceRoot) ||
               !Directory.Exists(w.SourceRoot) || !Directory.Exists(w.DestinationRoot) ||
               w.Photos.Any(p=>!SortingWorkspace.PathComparer.Equals(
                    Path.GetDirectoryName(p.Path),w.SourceRoot)) ||
               SortingWorkspace.HasLinkedExistingAncestor(w.SourceRoot) ||
               SortingWorkspace.HasLinkedExistingAncestor(w.DestinationRoot) ||
               w.RemovedCount!=0 || w.HasAppliedCopies || w.AssignmentCount!=0 ||
               w.PendingRejectCount!=1 ||
               Inside(w.SourceRoot,w.DestinationRoot) ||
               Inside(w.DestinationRoot,w.SourceRoot))return false;
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            if(SortingWorkspace.HasLinkedExistingAncestor(state) ||
               Inside(state,w.SourceRoot) || Inside(w.SourceRoot,state) ||
               Inside(state,w.DestinationRoot) || Inside(w.DestinationRoot,state) ||
               Cloud(state) || Cloud(w.SourceRoot) || Cloud(w.DestinationRoot))return false;
            foreach(var folder in new[]{w.SourceRoot,w.DestinationRoot})
            {
                var drive=new DriveInfo(Path.GetPathRoot(folder)!);
                if(!drive.IsReady || drive.DriveType!=DriveType.Fixed)return false;
                if(OperatingSystem.IsWindows() &&
                   !drive.DriveFormat.Equals("NTFS",StringComparison.OrdinalIgnoreCase))return false;
                if(OperatingSystem.IsLinux() &&
                   !new[]{"ext4","xfs","btrfs"}.Contains(drive.DriveFormat,
                      StringComparer.OrdinalIgnoreCase))return false;
            }
            if(NativeFileCapability.DeviceId(w.SourceRoot)!=
               NativeFileCapability.DeviceId(w.DestinationRoot))return false;
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException or NotSupportedException)
        {return false;}
    }
    public static bool CanStage(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try
        {
            if(!CanUseWorkspace(db,w) || db.HasUnreconciledCopy ||
               db.AllCopyHistoryCount()!=0 || db.AllMoveHistoryCount()!=0 ||
               db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0 ||
               !db.AssessFreshMoveEpoch().IsFresh)return false;
            var pending=w.PendingRejectReviewPhotos.ToArray();
            if(pending.Length!=1 || pending[0].NativeIdentity is null ||
               !File.Exists(pending[0].Path) ||
               SortingWorkspace.HasSymlinkOrReparsePoint(pending[0].Path))return false;
            var snap=db.Load();
            if(snap is null)return false;
            var live=w.CaptureSession();
            return snap.SessionId==live.SessionId &&
                (live.Revision==snap.Revision ||
                 (snap.Revision<long.MaxValue-1 && live.Revision==snap.Revision+1)) &&
                JsonSerializer.Serialize(live with {Revision=snap.Revision})==
                    JsonSerializer.Serialize(snap);
        }
        catch(Exception){return false;}
    }
    public static bool CanInspectHistory(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try
        {
            if(!CanUseWorkspace(db,w) || db.HasUnreconciledCopy ||
               db.AllCopyHistoryCount()!=0 || db.AllMoveHistoryCount()!=0 ||
               db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=1)return false;
            var rows=db.RejectItems();
            return rows.Count==1 && rows[0].SessionId==w.SessionId &&
                w.IsPendingReject(rows[0].PhotoId);
        }
        catch(Exception){return false;}
    }
}
