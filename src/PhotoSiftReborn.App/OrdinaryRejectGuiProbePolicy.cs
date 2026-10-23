using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P6F2C: UI-only owner acceptance fence. NO production native Reject authority.
/// Deliberately independent from the P5C generated-fixture marker and the
/// ForOrdinaryWorkspace native coordinator: the latter does not require this
/// probe file or environment variable. This gate prevents *untested GUI*
/// from operating on arbitrary personal photographs.
/// </summary>
internal static class OrdinaryRejectGuiProbePolicy
{
    private const string Key="PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT";
    internal static bool IsRequested => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Key));
    internal static bool IsSecondEpochProbe
    {
        get
        {
            var request=Environment.GetEnvironmentVariable(Key);
            return !string.IsNullOrWhiteSpace(request) &&
                P6F2D2GeneratedScope.IsExactGeneratedRootName(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(request)));
        }
    }
    private static bool Exact(string a,string b) => SortingWorkspace.PathComparer.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));
    internal static string Require(WorkspaceSessionStore db,SortingWorkspace work)
    {
        var requested=Environment.GetEnvironmentVariable(Key);
        if(string.IsNullOrWhiteSpace(requested))
            throw new IOException("Ordinary Reject GUI probe is not explicitly enabled.");
        var root=Path.GetFullPath(requested);
        bool second=IsSecondEpochProbe;
        if(!(second ? P6F2D2GeneratedScope.IsExactGeneratedRootName(Path.GetFileName(root))
                       : Path.GetFileName(root).StartsWith("PHOTOSIFT_P6F2C_ORDINARY_GUI_PROBE_",StringComparison.Ordinal)) ||
           !Directory.Exists(root) || SortingWorkspace.HasLinkedExistingAncestor(root))
            throw new IOException("Ordinary GUI probe root is missing, linked, or not a unique synthetic probe.");
        var source=Path.Combine(root,"source");
        var output=Path.Combine(root,"output");
        var state=Path.Combine(root,"state");
        var sentinel=Path.Combine(output,"PROTECTED_SENTINEL.txt");
        var scope=Path.Combine(root,second ? "P6F2D2_PROBE_SCOPE.json" : "P6F2C_PROBE_SCOPE.json");
        if(!Directory.Exists(source) || !Directory.Exists(output) || !Directory.Exists(state) ||
           SortingWorkspace.HasLinkedExistingAncestor(source) ||
           SortingWorkspace.HasLinkedExistingAncestor(output) ||
           SortingWorkspace.HasLinkedExistingAncestor(state) ||
           !File.Exists(scope) || SortingWorkspace.HasSymlinkOrReparsePoint(scope) ||
           !File.Exists(sentinel) || SortingWorkspace.HasSymlinkOrReparsePoint(sentinel) ||
           File.Exists(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker")) ||
           File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")))
            throw new IOException("Ordinary GUI probe provenance or isolated folders are missing/linked.");
        var home=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
        if(home is null || !Exact(home,state) ||
           !Exact(Path.GetDirectoryName(db.DatabasePath)!,state))
            throw new IOException("Ordinary Reject GUI cannot use another application's saved workspace.");
        using var document=JsonDocument.Parse(File.ReadAllText(scope));
        var top=document.RootElement;
        if(!top.TryGetProperty("schema",out var schema) ||
           schema.ValueKind!=JsonValueKind.Number || !schema.TryGetInt32(out var schemaVersion) ||
           schemaVersion!=(second ? 2 : 1) ||
           !top.TryGetProperty("origin",out var origin) ||
           origin.ValueKind!=JsonValueKind.String ||
           origin.GetString()!=(second ? "P6F2D2_TWO_GENERATED_ONLY" : "P6F2C_SYNTHETIC_ONLY") ||
           !top.TryGetProperty("sentinel",out var recordedSentinel) ||
           recordedSentinel.ValueKind!=JsonValueKind.String ||
           recordedSentinel.GetString()!=File.ReadAllText(sentinel) ||
           !top.TryGetProperty(second ? "sha256_01" : "sha256",out var recordedDigest) ||
           recordedDigest.ValueKind!=JsonValueKind.String)
            throw new IOException("Ordinary Reject GUI scope/sentinel is not the generated probe.");
        string digest=recordedDigest.GetString()??"";
        if(digest.Length!=64 || !digest.All(Uri.IsHexDigit))
            throw new IOException("Probe digest invalid.");
        var photo01=Path.Combine(source,"photo_01.png");
        var photo02=Path.Combine(source,"photo_02.png");
        var otherHash=second && top.TryGetProperty("sha256_02",out var hash2) &&
            hash2.ValueKind==JsonValueKind.String ? hash2.GetString() : null;
        if(second && (otherHash is null || otherHash.Length!=64 || !otherHash.All(Uri.IsHexDigit)))
            throw new IOException("Second synthetic photo hash missing or invalid.");
        if(second) P6F2D2GeneratedScope.RequireGeneratedDigests(digest,otherHash!,
            recordedSentinel.GetString()??"");
        var secondSession=second && db.AllRejectHistoryCount()!=0 &&
            db.RejectItems().Count>=1 && work.Photos.Count==1 &&
            Exact(work.Photos[0].Path,photo02);
        if(work.SourceRoot is null || work.DestinationRoot is null ||
           work.SourceRoots.Count!=1 || !Exact(work.SourceRoots[0],source) ||
           !Exact(work.SourceRoot,source) || !Exact(work.DestinationRoot,output) ||
           work.Photos.Count!=(second ? secondSession ? 1 : 2 : 1) ||
           !Exact(work.Photos[0].Path,secondSession ? photo02 : photo01) ||
           (second && !secondSession && !Exact(work.Photos[1].Path,photo02)))
            throw new IOException("GUI probe permits only its exact generated saved workspace.");
        // Exact original bytes must never silently become a different photograph.
        // After a native cut the historical coordinator additionally proves the
        // absent source is owned by its immutable archive. No policy auto-restore.
        foreach(var (path,hash) in second
            ? new[]{(photo01,digest),(photo02,otherHash!)}
            : new[]{(photo01,digest)})
            if(File.Exists(path) &&
                (SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
                 !string.Equals(SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult(),
                    hash,StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Synthetic GUI original changed or is linked.");
        var topLevelPhotos=Directory.EnumerateFiles(source,"*",SearchOption.TopDirectoryOnly).ToArray();
        if(topLevelPhotos.Any(path=>!Exact(path,photo01) && (!second || !Exact(path,photo02))))
            throw new IOException("Unrecognized files inside synthetic GUI photo folder.");
        if(second && (Directory.EnumerateFileSystemEntries(output).Count()!=1 ||
            !Directory.EnumerateFileSystemEntries(output).Any(e=>Exact(e,sentinel)) ||
            Directory.EnumerateDirectories(source).Any(dir=>
                !Exact(dir,Path.Combine(source,".photosift-reborn-reject-recovery")))))
            throw new IOException("Second probe contains unrecognized output or source directory.");
        var rows=db.RejectItems();
        if(!File.Exists(photo01) && (rows.Count==0 || rows[0].State is not
            ("Committed" or "Intent" or "RelocateIntent" or "UndoIntent")))
            throw new IOException("First generated photo absent without an applicable native operation.");
        if(second && !File.Exists(photo02) &&
           (rows.Count!=2 || rows[1].State is not
            ("Committed" or "Intent" or "RelocateIntent" or "UndoIntent")))
            throw new IOException("Second generated photo absent without an applicable native operation.");
        if(second && secondSession &&
           !db.InspectSecondOrdinaryRejectGuiSession(work.CaptureSession()).Verified)
            throw new IOException("Second ordinary GUI checkpoint / predecessor receipt refused.");
        if(db.AllMoveHistoryCount()!=0 || db.AllRenameHistoryCount()!=0 ||
           db.HasUnreconciledCopy || db.CopyItems(work.SessionId).Count!=0)
            throw new IOException("Ordinary GUI cannot mix Copy, Move or Rename history.");
        return root;
    }
}
