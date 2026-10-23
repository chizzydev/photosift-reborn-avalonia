namespace PhotoSiftReborn;

/// <summary>Deliberate two-key, fixture-only GUI gate. Not an authorization for personal photographs.</summary>
public static class P5CGuiFixturePolicy
{
    public const string EnvName = "PHOTOSIFT_P5C_GUI_FIXTURE";
    private const string Marker = "P5C_GUI_TEST_ONLY.marker";
    public const string MarkerContents = "p5c-generated-photo-fixture-only";

    public static bool IsMarkedStore(WorkspaceSessionStore db)
    {
        try
        {
            var root = Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)?.FullName;
            var env = Environment.GetEnvironmentVariable(EnvName);
            if (root is null || string.IsNullOrWhiteSpace(env)) return false;
            if (!SortingWorkspace.PathComparer.Equals(Path.GetFullPath(env),Path.GetFullPath(root))) return false;
            if (!Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal)) return false;
            if (!SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(db.DatabasePath),Path.Combine(root,"state"))) return false;
            if (SortingWorkspace.HasLinkedExistingAncestor(root)) return false;
            return File.Exists(Path.Combine(root,Marker)) &&
                !SortingWorkspace.HasSymlinkOrReparsePoint(Path.Combine(root,Marker)) &&
                File.ReadAllText(Path.Combine(root,Marker))==MarkerContents &&
                File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")) &&
                File.ReadAllText(Path.Combine(root,"P3B2_TEST_ONLY.marker"))=="synthetic-interruption-only" &&
                Directory.Exists(Path.Combine(root,"source")) && Directory.Exists(Path.Combine(root,"output")) &&
                !SortingWorkspace.HasLinkedExistingAncestor(Path.Combine(root,"source")) &&
                !SortingWorkspace.HasLinkedExistingAncestor(Path.Combine(root,"output"));
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) {return false;}
    }

    public static void VerifyExactWorkspace(WorkspaceSessionStore db, SortingWorkspace work)
    {
        if (!IsMarkedStore(db)) throw new IOException("Move is only available inside a generated P5C GUI fixture.");
        string root=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        string source=Path.Combine(root,"source"), output=Path.Combine(root,"output");
        if (!SortingWorkspace.PathComparer.Equals(work.SourceRoot,source) ||
            !SortingWorkspace.PathComparer.Equals(work.DestinationRoot,output) ||
            work.SourceRoots.Count!=1 || work.Photos.Any(p =>
                !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Path),source)))
            throw new IOException("Move GUI fixture must use only generated source/ and output/ folders, with no additional imported files.");
    }
}

public enum ReviewOperationMode { Copy, Move }

/// <summary>Modes are an independent authority. A Copy approval is never a Move approval.</summary>
public sealed class P5COperationModeGate
{
    public ReviewOperationMode Selected { get; private set; } = ReviewOperationMode.Copy;
    public ReviewOperationMode? Approved { get; private set; }
    public void Select(ReviewOperationMode mode) { Selected=mode; Approved=null; }
    public void Revoke() => Approved=null;
    public void RecordApproval() => Approved=Selected;
    public bool CanApply(ReviewOperationMode mode, bool planApproved, bool anyMoveHistory, bool fixtureEnabled)
        => Approved==mode && Selected==mode && planApproved && !anyMoveHistory &&
           (mode==ReviewOperationMode.Copy || fixtureEnabled);
}
