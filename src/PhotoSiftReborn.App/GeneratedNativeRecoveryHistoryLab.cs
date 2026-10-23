using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// Explicitly opted-in owner GUI acceptance only: makes an entirely NEW root
/// underneath the machine temporary directory and copies the packaged sample
/// PNGs. Native Copy/Move/Undo operations are executed by the EXISTING
/// generated-only coordinators and their normal consent/ownership checks.
/// There is no arbitrary-path argument, import of a personal-photo directory,
/// recovery grant, fixture replay or ability to target an existing lab.
/// </summary>
internal static class GeneratedNativeRecoveryHistoryLab
{
    internal const string OptInVariable = "PHOTOSIFT_REBORN_GENERATED_NATIVE_HISTORY_OPT_IN";
    internal const string ExactOptIn = "GENERATED_FIXTURES_ONLY_I_ACCEPT_ONE_NEW_LAB";

    internal static string Create()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != ExactOptIn)
            throw new InvalidOperationException("Explicit generated-only recovery-lab opt-in missing.");
        if (!NativeFileCapability.IsSupported)
            throw new PlatformNotSupportedException("Generated history requires verified native Windows or Linux filesystem capabilities.");
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (!Directory.Exists(fixturePath))
            throw new FileNotFoundException("Packaged synthetic fixtures missing.");
        var names = Directory.GetFiles(fixturePath, "*.png");
        if (names.Length < 2 || names.Any(x => Path.GetFileName(x).Contains(' ')))
            throw new InvalidDataException("Expected at least two bundled generated PNG fixtures.");
        var root = Path.Combine(Path.GetTempPath(), "PHOTOSIFT_REBORN_P1_DISPOSABLE_" +
            Guid.NewGuid().ToString("N"));
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("A supposed new generated fixture root already exists.");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "source"));
        Directory.CreateDirectory(Path.Combine(root, "output"));
        File.WriteAllText(Path.Combine(root, "P3B2_TEST_ONLY.marker"), "synthetic-interruption-only");
        foreach (string name in names)
            File.Copy(name, Path.Combine(root, "source", Path.GetFileName(name)), false);

        var work = new SortingWorkspace { IdentityResolver = NativeFileCapability.Identity };
        work.OpenSource(Path.Combine(root, "source"));
        work.SetDestinationRoot(Path.Combine(root, "output"));
        if (work.Photos.Count < 2 || work.Photos.Any(p =>
                string.IsNullOrWhiteSpace(p.NativeIdentity) ||
                p.NativeIdentity != NativeFileCapability.Identity(p.Path)))
            throw new InvalidDataException("Generated photo identity preflight failed.");
        Guid firstPhoto = work.Photos[0].Id;
        var state = Path.Combine(root, "state");
        Guid generatedMoveSession = Guid.Empty;
        using (var db = new WorkspaceSessionStore(state))
        {
            work.Assign('F');
            var firstPlan = work.ReviewPlan();
            var plan = DurableCopyCoordinator.PrepareAsync(work, firstPlan,
                work.CurrentPlanId!.Value, work.Revision).GetAwaiter().GetResult();
            work.ApprovePlan(work.CurrentPlanId!.Value, work.Revision);
            db.Save(work.CaptureSession());
            var copy = new DurableCopyCoordinator(db);
            if (copy.ApplyAsync(plan).GetAwaiter().GetResult().Completed != 1)
                throw new InvalidDataException("Generated native Copy did not commit.");
            var prior = db.CopyItems(work.SessionId).Single();
            if (prior.State != "Committed" || prior.PhotoId != firstPhoto)
                throw new InvalidDataException("Generated predecessor Copy was not committed.");
            work.RetireCommitted(new[] { firstPhoto });
            db.Save(work.CaptureSession());
            var resumed = new SortingWorkspace { IdentityResolver = NativeFileCapability.Identity };
            resumed.RestoreSession(db.Load()!);
            resumed.JumpToPhoto(2);
            if (resumed.Current is null || resumed.Current.Id == firstPhoto)
                throw new InvalidDataException("Disjoint second generated photo missing.");
            resumed.Assign('T');
            generatedMoveSession = resumed.SessionId;
            var secondPlan = resumed.ReviewPlan();
            var plannedMove = DurableCopyCoordinator.PrepareAsync(resumed,
                secondPlan, resumed.CurrentPlanId!.Value, resumed.Revision)
                .GetAwaiter().GetResult().Single();
            resumed.ApprovePlan(resumed.CurrentPlanId!.Value, resumed.Revision);
            db.Save(resumed.CaptureSession());
            Directory.CreateDirectory(Path.GetDirectoryName(plannedMove.Destination)!);
            var operationId = Guid.NewGuid();
            var candidate = new M6JProspectiveCandidate(operationId, "Move", resumed.SessionId,
                plannedMove.PhotoId, plannedMove.Source, plannedMove.Destination,
                plannedMove.Identity, plannedMove.Hash, plannedMove.Length, plannedMove.WriteTicks);
            db.StageM6JProspectiveReview(candidate);
            db.ReviewM6JProspectiveCandidate(operationId, candidate, true);
            string parent = Path.GetDirectoryName(plannedMove.Destination)!;
            string archiveRoot = Path.Combine(Path.GetDirectoryName(plannedMove.Source)!,
                ".photosift-reborn-recovery", db.MoveInstallationId().ToString("N"),
                resumed.SessionId.ToString("N"), operationId.ToString("N"));
            var intent = new DurableMoveItem(operationId, db.MoveInstallationId(),
                plannedMove.SessionId, plannedMove.PhotoId, plannedMove.PlanId,
                plannedMove.Revision, DateTime.UtcNow.Ticks,
                plannedMove.Source, plannedMove.Destination,
                Path.Combine(parent, ".photosift-move-" + operationId.ToString("N") + ".part"),
                Path.Combine(archiveRoot, Path.GetFileName(plannedMove.Source)),
                Path.Combine(archiveRoot, "installed-" + Path.GetFileName(plannedMove.Destination)),
                plannedMove.OutputRoot, plannedMove.Hash, plannedMove.Identity,
                plannedMove.Length, plannedMove.WriteTicks, null, null, "Intent", null,
                plannedMove.Key);
            var prepared = new PreparedCopy(intent.PlanId, intent.PlanRevision,
                intent.SessionId, intent.PhotoId, intent.RouteKey, intent.Source,
                intent.Destination, intent.SourceLength, intent.SourceWriteTicks,
                intent.SourceHash, intent.SourceIdentity, intent.OutputRoot);
            var mover = new DurableMoveCoordinator(db);
            var moved = mover.ExecuteM6JReviewedGeneratedCopyMoveAsync(resumed, prepared, intent)
                .GetAwaiter().GetResult();
            if (moved.State != "Committed")
                throw new InvalidDataException("Generated native Move did not commit.");
        }
        // Independently cold-restart native selective Undo, then inspect the
        // actual committed Copy and verified terminal Move journals together.
        using (var reopened = new WorkspaceSessionStore(state))
        {
            var mover = new DurableMoveCoordinator(reopened);
            var undone = mover.UndoM6JReviewedGeneratedCopyMoveAsync(generatedMoveSession)
                .GetAwaiter().GetResult();
            if (undone.State != "Undone" ||
                !DurableMoveCoordinator.ProveTerminalUndoneEvidence(undone))
                throw new InvalidDataException("Generated native selective Undo did not verify.");
        }
        using (var cold = new WorkspaceSessionStore(state))
        {
            var observation = cold.InspectM6JP3PairedRecoveryObservation();
            if (!observation.ConsistentDatabaseObservation || observation.Inventory.Rows.Count != 2 ||
                observation.Inventory.Rows.Count(x=>x.Family=="Copy" && x.State=="Committed" &&
                    x.Proof==UnifiedRowProof.NativeActiveVerified)!=1 ||
                observation.Inventory.Rows.Count(x=>x.Family=="Move" && x.State=="Undone" &&
                    x.Proof==UnifiedRowProof.NativeTerminalVerified)!=1)
                throw new InvalidDataException("Generated two-family native history failed exact cold-restart proof.");
        }
        var witness = new {
            schema = "photosift-generated-native-history-p3-v1",
            purpose = "Read-only GUI inspection of TRUE generated native Copy and Move journals",
            platform = OperatingSystem.IsWindows() ? "windows" : "linux",
            root, state,
            source = Path.Combine(root,"source"),
            notice = "DISPOSABLE GENERATED PNGS ONLY. No real photos. Never replay a native test or point this fixture at an existing workspace."
        };
        File.WriteAllText(Path.Combine(root, "P3_GENERATED_NATIVE_HISTORY_RECEIPT.json"),
            JsonSerializer.Serialize(witness,new JsonSerializerOptions{WriteIndented=true}));
        return state;
    }
}
