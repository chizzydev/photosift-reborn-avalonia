using System.Security.Cryptography;
using System.Text;

namespace PhotoSiftReborn;

/// <summary>
/// P6B-0: read-only audit for a candidate same-directory Rename on an ordinary source.
/// This is NOT an approval, executable plan, or permission to bypass the P5D fixture gate.
/// All answers can go stale immediately; production execution needs additional handle/
/// directory ownership and multi-operation journal proof before it may be enabled.
/// </summary>
public sealed record RenameReadinessAssessment(
    string Source, string ProposedTarget, IReadOnlyList<string> Blockers,
    string? ObservedIdentity, string? ObservedSha256)
{
    public bool PreliminaryChecksPass => Blockers.Count == 0;
    public bool AuthorizedForMutation => false;
}

public static class ProductionRenameReadiness
{
    private static bool PathInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return p.Equals(f, SortingWorkspace.PathComparison) ||
               p.StartsWith(f + Path.DirectorySeparatorChar, SortingWorkspace.PathComparison);
    }

    public static RenameReadinessAssessment Inspect(
        WorkspaceSessionStore store, SortingWorkspace work, Photo photo, string proposedFileName)
    {
        var reasons = new List<string>();
        string source = photo.Path;
        string target = "(invalid filename)";
        string? identity = null, digest = null;
        void Block(string reason) { if (!reasons.Contains(reason, StringComparer.Ordinal)) reasons.Add(reason); }

        // Validation must not create, rename, delete, or journal any source object.
        if (!NativeFileCapability.IsSupported) Block("No verified native identity/rename adapter for this operating system.");
        if (work.Current?.Id != photo.Id || photo.Id == Guid.Empty ||
            !work.Photos.Any(p => p.Id == photo.Id && SortingWorkspace.PathComparer.Equals(p.Path, photo.Path)))
            Block("Selected photo is not the current stable workspace photograph.");
        if (work.AssignmentCount != 0 || work.RemovedCount != 0 || work.HasAppliedCopies)
            Block("Current workspace has assignments, removals, or applied copies.");
        if (store.HasUnreconciledCopy || store.AllMoveHistoryCount() != 0 ||
            store.AllRenameHistoryCount() != 0 || store.CopyItems(work.SessionId).Count != 0)
            Block("Existing file-operation history or unresolved copy risk requires a separate multi-operation design.");
        if (photo.NativeIdentity is null) Block("The photograph has no native file identity recorded at import.");

        try
        {
            if (!Path.IsPathFullyQualified(source)) Block("Source path is not fully qualified.");
            var folder = Path.GetDirectoryName(Path.GetFullPath(source));
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                Block("The source directory does not exist.");
            }
            else
            {
                if (SortingWorkspace.HasLinkedExistingAncestor(folder) ||
                    SortingWorkspace.HasSymlinkOrReparsePoint(source))
                    Block("A source path component is a symbolic link/reparse point.");
                // The journal must not be inside the file's own source directory, nor may
                // the photograph reside under the application's active state directory.
                var state = Path.GetDirectoryName(store.DatabasePath)!;
                if (PathInside(source, state) || PathInside(state, folder))
                    Block("The workspace database must be separate from the photo directory.");
                try
                {
                    var name = DurableRenameCoordinator.ValidateFilename(source, proposedFileName);
                    target = Path.Combine(folder, name);
                    // Uniformly reject case/Unicode aliases on Linux and Windows.
                    var normalized = name.Normalize(NormalizationForm.FormC);
                    if (Directory.EnumerateFileSystemEntries(folder).Any(entry =>
                        string.Equals(Path.GetFileName(entry).Normalize(NormalizationForm.FormC),
                            normalized, StringComparison.OrdinalIgnoreCase)))
                        Block("Requested filename aliases an existing file or directory (portable case/Unicode rule).");
                    if (File.Exists(target) || Directory.Exists(target))
                        Block("Requested target path is occupied.");
                }
                catch (IOException ex) { Block("Invalid candidate filename: " + ex.Message); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Block("Cannot safely inspect source path: " + ex.GetType().Name); }

        try
        {
            if (!File.Exists(source)) Block("Source original is absent.");
            else if (SortingWorkspace.HasSymlinkOrReparsePoint(source) ||
                     SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(source)!))
                Block("Source contains a linked/reparse path.");
            else if (NativeFileCapability.IsSupported)
            {
                var info = new FileInfo(source);
                identity = NativeFileCapability.Identity(source);
                if (identity != photo.NativeIdentity || info.Length != photo.Length ||
                    info.LastWriteTimeUtc.Ticks != photo.LastWriteUtcTicks)
                    Block("Source native identity, size, or modification time changed since import.");
                if (target != "(invalid filename)" &&
                    NativeFileCapability.DeviceId(source) != NativeFileCapability.DeviceId(Path.GetDirectoryName(target)!))
                    Block("Source and destination are not proved to be on the same filesystem.");
                // Digest is evidence for the read-only snapshot, NOT a durable approval.
                using var input = File.OpenRead(source);
                digest = Convert.ToHexString(SHA256.HashData(input));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Block("Cannot prove readable ordinary source and native identity: " + ex.GetType().Name); }

        return new RenameReadinessAssessment(source, target, reasons.ToArray(), identity, digest);
    }
}
