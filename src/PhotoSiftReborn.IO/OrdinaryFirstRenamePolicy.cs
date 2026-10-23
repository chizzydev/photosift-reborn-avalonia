using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// First ordinary-file Rename authority. Exactly one fresh, same-directory,
/// extension-preserving, no-replace Rename on a local fixed NTFS/ext4/xfs/btrfs
/// filesystem. No filesystem mutation occurs here. This policy is revalidated
/// by the native coordinator immediately before and after durable Intent.
/// Historical Rename/Move/Copy/Reject rows cannot be bypassed by changing a
/// session identifier; later epochs require their own independently proved grant.
/// </summary>
public static class OrdinaryFirstRenamePolicy
{
    private static bool Overlaps(string a,string b)
    {
        string rel=Path.GetRelativePath(a,b);
        string back=Path.GetRelativePath(b,a);
        static bool Inside(string r)=>r=="." || (!Path.IsPathRooted(r) &&
            r!=".." && !r.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
        return Inside(rel) || Inside(back);
    }
    private static bool Cloud(string path) => Path.GetFullPath(path)
        .Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries)
        .Any(x=>x.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase) ||
            x.Equals("Dropbox",StringComparison.OrdinalIgnoreCase) ||
            x.Equals("Google Drive",StringComparison.OrdinalIgnoreCase) ||
            x.Equals("iCloudDrive",StringComparison.OrdinalIgnoreCase));

    public static bool CanUseWorkspace(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try
        {
            if (!NativeFileCapability.IsSupported ||
                !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) ||
                w.SourceRoot is null || w.DestinationRoot is null ||
                w.SourceRoots.Count!=1 || w.Photos.Count==0 ||
                !SortingWorkspace.PathComparer.Equals(w.SourceRoots[0],w.SourceRoot) ||
                !Directory.Exists(w.SourceRoot) || !Directory.Exists(w.DestinationRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(w.SourceRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(w.DestinationRoot) ||
                w.RemovedCount!=0 || w.PendingRejectCount!=0 || w.HasAppliedCopies ||
                w.AssignmentCount!=0 || Overlaps(w.SourceRoot,w.DestinationRoot) ||
                w.Photos.Any(p=>!SortingWorkspace.PathComparer.Equals(
                    Path.GetDirectoryName(p.Path),w.SourceRoot))) return false;
            var state=Path.GetDirectoryName(db.DatabasePath)!;
            if(Overlaps(state,w.SourceRoot) || Overlaps(state,w.DestinationRoot) ||
               Cloud(w.SourceRoot) || Cloud(w.DestinationRoot) || Cloud(state) ||
               SortingWorkspace.HasLinkedExistingAncestor(state)) return false;
            foreach(var folder in new[]{w.SourceRoot,w.DestinationRoot})
            {
                var drive=new DriveInfo(Path.GetPathRoot(folder)!);
                if(!drive.IsReady || drive.DriveType!=DriveType.Fixed) return false;
                if(OperatingSystem.IsWindows() &&
                   !drive.DriveFormat.Equals("NTFS",StringComparison.OrdinalIgnoreCase)) return false;
                if(OperatingSystem.IsLinux() &&
                   !new[]{"ext4","xfs","btrfs"}.Contains(drive.DriveFormat,StringComparer.OrdinalIgnoreCase))return false;
            }
            if(NativeFileCapability.DeviceId(w.SourceRoot)!=NativeFileCapability.DeviceId(w.DestinationRoot))return false;
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or InvalidDataException){return false;}
    }
    public static bool CanOffer(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try { return CanUseWorkspace(db,w) && !db.HasUnreconciledCopy &&
              db.AllCopyHistoryCount()==0 && db.AllMoveHistoryCount()==0 &&
              db.AllRejectHistoryCount()==0 && db.AllRenameHistoryCount()==0 &&
              w.Current is {NativeIdentity:not null}; }
        catch {return false;}
    }
    public static bool CanUndo(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try
        {
            if(!CanUseWorkspace(db,w) || db.HasUnreconciledCopy ||
               db.AllCopyHistoryCount()!=0 || db.AllMoveHistoryCount()!=0 ||
               db.AllRejectHistoryCount()!=0 || db.AllRenameHistoryCount()!=1)return false;
            var rows=db.RenameItems(w.SessionId);
            if(rows.Count!=1 || rows[0].State!="Committed" ||
               rows[0].ReviewedDirectoryIdentity is null)return false;
            var row=rows[0];
            return w.Photos.Any(p=>p.Id==row.PhotoId &&
                SortingWorkspace.PathComparer.Equals(p.Path,row.Target)) &&
                DurableRenameCoordinator.ProveState(row);
        }
        catch{return false;}
    }
    public static void RequireFresh(WorkspaceSessionStore db,SortingWorkspace w,
        Photo photo,string proposed,Guid? pending=null)
    {
        if(!CanUseWorkspace(db,w) || db.HasUnreconciledCopy ||
           db.AllCopyHistoryCount()!=0 || db.AllMoveHistoryCount()!=0 ||
           db.AllRejectHistoryCount()!=0 || w.Current?.Id!=photo.Id ||
           photo.NativeIdentity is null || photo.Id==Guid.Empty)
            throw new IOException("Ordinary Rename requires one selected unassigned local flat-source photograph, no competing history.");
        var history=db.RenameItems(w.SessionId);
        if(db.AllRenameHistoryCount()!=history.Count ||
           !(history.Count==0 || (pending is { } id && history.Count==1 &&
              history[0].Id==id && history[0].State=="Intent")))
            throw new IOException("Ordinary first Rename requires fresh history or this exact durable pending intent.");
        var saved=db.Load() ?? throw new IOException("Ordinary Rename saved workspace is missing.");
        var live=w.CaptureSession();
        if(saved.SessionId!=live.SessionId ||
           (saved.Revision!=live.Revision &&
            !(saved.Revision<long.MaxValue-1 && live.Revision==saved.Revision+1)) ||
           JsonSerializer.Serialize(live with {Revision=saved.Revision})!=JsonSerializer.Serialize(saved))
            throw new IOException("Ordinary Rename saved checkpoint differs from live session; refuse unsaved changes.");
        if(!File.Exists(photo.Path) || SortingWorkspace.HasSymlinkOrReparsePoint(photo.Path) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(photo.Path)!))
            throw new IOException("Ordinary Rename source file is absent or linked.");
        var info=new FileInfo(photo.Path);
        if(info.Length!=photo.Length || info.LastWriteTimeUtc.Ticks!=photo.LastWriteUtcTicks ||
           NativeFileCapability.Identity(photo.Path)!=photo.NativeIdentity ||
           NativeFileCapability.DeviceId(photo.Path)!=NativeFileCapability.DeviceId(w.SourceRoot!))
            throw new IOException("Ordinary Rename source native identity, timestamps or device has changed.");
        string candidate=DurableRenameCoordinator.ValidateFilename(photo.Path,proposed);
        var sourceDir=Path.GetDirectoryName(photo.Path)!;
        string normalized=candidate.Normalize(NormalizationForm.FormC);
        if(Directory.EnumerateFileSystemEntries(sourceDir).Any(path=>
            string.Equals(Path.GetFileName(path).Normalize(NormalizationForm.FormC),
                normalized,StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Ordinary Rename candidate collides under portable case/Unicode rules.");
    }
    public static void RequireUndo(WorkspaceSessionStore db,Guid session)
    {
        if(db.HasUnreconciledCopy || db.AllCopyHistoryCount()!=0 ||
           db.AllMoveHistoryCount()!=0 || db.AllRejectHistoryCount()!=0 ||
           db.AllRenameHistoryCount()!=1)
            throw new IOException("Ordinary Rename Undo requires exactly one owned Rename and no mixed operation history.");
        var rows=db.RenameItems(session);
        if(rows.Count!=1 || rows[0].State!="Committed" ||
           rows[0].ReviewedDirectoryIdentity is null ||
           !DurableRenameCoordinator.ProveState(rows[0]))
            throw new IOException("Ordinary Rename Undo requires a fully verified committed native ownership record.");
    }
}
