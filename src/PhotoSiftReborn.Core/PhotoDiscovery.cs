namespace PhotoSiftReborn;

/// <summary>
/// P4C single path-discovery entry point for pickers, folder selection, drop and CLI.
/// Reads metadata only. Does not mutate the workspace, database, or image files.
/// Returns a complete candidate that the caller commits only after cancellation checks.
/// </summary>
public sealed record PhotoDiscoveryReport(
    IReadOnlyList<Photo> Added,
    IReadOnlyList<string> Roots,
    int DuplicateCount, int UnsupportedCount, int UnsafeCount, int UnreadableCount,
    IReadOnlyList<string> Warnings)
{
    public string Summary => $"{Added.Count} added, {DuplicateCount} already present, " +
        $"{UnsupportedCount} unsupported, {UnsafeCount} linked/recovery skipped, {UnreadableCount} unreadable";
}

public static class PhotoDiscovery
{
    private const int MaximumPhotos = 100000;
    private const int MaximumDirectories = 50000;
    private static readonly string[] RecoveryNames = [".photosift-reborn-recovery", ".photosift-reborn-state"];
    private static readonly StringComparison Compare = SortingWorkspace.PathComparison;
    private static readonly StringComparer Comparer = SortingWorkspace.PathComparer;

    private static bool Within(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(path, normalizedRoot, Compare) ||
            path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, Compare);
    }

    private static bool IsRecoveryPath(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (RecoveryNames.Any(n => string.Equals(segment, n, StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    /// <summary>
    /// Supported extensions are authoritative; filename extension alone does not make
    /// an image decodable. The image decoder reports invalid bytes in the viewer.
    /// </summary>
    public static PhotoDiscoveryReport Discover(
        IEnumerable<string> inputs,
        IReadOnlyList<Photo> existing,
        string? outputRoot,
        bool recursive,
        Func<string, string>? identityResolver,
        CancellationToken cancel = default,
        IProgress<int>? progress = null)
    {
        var entries = inputs.ToArray();
        if (entries.Length == 0 || entries.Length > 4096)
            throw new ArgumentException("Choose between 1 and 4096 input files/folders.");
        var loaded = new List<Photo>();
        var roots = new List<string>();
        var knownPaths = new Dictionary<string,Photo>(Comparer);
        var knownIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var known in existing)
        {
            knownPaths.Add(known.Path, known);
            if (known.NativeIdentity is not null) knownIdentities.Add(known.NativeIdentity);
            else if (identityResolver is not null && File.Exists(known.Path))
            {
                // Old P4B sessions had no native identity field: only use live
                // identities for alias suppression when their recorded metadata
                // still agrees with the file on disk.
                try
                {
                    if (!SortingWorkspace.HasSymlinkOrReparsePoint(known.Path))
                    {
                        var info = new FileInfo(known.Path);
                        if (info.Length == known.Length && info.LastWriteTimeUtc.Ticks == known.LastWriteUtcTicks)
                            knownIdentities.Add(identityResolver(known.Path));
                    }
                }
                catch (IOException) { /* missing/changed old sources aren't trusted */ }
                catch (UnauthorizedAccessException) { /* retained, not silently overwritten */ }
            }
        }
        int duplicates=0, unsupported=0, unsafeCount=0, unreadable=0, visitedDirectories=0;
        var warnings = new List<string>();
        var seenDirs = new HashSet<string>(Comparer);
        var output = outputRoot is null ? null : Path.GetFullPath(outputRoot);
        void Warning(string warning)
        {
            if (warnings.Count < 12) warnings.Add(warning);
        }
        bool Excluded(string path) => IsRecoveryPath(path) ||
            (output is not null && Within(path,output));

        void InspectFile(string file)
        {
            cancel.ThrowIfCancellationRequested();
            if (Excluded(file)) { unsafeCount++; Warning("Excluded output/recovery: " + file); return; }
            if (!SortingWorkspace.Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            { unsupported++; return; }
            try
            {
                if (SortingWorkspace.HasSymlinkOrReparsePoint(file))
                { unsafeCount++; Warning("Skipped linked file: " + file); return; }
                if (knownPaths.TryGetValue(file,out var known))
                {
                    var info = new FileInfo(file);
                    if (!info.Exists || info.Length != known.Length || info.LastWriteTimeUtc.Ticks != known.LastWriteUtcTicks ||
                        (known.NativeIdentity is not null && identityResolver is not null &&
                         !string.Equals(known.NativeIdentity,identityResolver(file),StringComparison.Ordinal)))
                        throw new InvalidOperationException("Previously imported file changed or was replaced. Saved choice preserved; resolve the file before reimport: " + file);
                    duplicates++;
                    return;
                }
                var data = new FileInfo(file);
                if (!data.Exists) throw new FileNotFoundException("File disappeared during discovery",file);
                if (data.Length <= 0) { unreadable++; Warning("Empty image ignored: " + file); return; }
                string? identity;
                try { identity=identityResolver?.Invoke(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { throw new InvalidOperationException("Cannot establish physical identity for imported photograph: " + file,ex); }
                if (identity is not null && !knownIdentities.Add(identity))
                { duplicates++; Warning("Same physical file already in collection: " + file); return; }
                var photo = new Photo(file,data.Length,data.LastWriteTimeUtc.Ticks) { NativeIdentity=identity };
                knownPaths.Add(file,photo);
                loaded.Add(photo);
                if (loaded.Count + existing.Count > MaximumPhotos)
                    throw new InvalidOperationException("Too many photographs in one session (100000 maximum).");
                if (loaded.Count % 25 == 0) progress?.Report(loaded.Count);
            }
            catch (FileNotFoundException) { unreadable++; Warning("File disappeared during scan: " + file); }
            catch (UnauthorizedAccessException) { unreadable++; Warning("Access denied: " + file); }
            // Other IOException, including a known changed photo or native identity
            // failure, is a hard refusal: existing work remains untouched.
        }

        foreach (var raw in entries)
        {
            cancel.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Empty import path.");
            var path = Path.GetFullPath(raw);
            if (Excluded(path)) throw new IOException("Import path is inside the output or a recovery folder: " + path);
            if (SortingWorkspace.HasSymlinkOrReparsePoint(path))
                throw new IOException("Selected path contains a symbolic link/reparse point: " + path);
            bool isFile=File.Exists(path), isDir=Directory.Exists(path);
            if (!isFile && !isDir) throw new FileNotFoundException("Selected import path does not exist",path);
            var root = isDir ? path : Path.GetDirectoryName(path)!;
            if (output is not null && (Within(root,output) || Within(output,root)))
                throw new IOException("Import and output roots must be separate until nested-output ownership is certified: " + root);
            if (IsRecoveryPath(root)) throw new IOException("Recovery directories are never import roots: " + root);
            if (!roots.Contains(root,Comparer)) roots.Add(root);
            if (isFile) { InspectFile(path); continue; }
            var stack = new Stack<string>(); stack.Push(path);
            while (stack.Count != 0)
            {
                cancel.ThrowIfCancellationRequested();
                var directory=stack.Pop();
                if (!seenDirs.Add(directory)) continue;
                if (++visitedDirectories > MaximumDirectories)
                    throw new InvalidOperationException("Directory traversal limit reached (50000); cancel or choose a narrower folder.");
                if (Excluded(directory)) { unsafeCount++; Warning("Excluded output/recovery subtree: " + directory); continue; }
                if (SortingWorkspace.HasSymlinkOrReparsePoint(directory))
                { unsafeCount++; Warning("Skipped linked directory: " + directory); continue; }
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancel.ThrowIfCancellationRequested();
                        try
                        {
                            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                            { unsafeCount++; Warning("Skipped linked entry: " + entry); continue; }
                            if (Directory.Exists(entry))
                            {
                                if (recursive) stack.Push(Path.GetFullPath(entry));
                            }
                            else if (File.Exists(entry)) InspectFile(Path.GetFullPath(entry));
                            else { unreadable++; Warning("Unrecognized path type: " + entry); }
                        }
                        catch (FileNotFoundException) { unreadable++; Warning("Vanished entry: " + entry); }
                        catch (UnauthorizedAccessException) { unreadable++; Warning("Inaccessible entry: " + entry); }
                    }
                }
                catch (UnauthorizedAccessException) { unreadable++; Warning("Cannot list directory: " + directory); }
                catch (DirectoryNotFoundException) { unreadable++; Warning("Directory vanished: " + directory); }
                catch (IOException) { unreadable++; Warning("Cannot enumerate directory: " + directory); }
            }
        }
        cancel.ThrowIfCancellationRequested();
        loaded.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Path,b.Path));
        progress?.Report(loaded.Count);
        return new PhotoDiscoveryReport(loaded,roots,duplicates,unsupported,unsafeCount,unreadable,warnings);
    }
}

/// <summary>Literal process argv (no shell concatenation, no filename splitting).</summary>
public sealed record CliImportPaths(string[] Paths, bool Recursive)
{
    public static CliImportPaths Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("No CLI input paths.");
        var paths = new List<string>();
        bool recursive = false, afterEndOfOptions = false;
        foreach (string arg in args)
        {
            if (!afterEndOfOptions && arg == "--") { afterEndOfOptions=true; continue; }
            if (!afterEndOfOptions && arg == "--recursive") { recursive=true; continue; }
            if (!afterEndOfOptions && arg.StartsWith("--",StringComparison.Ordinal))
                throw new ArgumentException("Unknown command-line option: " + arg);
            if (string.IsNullOrWhiteSpace(arg)) throw new ArgumentException("Empty CLI path.");
            paths.Add(arg);
        }
        if (paths.Count == 0) throw new ArgumentException("No CLI file/folder paths.");
        return new CliImportPaths(paths.ToArray(),recursive);
    }
}
