using System.Text;

namespace PhotoSiftReborn;

/// <summary>
/// P6B-3E Windows NTFS experiment, explicitly opt-in only. This gate is not a
/// promise of atomicity under hostile third-party writers, power loss, network,
/// cloud-sync providers, or unsupported filters. Never falls back to path moves.
/// </summary>
public static class WindowsLocalRenamePolicy
{
    public const string OptInEnvironment = "PHOTOSIFT_P6B3E_WINDOWS_RENAME_TRIAL";
    public static bool Enabled => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable(OptInEnvironment)=="1";

    private static bool IsInside(string path,string directory)
    {
        var p=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var d=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return p.Equals(d,StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith(d+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }

    private static void LocalNtfs(string path, bool filePath = false)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal) ||
            path.StartsWith(@"\\?\",StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\",StringComparison.Ordinal))
            throw new IOException("UNC, extended or device paths are not supported for Windows Rename trial.");
        string full=Path.GetFullPath(path);
        string? root=Path.GetPathRoot(full);
        if (root is null || root.Length!=3 || root[1]!=':' || root[2]!='\\')
            throw new IOException("Only local drive-letter paths are permitted.");
        var drive=new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType!=DriveType.Fixed ||
            !drive.DriveFormat.Equals("NTFS",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Rename trial requires a fixed local NTFS volume.");
        // This routine is called for both directory paths and real file leaves
        // (SQLite journal / imported image). The ancestor walker is deliberately
        // a DIRECTORY-path validator and refuses a real file as a directory.
        // Always validate the parent for file leaves, while separately rejecting
        // linked/reparse FILE leaves themselves. Never swallow a missing file.
        if (filePath)
        {
            var attributes = File.GetAttributes(full); // missing/unreadable: fail closed
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Windows Rename trial requires an ordinary, non-linked file.");
        }
        var directoryToCheck = filePath ? Path.GetDirectoryName(full)! : full;
        if (SortingWorkspace.HasLinkedExistingAncestor(directoryToCheck))
            throw new IOException("Any reparse/symlink ancestor refuses Rename.");
    }

    public static void CheckStore(WorkspaceSessionStore db)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Production-style Rename trial is Windows-only.");
        if (!Enabled) throw new IOException("Windows Rename trial requires explicit environment opt-in.");
        LocalNtfs(db.DatabasePath, filePath: true);
        var folder=Path.GetDirectoryName(db.DatabasePath)!;
        if(SortingWorkspace.HasLinkedExistingAncestor(folder))
            throw new IOException("Journal has reparse/linked ancestors.");
    }

    public static void CheckWorkspace(WorkspaceSessionStore db,SortingWorkspace work)
    {
        CheckStore(db);
        var source=work.SourceRoot;
        var output=work.DestinationRoot;
        if(source is null || output is null || work.SourceRoots.Count!=1 ||
           !SortingWorkspace.PathComparer.Equals(source,work.SourceRoots[0]) ||
           !Directory.Exists(source) || !Directory.Exists(output))
            throw new IOException("Rename trial requires exactly one imported source folder and one separate output folder.");
        LocalNtfs(source);LocalNtfs(output);
        var state=Path.GetDirectoryName(db.DatabasePath)!;
        if(IsInside(state,source) || IsInside(source,state) || IsInside(output,source) ||
           IsInside(source,output) || IsInside(output,state) || IsInside(state,output))
            throw new IOException("Source, output and durable state directories must not overlap.");
        if(work.Photos.Any(photo=>!SortingWorkspace.PathComparer.Equals(
             Path.GetDirectoryName(photo.Path),source)))
            throw new IOException("Rename trial refuses recursive, cross-folder or mixed-source imports.");
    }

    public static void CheckPhoto(WorkspaceSessionStore db,SortingWorkspace work,Photo photo)
    {
        CheckWorkspace(db,work);
        if(!SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(photo.Path),work.SourceRoot!))
            throw new IOException("Photo is outside the single approved source directory.");
        LocalNtfs(photo.Path, filePath: true);
        if(SortingWorkspace.HasSymlinkOrReparsePoint(photo.Path))
            throw new IOException("Reparse, linked or cloud-placeholder file refused.");
        var attrs=File.GetAttributes(photo.Path);
        // Offline files, placeholders and special NTFS data layouts are not yet certified.
        if((attrs & (FileAttributes.Offline | FileAttributes.ReparsePoint |
             FileAttributes.Encrypted | FileAttributes.SparseFile))!=0)
            throw new IOException("Offline, reparse, encrypted or sparse file refused.");
        if(NativeFileCapability.DeviceId(photo.Path)!=NativeFileCapability.DeviceId(work.SourceRoot!))
            throw new IOException("Source device identity differs from approved folder.");
    }
}
