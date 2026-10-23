using System.Security.Cryptography;

namespace PhotoSiftReborn;

public enum RouteKind { Relative = 0, Default = 1, Absolute = 2 }
// The two optional properties preserve JSON compatibility with pre-P4D Route(Key, Label).
// Label remains the human-facing text; Location is the validated relative path or
// native-picked fully qualified directory. Old P4C records deserialize as Relative.
public sealed record Route(char Key, string Label, RouteKind Kind = RouteKind.Relative, string? Location = null);
public sealed record Photo(string Path, long Length, long LastWriteUtcTicks)
{
    // Stable within a session; P3 persists this logical ID. Not proof of physical file ownership.
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? NativeIdentity { get; init; }
}
public sealed record SortPlanItem(string Source, string Destination, char Key,
    long ExpectedLength, long ExpectedWriteUtcTicks);
public sealed record AppliedCopy(string Source, string Destination, string Sha256,
    long Length, long InstalledWriteUtcTicks);
public sealed record CopyResult(int Copied, string? Error);

public enum CollisionChoice { Refuse, Number, Skip }
public sealed record SkippedCollision(string Source, string OriginalDestination, string Reason);
public sealed record SavedAssignment(Guid PhotoId, char Key);
public sealed record SavedDecision(Guid PhotoId, char? Before);
// A removed photo remains recoverable in the SAME saved session. This is NOT a file deletion.
public sealed record RemovedSessionPhoto(Photo Photo, int Position);
public sealed record WorkspaceSessionSnapshot(
    int Schema, Guid SessionId, string? SourceRoot, string? DestinationRoot,
    Photo[] Photos, Route[] Routes, SavedAssignment[] Assignments,
    SavedDecision[] Decisions, int Index, long Revision,
    string[]? SourceRoots = null, CollisionChoice CollisionMode = CollisionChoice.Refuse,
    RemovedSessionPhoto[]? RemovedPhotos = null, Guid[]? PendingRejectIds = null);


/// <summary>
/// Cross-platform, copy-only slice. No source mutations. This is not yet the
/// crash-safe, identity-aware copy/move/rename/recovery engine of the final port.
/// </summary>
public sealed class SortingWorkspace
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif"];
    private readonly List<Photo> _photos = [];
    private readonly List<string> _sourceRoots = [];
    private readonly List<SkippedCollision> _skippedReview = [];
    private readonly Dictionary<Guid, char> _assigned = [];
    private readonly List<(Guid PhotoId, char? Before)> _decisionUndo = [];
    private readonly List<AppliedCopy> _applied = [];
    // P4-R22-R2: P1 legacy in-memory Copy Undo must never forget an observed
    // external output conflict just because bytes and timestamps were later
    // restored. AppliedCopy histories are deliberately NOT restored on restart;
    // this latch does not grant persistent native ownership or recovery rights.
    private readonly HashSet<AppliedCopy> _uncertainAppliedCopies = [];

    private readonly List<RemovedSessionPhoto> _removedPhotos = [];
    // Review-only Reject candidates. These are NOT filesystem operations.
    // Ordered ids provide a durable LIFO undo; the set makes membership cheap.
    private readonly List<Guid> _pendingRejectOrder = [];
    private readonly HashSet<Guid> _pendingRejects = [];
    private readonly Dictionary<char, Route> _routes = [];
    private long _revision;
    private Guid _sessionId = Guid.NewGuid();
    public Guid SessionId => _sessionId;
    private long _reviewedRevision = -1;
    private List<SortPlanItem>? _reviewed;
    private Guid? _reviewedId;
    private Guid? _approvedId;

    public string? SourceRoot { get; private set; }
    public string? DestinationRoot { get; private set; }
    public IReadOnlyList<string> SourceRoots => _sourceRoots;
    public IReadOnlyList<SkippedCollision> SkippedReview => _skippedReview;
    public CollisionChoice CollisionMode { get; private set; } = CollisionChoice.Refuse;
    public IReadOnlyList<Photo> Photos => _photos;
    public IReadOnlyList<AppliedCopy> Applied => _applied;
    public int Index { get; private set; }
    public Photo? Current => Index >= 0 && Index < _photos.Count ? _photos[Index] : null;
    public bool AtEnd => _photos.Count > 0 && Index >= _photos.Count;
    public Guid? CurrentPlanId => _reviewedId;
    public bool IsCurrentPlanApproved => _reviewedId.HasValue && _reviewedId == _approvedId && _reviewedRevision == _revision;
    public int AssignmentCount => _assigned.Count;
    public int RemovedCount => _removedPhotos.Count;
    public int PendingRejectCount => _pendingRejectOrder.Count;
    public bool CanUndoPendingReject => _pendingRejectOrder.Count != 0;
    /// <summary>Ordered, read-only Reject-review queue. No native Reject journal authority.</summary>
    public IReadOnlyList<Photo> PendingRejectReviewPhotos
    {
        get
        {
            var byId=_photos.ToDictionary(p=>p.Id);
            return _pendingRejectOrder.Select(id=>byId.TryGetValue(id,out var photo)
                ? photo : throw new InvalidDataException("Reject-review queue references a missing photograph.")).ToArray();
        }
    }
    public bool IsPendingReject(Guid photoId) => _pendingRejects.Contains(photoId);
    public bool CanMarkCurrentForReject => Current is { } photo && _applied.Count == 0 &&
        !_pendingRejects.Contains(photo.Id) && !_assigned.ContainsKey(photo.Id) &&
        !_decisionUndo.Any(d => d.PhotoId == photo.Id);
    public bool CanUndoRemoval => _removedPhotos.Count > 0;
    public bool CanUndoDecision => _decisionUndo.Count > 0;
    public bool HasAppliedCopies => _applied.Count > 0;
    public long Revision => _revision;
    public Func<string,string>? IdentityResolver { get; set; }
    public PhotoDiscoveryReport? LastImportReport { get; private set; }
    public IReadOnlyList<Route> Routes => _routes.Values.OrderBy(x => x.Key).ToArray();

    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public SortingWorkspace()
    {
        SetRoute('F', "Family"); SetRoute('T', "Travel"); SetRoute('W', "Work");
    }

    /// <summary>Immutable-in-meaning saved work only: approvals and applied-file history are NOT restored.</summary>
    public WorkspaceSessionSnapshot CaptureSession() => new(
        4, _sessionId, SourceRoot, DestinationRoot, _photos.ToArray(),
        Routes.ToArray(), _assigned.Select(x => new SavedAssignment(x.Key, x.Value)).ToArray(),
        _decisionUndo.Select(x => new SavedDecision(x.PhotoId, x.Before)).ToArray(), Index, _revision,
        _sourceRoots.ToArray(), CollisionMode, _removedPhotos.ToArray(), _pendingRejectOrder.ToArray());

    /// <summary>Restore an earlier acknowledged decision state. Never authorize file writes on load.</summary>
    public void RestoreSession(WorkspaceSessionSnapshot snapshot)
    {
        if (snapshot.Schema is not (1 or 2 or 3 or 4) || snapshot.SessionId == Guid.Empty || snapshot.Revision < 0 ||
            snapshot.Revision == long.MaxValue || snapshot.Photos is null || snapshot.Routes is null ||
            snapshot.Assignments is null || snapshot.Decisions is null || snapshot.Index < 0 ||
            snapshot.Index > snapshot.Photos.Length || snapshot.Photos.Length > 1000000 ||
            snapshot.Routes.Length > 36 || snapshot.Assignments.Length > snapshot.Photos.Length ||
            snapshot.Decisions.Length > 1000000)
            throw new InvalidDataException("Invalid or unsupported saved sorting session. Original database preserved.");
        if ((snapshot.SourceRoot is null && snapshot.Photos.Length != 0) ||
            (snapshot.SourceRoot is not null && !Path.IsPathFullyQualified(snapshot.SourceRoot)) ||
            (snapshot.DestinationRoot is not null && !Path.IsPathFullyQualified(snapshot.DestinationRoot)))
            throw new InvalidDataException("Saved source/destination location is invalid.");
        if (snapshot.SourceRoot is not null && snapshot.DestinationRoot is not null &&
            (snapshot.SourceRoot.Equals(snapshot.DestinationRoot, PathComparison) ||
             IsDescendant(snapshot.SourceRoot, snapshot.DestinationRoot) ||
             IsDescendant(snapshot.DestinationRoot, snapshot.SourceRoot)))
            throw new InvalidDataException("Saved source and output overlap.");
        var roots = snapshot.Schema == 1
            ? (snapshot.SourceRoot is null ? Array.Empty<string>() : new[] { snapshot.SourceRoot })
            : snapshot.SourceRoots ?? Array.Empty<string>();
        if (roots.Length > 10000 || (snapshot.SourceRoot is null) != (roots.Length == 0) ||
            (snapshot.SourceRoot is not null && !PathComparer.Equals(snapshot.SourceRoot, roots[0])) ||
            !Enum.IsDefined(snapshot.CollisionMode))
            throw new InvalidDataException("Saved source roots or collision mode invalid.");
        for (int i=0;i<roots.Length;i++)
        {
            if (string.IsNullOrWhiteSpace(roots[i]) || !Path.IsPathFullyQualified(roots[i]) ||
                roots.Take(i).Any(r=>PathComparer.Equals(r,roots[i])) ||
                (snapshot.DestinationRoot is { } dest &&
                 (PathComparer.Equals(roots[i],dest) || IsDescendant(roots[i],dest) || IsDescendant(dest,roots[i]))))
                throw new InvalidDataException("Saved source roots conflict, overlap or escape.");
        }
        var removed = snapshot.RemovedPhotos ?? Array.Empty<RemovedSessionPhoto>();
        if (removed.Length > 1000000 - snapshot.Photos.Length ||
            (snapshot.Schema < 4 && removed.Length != 0))
            throw new InvalidDataException("Invalid removed-photo history in saved session.");
        var photoIds = new HashSet<Guid>();
        var paths = new HashSet<string>(PathComparer);
        foreach (var photo in snapshot.Photos)
        {
            if (photo is null || photo.Id == Guid.Empty || !photoIds.Add(photo.Id) ||
                string.IsNullOrWhiteSpace(photo.Path) || !Path.IsPathFullyQualified(photo.Path) ||
                !roots.Any(r=>IsDescendant(photo.Path,r)) ||
                !paths.Add(photo.Path) || photo.Length < 0 || photo.LastWriteUtcTicks < 0)
                throw new InvalidDataException("Saved photograph identities or locations are invalid.");
        }
        // Validate the complete reversible removal stack BEFORE modifying current state.
        // Removed IDs and paths cannot alias active photos or another removed item.
        for (int i = 0; i < removed.Length; i++)
        {
            var entry = removed[i];
            var photo = entry?.Photo;
            if (photo is null || photo.Id == Guid.Empty || !photoIds.Add(photo.Id) ||
                string.IsNullOrWhiteSpace(photo.Path) || !Path.IsPathFullyQualified(photo.Path) ||
                !roots.Any(root => IsDescendant(photo.Path, root)) || !paths.Add(photo.Path) ||
                photo.Length < 0 || photo.LastWriteUtcTicks < 0 ||
                entry!.Position < 0 || entry.Position > snapshot.Photos.Length + removed.Length)
                throw new InvalidDataException("Invalid or duplicate removed-photo snapshot.");
        }
        var activeIds = snapshot.Photos.Select(photo => photo.Id).ToHashSet();
        var newRoutes = new SortingWorkspace();
        newRoutes._routes.Clear();
        foreach (var route in snapshot.Routes)
        {
            if (route is null || newRoutes._routes.ContainsKey(route.Key))
                throw new InvalidDataException("Duplicate or invalid saved route.");
            newRoutes.DefineRoute(route);
        }
        var a = new Dictionary<Guid, char>();
        foreach (var assigned in snapshot.Assignments)
            if (!activeIds.Contains(assigned.PhotoId) || !newRoutes._routes.ContainsKey(assigned.Key) ||
                !a.TryAdd(assigned.PhotoId, assigned.Key))
                throw new InvalidDataException("Saved photo assignment refers to a missing photo or route.");
        foreach (var dec in snapshot.Decisions)
            if (!activeIds.Contains(dec.PhotoId) ||
                (dec.Before is { } key && !newRoutes._routes.ContainsKey(key)))
                throw new InvalidDataException("Saved Undo history refers to an unknown photo or route.");
        // Additive nullable field: old schema-4 and pre-schema-4 JSON loads unchanged.
        // Reject marking is only a reversible decision; it never claims a native archive.
        var pendingRejects = snapshot.PendingRejectIds ?? Array.Empty<Guid>();
        var decisionIds = snapshot.Decisions.Select(decision => decision.PhotoId).ToHashSet();
        if ((snapshot.Schema < 4 && pendingRejects.Length != 0) ||
            pendingRejects.Length > activeIds.Count ||
            pendingRejects.Distinct().Count() != pendingRejects.Length ||
            pendingRejects.Any(id => id == Guid.Empty || !activeIds.Contains(id) ||
                a.ContainsKey(id) || decisionIds.Contains(id)))
            throw new InvalidDataException("Saved Reject-review decisions conflict with photographs, assigned routes or Undo history.");
        if (_applied.Count != 0) throw new InvalidOperationException("Cannot restore over an applied operation.");
        // Validation completed before mutating active work.
        _sessionId = snapshot.SessionId;
        SourceRoot = snapshot.SourceRoot;
        DestinationRoot = snapshot.DestinationRoot;
        _sourceRoots.Clear(); _sourceRoots.AddRange(roots);
        CollisionMode = snapshot.Schema == 1 ? CollisionChoice.Refuse : snapshot.CollisionMode;
        _skippedReview.Clear();
        _photos.Clear(); _photos.AddRange(snapshot.Photos);
        _removedPhotos.Clear(); _removedPhotos.AddRange(removed);
        LastImportReport = null;
        _routes.Clear(); foreach (var r in newRoutes._routes.Values) _routes.Add(r.Key, r);
        _assigned.Clear(); foreach (var pair in a) _assigned.Add(pair.Key, pair.Value);
        _decisionUndo.Clear(); _decisionUndo.AddRange(snapshot.Decisions.Select(d => (d.PhotoId,d.Before)));
        _pendingRejectOrder.Clear(); _pendingRejectOrder.AddRange(pendingRejects);
        _pendingRejects.Clear(); foreach (var id in pendingRejects) _pendingRejects.Add(id);
        Index = snapshot.Index;
        _revision = snapshot.Revision + 1;  // recovery always invalidates any previous approval
        _reviewed = null; _reviewedRevision = -1; _reviewedId = null; _approvedId = null;
    }

    private void Invalidate() { _reviewed = null; _skippedReview.Clear(); _reviewedRevision = -1; _reviewedId = null; _approvedId = null; _revision++; }

    public void SetCollisionChoice(CollisionChoice choice)
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        if (CollisionMode != choice) { CollisionMode = choice; Invalidate(); }
    }

    // Retain the old single-segment entry point for saved F/T/W/P routes and tests.
    public void SetRoute(char key, string label) => SetRelativeRoute(key, label, singleSegment: true);

    public void SetDefaultRoute(char key) => DefineRoute(new Route(key, char.ToUpperInvariant(key).ToString(), RouteKind.Default));

    public void SetRelativeRoute(char key, string relative, bool singleSegment = false)
    {
        string[] parts = CheckRelativeSegments(relative, singleSegment);
        string path = string.Join(Path.DirectorySeparatorChar, parts);
        DefineRoute(new Route(key, path, RouteKind.Relative, path));
    }

    public void SetAbsoluteRoute(char key, string nativePickedDirectory)
    {
        if (string.IsNullOrWhiteSpace(nativePickedDirectory) ||
            !Path.IsPathFullyQualified(nativePickedDirectory))
            throw new ArgumentException("Choose a fully qualified destination directory using the native folder picker.");
        string full = Path.GetFullPath(nativePickedDirectory);
        if (!Directory.Exists(full) || HasLinkedExistingAncestor(full))
            throw new IOException("Absolute route must be an existing, non-linked directory.");
        if (Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is null)
            throw new IOException("Choose a subdirectory rather than a filesystem root for recoverable Copy Undo.");
        if (_sourceRoots.Any(root => PathComparer.Equals(full,root) ||
            IsDescendant(full,root) || IsDescendant(root,full)))
            throw new IOException("Absolute destination must be separate from all imported source roots.");
        if (full.Split(Path.DirectorySeparatorChar).Any(part =>
            part.Equals(".photosift-reborn-recovery",StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Recovery directories cannot be configured as destinations.");
        DefineRoute(new Route(key, full, RouteKind.Absolute, full));
    }

    private static string[] CheckRelativeSegments(string relative, bool singleSegment = false)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 320 ||
            Path.IsPathFullyQualified(relative) || relative.StartsWith('/') || relative.StartsWith('\\'))
            throw new ArgumentException("Enter a relative destination such as Family/Holidays, not a rooted path.");
        string[] parts = relative.Split(['/', '\\'], StringSplitOptions.None);
        if (singleSegment && parts.Length != 1)
            throw new ArgumentException("Use a single folder name for this route.");
        if (parts.Length > 8 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Length > 64 ||
            part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.Contains(':') || part.IndexOfAny(['*','?','"','<','>','|']) >= 0 ||
            part.Any(char.IsControl) || IsReservedWindowsName(part)))
            throw new ArgumentException("Invalid relative folder component; remove dot, parent, reserved, or special path segments.");
        return parts;
    }

    private void DefineRoute(Route route)
    {
        char key = char.ToUpperInvariant(route.Key);
        if (!IsValidKey(key)) throw new ArgumentException("Key must be A-Z or 0-9.");
        if (!Enum.IsDefined(route.Kind)) throw new ArgumentException("Unknown saved route type.");
        Route valid;
        if (route.Kind == RouteKind.Default)
        {
            if (route.Label != key.ToString() || route.Location is not null)
                throw new ArgumentException("Default routes must use their own key-named folder.");
            valid = new Route(key, key.ToString(), RouteKind.Default);
        }
        else if (route.Kind == RouteKind.Absolute)
        {
            string location = route.Location ?? throw new ArgumentException("Absolute route missing directory.");
            if (!Path.IsPathFullyQualified(location) || !Path.IsPathFullyQualified(route.Label) ||
                !PathComparer.Equals(Path.GetFullPath(location), Path.GetFullPath(route.Label)))
                throw new ArgumentException("Absolute route path and label disagree.");
            string full = Path.GetFullPath(location);
            // Restore validates the saved PATH, not its current existence; missing paths
            // must survive and be refused at Review, never silently rewritten.
            if (Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)) is null)
                throw new ArgumentException("Absolute route cannot be the filesystem root.");
            valid = new Route(key, full, RouteKind.Absolute, full);
        }
        else
        {
            string[] parts = CheckRelativeSegments(route.Location ?? route.Label);
            string normalized = string.Join(Path.DirectorySeparatorChar, parts);
            if (!PathComparer.Equals(normalized, route.Label.Replace('/',Path.DirectorySeparatorChar).Replace('\\',Path.DirectorySeparatorChar)))
                throw new ArgumentException("Saved relative route path and label disagree.");
            valid = new Route(key, normalized, RouteKind.Relative, normalized);
        }
        // Compare effective relative destinations, NOT just route Kind. A Default
        // key '9' resolves to the same folder as a Relative route labelled '9'.
        // Pre-P4D behavior refused this ambiguous alias even when the keys differ.
        // Preserve that fail-closed rule for both saved routes and implicit keys.
        // Absolute routes are compared with other Absolute routes here; Review
        // additionally checks exact resolved destinations and source aliases.
        if (_routes.Values.Any(x => x.Key != key &&
            (x.Kind == RouteKind.Absolute || valid.Kind == RouteKind.Absolute
                ? x.Kind == valid.Kind && x.Label.Equals(valid.Label, StringComparison.OrdinalIgnoreCase)
                : string.Equals(
                    x.Kind == RouteKind.Default ? x.Key.ToString() : x.Location ?? x.Label,
                    valid.Kind == RouteKind.Default ? valid.Key.ToString() : valid.Location ?? valid.Label,
                    StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Destination routes must be distinct.");
        _routes[key] = valid;
        Invalidate();
    }

    // Readable destination preview also used by the UI. Only Review/Apply
    // authorize writes; this function never creates directories.
    public string ExpandRouteDirectory(char key)
    {
        if (!_routes.TryGetValue(char.ToUpperInvariant(key), out var route))
            throw new IOException("Destination route is missing.");
        string folder = route.Kind switch
        {
            RouteKind.Absolute => Path.GetFullPath(route.Location!),
            RouteKind.Default => Path.Combine(DestinationRoot ?? throw new IOException("Choose an output root."),route.Key.ToString()),
            RouteKind.Relative => Path.Combine(DestinationRoot ?? throw new IOException("Choose an output root."),route.Location ?? route.Label),
            _ => throw new IOException("Invalid destination route type.")
        };
        return Path.GetFullPath(folder);
    }

    public string CheckedRouteDirectory(char key)
    {
        var folder = ExpandRouteDirectory(key);
        if (DestinationRoot is null || !Directory.Exists(DestinationRoot) || HasLinkedExistingAncestor(DestinationRoot))
            throw new IOException("Chosen output root is missing or linked. Choose a safe output folder.");
        if (_routes[key].Kind == RouteKind.Absolute && !Directory.Exists(folder))
            throw new IOException("Absolute destination folder no longer exists. Choose it again: " + folder);
        if (HasLinkedExistingAncestor(folder))
            throw new IOException("Destination contains a symbolic link or reparse point: " + folder);
        if (folder.Split(Path.DirectorySeparatorChar).Any(part =>
            part.Equals(".photosift-reborn-recovery",StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Recovery directories cannot be configured as destinations.");
        if (_sourceRoots.Any(root => PathComparer.Equals(folder,root) ||
                IsDescendant(folder,root) || IsDescendant(root,folder)))
            throw new IOException("Destination overlaps an imported source. Choose a separate folder: " + folder);
        if (_routes[key].Kind != RouteKind.Absolute &&
            !(IsDescendant(folder,DestinationRoot) && !PathComparer.Equals(folder, DestinationRoot)))
            throw new IOException("Relative route escaped the chosen output root.");
        return folder;
    }

    public static bool HasLinkedExistingAncestor(string path)
    {
        // Inspect every existing segment; a missing child is allowed for
        // future relative output creation, but a missing absolute root is not.
        for (DirectoryInfo? d = new DirectoryInfo(Path.GetFullPath(path)); d is not null; d=d.Parent)
        {
            if (File.Exists(d.FullName)) throw new IOException("A destination ancestor is a file: " + d.FullName);
            if (d.LinkTarget is not null || (d.Exists && (d.Attributes & FileAttributes.ReparsePoint)!=0)) return true;
        }
        return false;
    }

    private static bool IsReservedWindowsName(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";
    }

    public static bool IsValidKey(char key) => key is >= 'A' and <= 'Z' or >= '0' and <= '9';

    public void RemoveRoute(char key)
    {
        key = char.ToUpperInvariant(key);
        if (_assigned.Values.Any(x => x == key))
            throw new InvalidOperationException("Undo or change assignments using this key before removing it.");
        if (_routes.Remove(key)) Invalidate();
    }

    public static bool HasSymlinkOrReparsePoint(string path)
    {
        for (var p = new DirectoryInfo(Path.GetFullPath(path)); p is not null; p = p.Parent)
        {
            if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        }
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    public void OpenSource(string directory, CancellationToken cancel = default, IProgress<int>? progress = null, bool recursive = false)
        => ImportPaths([directory], true, recursive, cancel, progress);

    /// <summary>Backwards-compatible single-folder Add; all paths use the same discovery service.</summary>
    public int AddSourceFolder(string directory, CancellationToken cancel = default, IProgress<int>? progress = null, bool recursive = false)
        => ImportPaths([directory], false, recursive, cancel, progress).Added.Count;

    /// <summary>
    /// Replace only on explicit Open; Add files/folders, drag/drop and CLI use replace=false.
    /// Scanning does not modify the session; cancel/failure cannot erase staged work.
    /// Existing physical identities and changed-path evidence are conservatively checked.
    /// </summary>
    public PhotoDiscoveryReport ImportPaths(IEnumerable<string> paths, bool replace = false, bool recursive = false,
        CancellationToken cancel = default, IProgress<int>? progress = null)
    {
        if (replace && _applied.Count > 0)
            throw new InvalidOperationException("Cannot replace a collection with unresolved applied history.");
        var discovered = PhotoDiscovery.Discover(paths,
            replace ? Array.Empty<Photo>() : _photos.Concat(_removedPhotos.Select(x=>x.Photo)).ToArray(),
            DestinationRoot,recursive,IdentityResolver,cancel,progress);
        cancel.ThrowIfCancellationRequested();
        if (replace)
        {
            _photos.Clear(); _photos.AddRange(discovered.Added);
            _sourceRoots.Clear(); _sourceRoots.AddRange(discovered.Roots);
            SourceRoot = _sourceRoots.FirstOrDefault();
            _assigned.Clear(); _decisionUndo.Clear(); _removedPhotos.Clear();
            _pendingRejects.Clear(); _pendingRejectOrder.Clear();
            Index = 0;
            Invalidate();
        }
        else if (discovered.Added.Count != 0)
        {
            int prior = _photos.Count;
            _photos.AddRange(discovered.Added);
            foreach (var root in discovered.Roots)
                if (!_sourceRoots.Contains(root,PathComparer)) _sourceRoots.Add(root);
            SourceRoot ??= _sourceRoots.FirstOrDefault();
            if (Index >= prior) Index = prior;
            Invalidate();
        }
        LastImportReport = discovered;
        return discovered;
    }

    public void SetDestinationRoot(string directory)
    {
        if (_applied.Count > 0) throw new InvalidOperationException("Undo or resolve applied copies before switching output folders.");
        var full = Path.GetFullPath(directory);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        if (HasSymlinkOrReparsePoint(full)) throw new IOException("Destination folder includes a symbolic link/reparse point.");
        if (_sourceRoots.Any(src=>PathComparer.Equals(full,src) ||
             IsDescendant(full,src) || IsDescendant(src,full)))
            throw new IOException("Select an output folder separate from the source tree.");
        DestinationRoot = full;
        Invalidate();
    }

    private static bool IsDescendant(string path, string parent)
    {
        string stem = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(stem, PathComparison);
    }

    public void Navigate(int delta)
    {
        if (_photos.Count > 0) Index = Math.Clamp(Index + delta, 0, _photos.Count);
    }

    /// <summary>Jump to a visible photo (1-based). Cursor-only; existing plan authorization is unchanged.</summary>
    public void JumpToPhoto(int number)
    {
        if (number < 1 || number > _photos.Count)
            throw new ArgumentOutOfRangeException(nameof(number), "Choose a photograph number in the collection.");
        Index = number - 1;
    }

    /// <summary>Find the next unsorted photo, wrapping once. Does not change saved assignments.</summary>
    public bool JumpToNextUnassigned()
    {
        if (_photos.Count == 0) return false;
        int start = Index >= _photos.Count ? -1 : Index;
        for (int step = 1; step <= _photos.Count; step++)
        {
            int candidate = (start + step + _photos.Count) % _photos.Count;
            if (_assigned.ContainsKey(_photos[candidate].Id) || _pendingRejects.Contains(_photos[candidate].Id)) continue;
            Index = candidate;
            return true;
        }
        return false;
    }

    /// <summary>Fisher-Yates order change; stable Photo IDs and decisions remain attached.
    /// This revokes any reviewed plan because item order is part of its snapshot.</summary>
    public void ShufflePhotos(int? seed = null)
    {
        if (_photos.Count < 2) return;
        Guid? selected = Current?.Id;
        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        for (int i = _photos.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (_photos[i], _photos[j]) = (_photos[j], _photos[i]);
        }
        Index = selected is null ? _photos.Count : _photos.FindIndex(p => p.Id == selected.Value);
        if (Index < 0) Index = 0;
        Invalidate();
    }

    /// <summary>Hide one unassigned photo from this session; do not touch its file.
    /// A photo with staged history must first be undone/cleared to avoid orphaned decisions.</summary>
    public void RemoveCurrentFromSession()
    {
        var current = Current ?? throw new InvalidOperationException("Choose a photograph to remove from this session.");
        if (_applied.Count != 0)
            throw new InvalidOperationException("Resolve applied copies before changing session membership.");
        if (_pendingRejects.Contains(current.Id))
            throw new InvalidOperationException("Undo the Reject-review mark before removing this photograph.");
        if (_assigned.ContainsKey(current.Id) || _decisionUndo.Any(d => d.PhotoId == current.Id))
            throw new InvalidOperationException("Undo or clear this photo's staged decision history before removing it.");
        _removedPhotos.Add(new RemovedSessionPhoto(current, Index));
        _photos.RemoveAt(Index);
        if (Index > _photos.Count) Index = _photos.Count;
        Invalidate();
    }

    /// <summary>Restore the most recently removed photo at its prior position, with its ID intact.</summary>
    public void UndoLastSessionRemoval()
    {
        if (_removedPhotos.Count == 0)
            throw new InvalidOperationException("No removed session photo to restore.");
        var entry = _removedPhotos[^1];
        if (_photos.Any(p => p.Id == entry.Photo.Id || PathComparer.Equals(p.Path, entry.Photo.Path)))
            throw new IOException("Removal undo conflicts with a photo already present; session unchanged.");
        _photos.Insert(Math.Min(entry.Position, _photos.Count), entry.Photo);
        Index = _photos.FindIndex(p => p.Id == entry.Photo.Id);
        _removedPhotos.RemoveAt(_removedPhotos.Count - 1);
        Invalidate();
    }

    public char? CategoryFor(string path)
    {
        var photo = _photos.FirstOrDefault(p => PathComparer.Equals(p.Path, path));
        return photo is not null && _assigned.TryGetValue(photo.Id, out var key) ? key : null;
    }

    public void Assign(char key)
    {
        key = char.ToUpperInvariant(key);
        if (!IsValidKey(key)) throw new ArgumentOutOfRangeException(nameof(key), "Choose a letter A-Z or digit 0-9.");
        if (Current is not { } photo) return;
        if (_pendingRejects.Contains(photo.Id))
            throw new InvalidOperationException("This photograph is marked for Reject review. Undo that mark before assigning a route.");
        // Original PhotoSift can sort with any A-Z/0-9 key out of the box.
        // Materialize a default key-named route only when used so existing
        // P3 saved sessions, their custom paths and schema remain unchanged.
        // The route is persisted with the decision in the next SQLite save.
        // A conflicting custom label fails closed rather than aliasing a route.
        if (!_routes.ContainsKey(key)) SetDefaultRoute(key);
        var prev = CategoryFor(photo.Path);
        _decisionUndo.Add((photo.Id, prev));
        _assigned[photo.Id] = key;
        Invalidate();
        // Completion is its own state, not a second opportunity to reassign the final photo.
        Index = Math.Min(Index + 1, _photos.Count);
    }

    /// <summary>
    /// Stage up to 100 deliberately reviewed, previously undecided photographs
    /// under one EXISTING route. This is a decision-only operation: it does not
    /// inspect or mutate original files, construct a file-operation grant, or
    /// authorize Copy/Move/Reject/Rename. Validate the complete set first;
    /// no earlier photograph changes if any requested identity is unsuitable.
    /// Requiring the previously reviewed session/revision prevents stale
    /// collection dialogs from assigning another session's photographs.
    /// </summary>
    public void AssignReviewedBatch(IReadOnlyList<Guid> photoIds, char routeKey,
        Guid expectedSessionId, long expectedRevision)
    {
        if (expectedSessionId != _sessionId || expectedRevision != _revision)
            throw new InvalidOperationException("Saved collection changed since batch review; no decisions changed.");
        if (photoIds is null || photoIds.Count is < 1 or > 100 || _applied.Count != 0)
            throw new InvalidOperationException("Batch must contain 1–100 unapplied photographs.");
        routeKey = char.ToUpperInvariant(routeKey);
        if (!IsValidKey(routeKey) || !_routes.ContainsKey(routeKey))
            throw new InvalidOperationException("Choose an already configured destination route.");
        var known = _photos.Select(p => p.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        var historic = _decisionUndo.Select(x => x.PhotoId).ToHashSet();
        foreach (var id in photoIds)
        {
            if (id == Guid.Empty || !seen.Add(id) || !known.Contains(id) ||
                _assigned.ContainsKey(id) || _pendingRejects.Contains(id) || historic.Contains(id))
                throw new InvalidOperationException("Batch contains a missing, duplicate or previously decided photograph. Nothing changed.");
        }
        // Only after the entire batch was accepted may it change any choice.
        foreach (var id in photoIds)
        {
            _assigned.Add(id, routeKey);
            _decisionUndo.Add((id, null));
        }
        Invalidate(); // single revision change, invalidate all Copy review/approval
        // Deliberately do not move the photo-viewer cursor or change any file.
    }

    public void UndoDecision()
    {
        if (_decisionUndo.Count == 0) throw new InvalidOperationException("No staged assignment to undo.");
        var (photoId, previous) = _decisionUndo[^1]; _decisionUndo.RemoveAt(_decisionUndo.Count - 1);
        if (previous is { } key) _assigned[photoId] = key;
        else _assigned.Remove(photoId);
        int position = _photos.FindIndex(p => p.Id == photoId);
        if (position >= 0) Index = position;
        Invalidate();
    }

    /// <summary>
    /// Reversible, persisted Reject-review CHOICE ONLY. No file move, recycle,
    /// deletion or journal authorization occurs here. The future reviewed
    /// Reject-to-recovery engine requires its own explicit authorization.
    /// </summary>
    public void MarkCurrentForRejectReview()
    {
        var photo = Current ?? throw new InvalidOperationException("Choose a photograph for Reject review.");
        if (_applied.Count != 0 || _assigned.ContainsKey(photo.Id) ||
            _decisionUndo.Any(decision => decision.PhotoId == photo.Id) ||
            _pendingRejects.Contains(photo.Id))
            throw new InvalidOperationException("Only an untouched, unassigned photograph may be marked for Reject review. Undo previous decisions first.");
        _pendingRejects.Add(photo.Id);
        _pendingRejectOrder.Add(photo.Id);
        Invalidate();
        Index = Math.Min(Index + 1, _photos.Count);
    }

    /// <summary>
    /// Clear ONE selected review mark by its stable imported photo identity,
    /// without changing other marks, reordering photographs or touching files.
    /// Distinct from authorizing/executing a native Reject operation.
    /// </summary>
    public void UnmarkRejectReview(Guid photoId)
    {
        if(photoId==Guid.Empty || _applied.Count!=0 || !_pendingRejects.Contains(photoId))
            throw new InvalidOperationException("Selected photograph is not eligible for Reject-review unmarking.");
        int position=_photos.FindIndex(p=>p.Id==photoId);
        if(position<0 || _assigned.ContainsKey(photoId) ||
           _decisionUndo.Any(decision=>decision.PhotoId==photoId) ||
           _pendingRejectOrder.Count(x=>x==photoId)!=1)
            throw new InvalidDataException("Reject-review queue identity is invalid; no mark was changed.");
        _pendingRejectOrder.Remove(photoId);
        _pendingRejects.Remove(photoId);
        Index=position;
        Invalidate(); // revoke any previously reviewed/approved Copy plan
    }

    /// <summary>Undo the most recent staged Reject mark, restoring the same photo ID.</summary>
    public void UndoLastRejectReview()
    {
        if (_pendingRejectOrder.Count == 0)
            throw new InvalidOperationException("No Reject-review mark to undo.");
        var id = _pendingRejectOrder[^1];
        int position = _photos.FindIndex(photo => photo.Id == id);
        if (position < 0 || !_pendingRejects.Contains(id))
            throw new InvalidDataException("Reject-review history references a missing photo; original state preserved.");
        _pendingRejectOrder.RemoveAt(_pendingRejectOrder.Count-1);
        _pendingRejects.Remove(id);
        Index = position;
        Invalidate();
    }

    /// <summary>Reconsider ONE assigned photograph by its stable session ID.
    /// Does not touch files, journals, other assignments or other review marks.
    /// The associated undo history is removed so later LIFO undo cannot resurrect
    /// a cleared decision. Invalidate revokes a reviewed or approved Copy plan.
    /// Filesystem history locks are ALSO enforced by the GUI before this call.
    /// </summary>
    public void ClearSelectedAssignment(Guid photoId)
    {
        if (photoId == Guid.Empty || _applied.Count != 0 ||
            !_photos.Any(p => p.Id == photoId) || !_assigned.ContainsKey(photoId) ||
            _pendingRejects.Contains(photoId))
            throw new InvalidOperationException("Selected assigned photograph cannot be cleared. No decision changed.");
        if (_decisionUndo.Count(x => x.PhotoId == photoId) == 0)
            throw new InvalidDataException("Selected assignment has no matching undo history; saved choices preserved.");
        int position = _photos.FindIndex(p => p.Id == photoId);
        _assigned.Remove(photoId);
        _decisionUndo.RemoveAll(x => x.PhotoId == photoId);
        Index = position;
        Invalidate();
    }

    /// <summary>
    /// Decision-only mutation for a route restored by a fully undone durable
    /// Copy. It has no ordinary decision-undo entry because RetireCommitted
    /// consumed that history. This method is NOT itself file/journal authority:
    /// CopyUndoChoiceEditor must freshly prove terminal native Copy ownership,
    /// exact saved checkpoint and the specific latest restored route first.
    /// Never use it for ordinary assignments with existing decision history.
    /// </summary>
    public void ClearNativeVerifiedRestoredCopyChoice(Guid photoId, char expectedKey)
    {
        if (photoId==Guid.Empty || _applied.Count!=0 ||
            !_photos.Any(photo=>photo.Id==photoId) ||
            !_assigned.TryGetValue(photoId,out var current) || current!=expectedKey ||
            !_routes.ContainsKey(expectedKey) || _pendingRejects.Contains(photoId) ||
            _decisionUndo.Any(decision=>decision.PhotoId==photoId))
            throw new InvalidOperationException("Recovered Copy choice is not a pristine restored assignment. No change made.");
        int position=_photos.FindIndex(photo=>photo.Id==photoId);
        _assigned.Remove(photoId);
        Index=position;
        Invalidate(); // revoke every previous plan and approval
    }

    public void ClearAssignments() { _assigned.Clear(); _decisionUndo.Clear(); Invalidate(); }

    public IReadOnlyList<SortPlanItem> ReviewPlan()
    {
        if (SourceRoot is null || DestinationRoot is null)
            throw new InvalidOperationException("Select source and separate destination folders first.");
        if (_assigned.Count == 0) throw new InvalidOperationException("Assign at least one photo first.");
        if (!Directory.Exists(DestinationRoot) || HasLinkedExistingAncestor(DestinationRoot))
            throw new IOException("Destination root no longer exists or has changed type.");
        var plan=new List<SortPlanItem>();
        var skipped=new List<SkippedCollision>();
        var reserved=new HashSet<string>(PathComparer);
        foreach (var photo in _photos)
        {
            if (!_assigned.TryGetValue(photo.Id,out var key)) continue;
            if (!_routes.TryGetValue(key,out var route)) throw new IOException("Route was removed.");
            VerifySource(photo);
            string folder = CheckedRouteDirectory(key);
            var original=Path.Combine(folder,Path.GetFileName(photo.Path));
            string destination=original;
            if (PathComparer.Equals(photo.Path, original))
                throw new IOException("Destination aliases the source photograph.");
            bool collision=File.Exists(destination) || Directory.Exists(destination) || reserved.Contains(destination);
            if (collision)
            {
                if (CollisionMode == CollisionChoice.Refuse)
                    throw new IOException("Destination collision — choose Number or Skip before reviewing: " + original);
                if (CollisionMode == CollisionChoice.Skip)
                { skipped.Add(new SkippedCollision(photo.Path,original,"Already present or assigned by another staged item")); continue; }
                if (CollisionMode != CollisionChoice.Number) throw new IOException("Unknown collision mode — refusing Apply.");
                string stem=Path.GetFileNameWithoutExtension(original);
                string ext=Path.GetExtension(original);
                bool chosen=false;
                for (int n=2;n<=10000;n++)
                {
                    string candidate=Path.Combine(folder,$"{stem} ({n}){ext}");
                    if (File.Exists(candidate) || Directory.Exists(candidate) || reserved.Contains(candidate)) continue;
                    destination=candidate; chosen=true; break;
                }
                if (!chosen) throw new IOException("Unable to allocate a reviewed destination number for " + original);
            }
            if (!reserved.Add(destination)) throw new IOException("Two photos share the exact destination.");
            plan.Add(new SortPlanItem(photo.Path,destination,key,photo.Length,photo.LastWriteUtcTicks));
        }
        if (plan.Count==0) throw new IOException("All selected destinations collide. Nothing to approve; choose Number or change the routes.");
        _skippedReview.Clear(); _skippedReview.AddRange(skipped);
        _reviewed=plan;
        _reviewedRevision=_revision;
        _reviewedId=Guid.NewGuid();
        _approvedId=null;
        return plan;
    }

    /// <summary>Only the explicit user approval event may authorize Apply for this plan/revision.</summary>
    public void ApprovePlan(Guid planId, long revision)
    {
        if (_reviewed is null || _reviewedId != planId || _reviewedRevision != revision || _revision != revision)
            throw new IOException("The reviewed plan changed. Review the exact destinations again.");
        _approvedId = planId;
    }

    public void RevokeApproval() => _approvedId = null;

    /// <summary>Frozen, explicitly approved paths only; caller must separately prove source bytes.</summary>
    public IReadOnlyList<SortPlanItem> GetApprovedPlan()
    {
        if (_reviewed is null || !IsCurrentPlanApproved)
            throw new IOException("The current plan is not explicitly approved.");
        return _reviewed.ToArray();
    }

    // The journal is the authority for committed items. If the process crashed after
    // commit but before a session save, it may safely replay this idempotent retirement.
    public void RetireCommitted(IEnumerable<Guid> ids)
    {
        var completed = ids.ToHashSet();
        if (completed.Count == 0) return;
        foreach (var id in completed) _assigned.Remove(id);
        _decisionUndo.RemoveAll(x => completed.Contains(x.PhotoId));
        Invalidate();
    }

    /// <summary>Change the saved display/source path only AFTER native rename verification.
    /// The photo GUID, staged decisions, and original metadata remain stable.
    /// A journal row is authoritative if the process stops before session save.</summary>
    public void ApplyVerifiedRename(Guid photoId, string prior, string updated)
    {
        var index=_photos.FindIndex(p=>p.Id==photoId);
        if (index<0) throw new InvalidDataException("Renamed photo is not in the active session.");
        if (!PathComparer.Equals(_photos[index].Path,prior))
        {
            if (PathComparer.Equals(_photos[index].Path,updated)) return; // replay after checkpoint save
            throw new InvalidDataException("Renamed photo source diverged from saved workspace.");
        }
        if (!_sourceRoots.Any(root=>IsDescendant(updated,root)) ||
            _photos.Any(p=>p.Id!=photoId && PathComparer.Equals(p.Path,updated)) ||
            _removedPhotos.Any(p=>PathComparer.Equals(p.Photo.Path,updated)))
            throw new InvalidDataException("Rename target is outside sources or collides with saved photographs.");
        _photos[index]=_photos[index] with {Path=updated};
        Invalidate();
    }

    public void RestoreUndone(Guid photoId, char key)
    {
        if (!_photos.Any(p=>p.Id==photoId)) return;
        if (!_routes.ContainsKey(key)) return;
        if (!_assigned.ContainsKey(photoId)) { _assigned[photoId] = key; Invalidate(); }
    }


    private static void VerifySource(Photo photo)
    {
        if (!File.Exists(photo.Path) || HasSymlinkOrReparsePoint(photo.Path))
            throw new IOException("Source missing or replaced with a symbolic link: " + photo.Path);
        var info = new FileInfo(photo.Path);
        if (info.Length != photo.Length || info.LastWriteTimeUtc.Ticks != photo.LastWriteUtcTicks)
            throw new IOException("Source changed since import. Reopen folder to review again: " + photo.Path);
    }

    public async Task<CopyResult> ApplyReviewedCopiesAsync(CancellationToken cancel = default)
    {
        if (_reviewed is null || !IsCurrentPlanApproved)
            throw new IOException("Approve the current reviewed destination plan before applying.");
        if (_applied.Count > 0)
            throw new IOException("Undo the previous applied batch before applying a new one in P1.");
        var plan = _reviewed.ToArray();
        // All preflight checks precede writes; later checks still happen per file.
        foreach (var op in plan)
        {
            cancel.ThrowIfCancellationRequested();
            VerifySource(new Photo(op.Source, op.ExpectedLength, op.ExpectedWriteUtcTicks));
            if (File.Exists(op.Destination) || Directory.Exists(op.Destination))
                throw new IOException("Destination changed after review: " + op.Destination);
        }
        var success = 0;
        string? error = null;
        foreach (var op in plan)
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                VerifySource(new Photo(op.Source, op.ExpectedLength, op.ExpectedWriteUtcTicks));
                if (DestinationRoot is null || !Directory.Exists(DestinationRoot) || HasSymlinkOrReparsePoint(DestinationRoot))
                    throw new IOException("Destination root is no longer a real directory.");
                string folder = Path.GetDirectoryName(op.Destination)!;
                Directory.CreateDirectory(folder);
                if (HasSymlinkOrReparsePoint(folder) || File.Exists(op.Destination) || Directory.Exists(op.Destination))
                    throw new IOException("Unsafe destination or collision: " + op.Destination);
                string temp = Path.Combine(folder, ".photosift-reborn-" + Guid.NewGuid().ToString("N") + ".part");
                try
                {
                    // Note: conservative .NET file-creation checks do not guarantee atomic
                    // safety against an adversarial noncooperating external filesystem writer.
                    string sourceHash = await ComputeSha256Async(op.Source, cancel);
                    var original = new FileInfo(op.Source);
                    if (original.Length != op.ExpectedLength || original.LastWriteTimeUtc.Ticks != op.ExpectedWriteUtcTicks)
                        throw new IOException("Source changed while preparing copy.");
                    await using (var input = new FileStream(op.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await input.CopyToAsync(output, cancel);
                        await output.FlushAsync(cancel);
                        output.Flush(flushToDisk: true);
                    }
                    if (await ComputeSha256Async(temp, cancel) != sourceHash ||
                        await ComputeSha256Async(op.Source, cancel) != sourceHash)
                        throw new IOException("Source or temporary copy changed during verification.");
                    if (File.Exists(op.Destination) || Directory.Exists(op.Destination))
                        throw new IOException("Destination appeared while copying; no overwrite permitted.");
                    File.Move(temp, op.Destination, overwrite: false);
                    var newInfo = new FileInfo(op.Destination);
                    _applied.Add(new AppliedCopy(op.Source, op.Destination, sourceHash, newInfo.Length, newInfo.LastWriteTimeUtc.Ticks));
                    success++;
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                error = $"Stopped after {success} successful copies. {ex.GetType().Name}: {ex.Message}";
                break;
            }
        }
        // Do not forget successful copies or falsely assert a whole-batch transaction.
        _reviewed = null; _reviewedRevision = -1; _reviewedId = null; _approvedId = null;
        return new CopyResult(success, error);
    }

    public async Task<CopyResult> UndoAppliedCopiesAsync(CancellationToken cancel = default)
    {
        if (_applied.Count == 0) throw new IOException("No applied copy to undo.");
        int removed = 0;
        string? error = null;
        for (int i = _applied.Count - 1; i >= 0; i--)
        {
            var op = _applied[i];
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (_uncertainAppliedCopies.Contains(op))
                    throw new IOException("Previously observed output ownership conflict remains unresolved; rewriting bytes or timestamps does not restore Undo authority: " + op.Destination);
                if (!File.Exists(op.Destination) || HasSymlinkOrReparsePoint(op.Destination))
                    throw new IOException("Output missing or replaced with link: " + op.Destination);
                var info = new FileInfo(op.Destination);
                if (info.Length != op.Length || info.LastWriteTimeUtc.Ticks != op.InstalledWriteUtcTicks ||
                    await ComputeSha256Async(op.Destination, cancel) != op.Sha256)
                    throw new IOException("Output changed since application. Refusing to delete: " + op.Destination);
                // This is bounded accidental-conflict detection, NOT proof of file identity
                // against an active writer that replaces a file with identical bytes/metadata.
                File.Delete(op.Destination);
                _applied.RemoveAt(i);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // A failed ownership check or failed deletion makes this exact
                // applied output permanently uncertain for this workspace.
                // Cancellation before a mutation alone is not an ownership conflict.
                if (ex is IOException or UnauthorizedAccessException)
                    _uncertainAppliedCopies.Add(op);
                error = $"Undo stopped after {removed}. Unresolved outputs preserved. {ex.Message}";
                break;
            }
        }
        return new CopyResult(removed, error);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        // Reconcile() can hash an existing journalled output during startup, before
        // the Avalonia window is shown. Avoid capturing the UI SynchronizationContext:
        // the caller currently waits synchronously for this hash during reconciliation.
        // A synchronous using also avoids an awaited disposal recapturing that context.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }
}
