namespace PhotoSiftReborn;

/// <summary>
/// P5A READ-ONLY Move safety inspection. Deliberately has no Apply method.
/// The later Move executor must independently revalidate everything under a
/// durable per-item journal BEFORE creating directories or relocating sources.
/// A passing inspection is NOT authorization to mutate files.
/// </summary>
public sealed record MoveReadinessReport(
    Guid SessionId, Guid PlanId, Guid PhotoId,
    string Source, string SortedDestination, string ProposedSourceArchive,
    string SourceHash, string SourceNativeIdentity,
    long SourceBytes, string Report);

public static class MoveReadinessProbe
{
    public static async Task<MoveReadinessReport> InspectAsync(
        SortingWorkspace workspace, WorkspaceSessionStore store,
        CancellationToken cancellation = default)
    {
        if (!NativeFileCapability.IsSupported)
            throw new PlatformNotSupportedException("Move requires a verified native filesystem adapter. This OS is not yet supported.");
        // P6C2-A: refuse historical rows in ANY session before even producing
        // a read-only readiness preview; this is not execution approval.
        store.RequireFreshMoveEpoch();
        if (store.HasUnreconciledCopy || store.CopyItems(workspace.SessionId).Count != 0)
            throw new IOException("Move preflight is restricted to a fresh session with no earlier file-operation history. Files unchanged.");
        if (workspace.DestinationRoot is null)
            throw new IOException("Choose an output directory before assessing Move.");

        // This is an ordinary, read-only Review. It captures one exact frozen
        // source/target and calculates source digest + native identity.
        var plans = workspace.ReviewPlan();
        if (plans.Count != 1 || workspace.SkippedReview.Count != 0)
            throw new IOException("Move preflight currently requires exactly one staged photograph and no skipped collisions. Files unchanged.");
        var id = workspace.CurrentPlanId ?? throw new IOException("Missing review identity.");
        var prepared = await DurableCopyCoordinator.PrepareAsync(
            workspace, plans, id, workspace.Revision, cancellation);
        if (prepared.Count != 1) throw new IOException("Move preflight expected one verified photograph.");
        var p = prepared[0];
        cancellation.ThrowIfCancellationRequested();
        var srcParent = Path.GetDirectoryName(Path.GetFullPath(p.Source))
            ?? throw new IOException("Cannot derive a trusted source parent.");
        if (!Directory.Exists(srcParent) || SortingWorkspace.HasLinkedExistingAncestor(srcParent) ||
            SortingWorkspace.HasSymlinkOrReparsePoint(p.Source))
            throw new IOException("The source or its parent is missing or includes a link/reparse point.");
        var recoveryRoot = Path.Combine(srcParent, ".photosift-reborn-recovery");
        if (SortingWorkspace.HasLinkedExistingAncestor(recoveryRoot))
            throw new IOException("Source recovery path contains a link/reparse point.");
        if (File.Exists(recoveryRoot))
            throw new IOException("Source recovery root is an occupied file.");
        // Never silently claim an existing unmarked recovery directory. Move's
        // later durable executor must also write and verify an owner marker.
        if (Directory.Exists(recoveryRoot))
            throw new IOException("An existing source recovery root needs ownership validation before Move can be enabled.");
        if (Directory.Exists(p.Destination) || File.Exists(p.Destination))
            throw new IOException("Exact proposed output is now occupied; start a fresh review.");
        if (SortingWorkspace.PathComparer.Equals(p.Source, p.Destination) ||
            SortingWorkspace.PathComparer.Equals(p.Source, recoveryRoot))
            throw new IOException("Source/output/recovery alias is unsafe.");
        var candidate = Path.Combine(recoveryRoot, workspace.SessionId.ToString("N"),
            p.PhotoId.ToString("N"), Path.GetFileName(p.Source));
        if (Path.GetFullPath(candidate).StartsWith(Path.GetFullPath(p.Destination) + Path.DirectorySeparatorChar,
                SortingWorkspace.PathComparison))
            throw new IOException("Recovery location overlaps the output destination.");
        string report = "MOVE READINESS — NO CHANGES AND NO EXECUTION AUTHORIZED" + Environment.NewLine +
            "ONE verified staged photograph; local native file identity and SHA-256 recorded." + Environment.NewLine +
            "FROM  " + p.Source + Environment.NewLine +
            "SORT TO  " + p.Destination + Environment.NewLine +
            "RETAIN ORIGINAL AT  " + candidate + Environment.NewLine +
            "BYTES  " + p.Length + " (plus backup/recovery storage overhead)" + Environment.NewLine +
            "HASH  " + p.Hash + Environment.NewLine +
            "SOURCE IDENTITY  " + p.Identity + Environment.NewLine +
            "NOT YET CERTIFIED: source-volume recovery directory creation/ownership; live no-replace relocation;" + Environment.NewLine +
            "durable Move/Undo journal and interruption recovery; current filesystem free space." + Environment.NewLine +
            "P5A cannot move, rename, reject or delete any files. Copy remains the only enabled operation.";
        return new MoveReadinessReport(p.SessionId, p.PlanId, p.PhotoId, p.Source,
            p.Destination, candidate, p.Hash, p.Identity, p.Length, report);
    }
}
