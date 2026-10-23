using System.Text.Json;
using System.Text;

namespace PhotoSiftReborn;

/// <summary>
/// Strict fresh-installation/first-Move-only authority for a flat ordinary-photo
/// collection. This is not a blanket claim that arbitrary cloud/network/hostile
/// concurrent writers are safe. No marker or environment variable can opt in.
/// A separate exact reviewed plan AND an explicit GUI approval remain mandatory.
/// </summary>
public static class OrdinaryFirstMovePolicy
{
    private static bool Overlaps(string a,string b)
    {
        string rel=Path.GetRelativePath(a,b);
        string back=Path.GetRelativePath(b,a);
        static bool Inside(string rel)=>rel=="." || (!Path.IsPathRooted(rel) &&
            rel!=".." && !rel.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
        return Inside(rel) || Inside(back);
    }

    public static bool CanOffer(WorkspaceSessionStore db,SortingWorkspace w)
    {
        try
        {
            if (!NativeFileCapability.IsSupported || db.HasUnreconciledCopy ||
                db.AllCopyHistoryCount()!=0 || db.AllMoveHistoryCount()!=0 ||
                db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0 ||
                !db.AssessFreshMoveEpoch().IsFresh || w.SourceRoot is null ||
                w.DestinationRoot is null || w.SourceRoots.Count!=1 ||
                w.CollisionMode!=CollisionChoice.Refuse || w.RemovedCount!=0 ||
                w.HasAppliedCopies || w.PendingRejectCount!=0 ||
                !Directory.Exists(w.SourceRoot) || !Directory.Exists(w.DestinationRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(w.SourceRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(w.DestinationRoot) ||
                Overlaps(w.SourceRoot,w.DestinationRoot) ||
                !SortingWorkspace.PathComparer.Equals(w.SourceRoots[0],w.SourceRoot)) return false;
            string state=Path.GetDirectoryName(db.DatabasePath)!;
            if (Overlaps(state,w.SourceRoot) || Overlaps(state,w.DestinationRoot))return false;
            if(w.Photos.Count==0 || w.Photos.Any(p=>
                !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Path),w.SourceRoot)))return false;
            // Reject removable/network drives and known cloud-controlled folders.
            // For Linux, only the locally verified block-file formats listed
            // here are enabled; unsupported filesystems remain Copy-only.
            string inputRoot=Path.GetPathRoot(w.SourceRoot)!;
            string outputRoot=Path.GetPathRoot(w.DestinationRoot)!;
            var di=new DriveInfo(inputRoot);
            var outputDrive=new DriveInfo(outputRoot);
            if(di.DriveType!=DriveType.Fixed || outputDrive.DriveType!=DriveType.Fixed ||
                !di.IsReady || !outputDrive.IsReady ||
                !string.Equals(NativeFileCapability.DeviceId(w.SourceRoot),
                    NativeFileCapability.DeviceId(w.DestinationRoot),StringComparison.Ordinal))return false;
            if(OperatingSystem.IsWindows() &&
                (!string.Equals(di.DriveFormat,"NTFS",StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(outputDrive.DriveFormat,"NTFS",StringComparison.OrdinalIgnoreCase)))return false;
            if(OperatingSystem.IsLinux() &&
                (!new[]{"ext4","xfs","btrfs"}.Contains(di.DriveFormat,StringComparer.OrdinalIgnoreCase) ||
                 !new[]{"ext4","xfs","btrfs"}.Contains(outputDrive.DriveFormat,StringComparer.OrdinalIgnoreCase)))return false;
            // Conservatively disallow active cloud sync roots; this is an
            // explicitly unsupported environment, not a promise of detection
            // against every possible sync provider.
            foreach(string path in new[]{w.SourceRoot,w.DestinationRoot})
            {
                var segments=Path.GetFullPath(path).Split(Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries);
                if(segments.Any(x=>x.StartsWith("OneDrive",StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("Dropbox",StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("Google Drive",StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("iCloudDrive",StringComparison.OrdinalIgnoreCase)))return false;
            }
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or InvalidDataException){return false;}
    }

    public static void RequireExact(WorkspaceSessionStore db,SortingWorkspace w,PreparedCopy p)
    {
        if(!CanOffer(db,w) || !w.IsCurrentPlanApproved ||
            w.CurrentPlanId!=p.PlanId || w.SessionId!=p.SessionId ||
            w.Revision!=p.Revision || w.AssignmentCount!=1 ||
            w.SkippedReview.Count!=0)
            throw new IOException("Ordinary Move requires a single freshly approved local first-epoch plan.");
        var photo=w.Photos.SingleOrDefault(x=>x.Id==p.PhotoId)
            ?? throw new IOException("Reviewed photo is not in the active session.");
        if(photo.NativeIdentity is null || photo.NativeIdentity!=p.Identity ||
            photo.Length!=p.Length || photo.LastWriteUtcTicks!=p.WriteTicks ||
            !SortingWorkspace.PathComparer.Equals(photo.Path,p.Source) ||
            !SortingWorkspace.PathComparer.Equals(p.OutputRoot,w.DestinationRoot))
            throw new IOException("Original imported identity and reviewed paths disagree.");
        var route=w.Routes.SingleOrDefault(r=>r.Key==p.Key);
        if(route is null || route.Kind==RouteKind.Absolute)
            throw new IOException("Ordinary Move requires a relative or default destination route.");
        string dir=w.CheckedRouteDirectory(p.Key);
        if(!SortingWorkspace.PathComparer.Equals(p.Destination,
            Path.Combine(dir,Path.GetFileName(p.Source))) ||
           !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Destination),dir))
            throw new IOException("Move target differs from the exact ordinary reviewed route.");
        // Cross-platform portability: refuse case/Unicode aliases even when
        // Linux would regard two names as distinct. No overwrite is possible.
        if(Directory.Exists(dir))
        {
            var proposed=Path.GetFileName(p.Destination).Normalize(NormalizationForm.FormC);
            if(Directory.EnumerateFileSystemEntries(dir).Any(other=>
                string.Equals(Path.GetFileName(other).Normalize(NormalizationForm.FormC),
                    proposed,StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Move output is occupied by a case/Unicode alias.");
        }
        var saved=db.Load() ?? throw new IOException("The persisted Move snapshot is missing.");
        var live=w.CaptureSession();
        // One restore revision bump is normal. The ENTIRE rest of the snapshot
        // must still match, not merely the photo ID or plan revision.
        if(saved.SessionId!=live.SessionId ||
            (live.Revision!=saved.Revision &&
             !(saved.Revision<long.MaxValue-1 && live.Revision==saved.Revision+1)) ||
            JsonSerializer.Serialize(live with {Revision=saved.Revision})!=
                JsonSerializer.Serialize(saved))
            throw new IOException("Ordinary Move requires an exact durable saved checkpoint; unsaved edits refused.");
        if(!File.Exists(p.Source) || SortingWorkspace.HasSymlinkOrReparsePoint(p.Source) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(p.Source)!))
            throw new IOException("Ordinary Move source or its parent is absent/linked.");
    }
}
