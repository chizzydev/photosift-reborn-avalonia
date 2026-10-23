namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P2C-P2: a SECOND very narrow crash-boundary terminal journal repair.
/// Only a generated Copy A -> Move B fixture whose source is ALREADY archived,
/// output is ALREADY published and temporary hard link is ALREADY absent may
/// advance SourceArchived -> Committed. This method does not mutate native files.
/// </summary>
public sealed record M6JCompletedMoveFinalizationReceipt(Guid MoveId,
    string FinalRecordedState, bool JournalUpdated)
{
    public bool AuthorizesFilesystemMutation => false;
    public bool AuthorizesAutomaticReplay => false;
    public bool AuthorizesAdditionalEpoch => false;
    public bool AuthorizesOrdinaryPhotos => false;
}

public sealed partial class WorkspaceSessionStore
{
    internal M6JCompletedMoveFinalizationReceipt FinalizeM6JGeneratedOnlyCompletedMoveJournal(
        Guid moveSession)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            // Maintain the independent, exact disposable-test boundary rather
            // than exposing a generic recovery method or fresh public grant.
            string stateFolder=Path.GetDirectoryName(DatabasePath)
                ?? throw new IOException("P2C-P2 state folder unavailable.");
            string root=Directory.GetParent(stateFolder)?.FullName
                ?? throw new IOException("P2C-P2 fixture root unavailable.");
            string fixtureMarker=Path.Combine(root,"P3B2_TEST_ONLY.marker");
            if (Path.GetFileName(stateFolder)!="state" ||
                !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",
                    StringComparison.Ordinal) ||
                !File.Exists(fixtureMarker) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(fixtureMarker) ||
                SortingWorkspace.HasLinkedExistingAncestor(root) ||
                File.ReadAllText(fixtureMarker)!="synthetic-interruption-only")
                throw new IOException("P2C-P2 journal finalization is disposable-test-only.");

            var evidence=InspectM6JInterruptedCopyMoveReadOnly(moveSession);
            if (evidence.RecordedMoveState!="SourceArchived" ||
                !evidence.ConsumedReviewSealAndCandidateMatch ||
                !evidence.OriginalCopyHistoryDigestMatches ||
                !evidence.CommittedCopyNativeOwnershipMatches ||
                !evidence.NativeClaimsAreDisjoint ||
                evidence.MoveSource!="ABSENT" || evidence.MoveTarget!="OWNED" ||
                evidence.MoveTemp!="ABSENT" ||
                evidence.MoveOriginalArchive!="OWNED" ||
                evidence.MoveOutputArchive!="ABSENT")
                throw new IOException("P2C-P2 journal/native execution is not terminal Move.");

            var entries=MoveItems(moveSession);
            if (entries.Count!=1 || entries[0].Id!=evidence.MoveId ||
                entries[0].State!="SourceArchived" || entries[0].Error is not null ||
                entries[0].TargetIdentity is null || entries[0].TempIdentity is null ||
                entries[0].TargetIdentity!=entries[0].TempIdentity ||
                entries[0].SourceIdentity is null ||
                entries[0].SourceWriteTicks<=0)
                throw new IOException("P2C-P2 Move row cannot be journal-terminalized.");
            var move=entries[0];
            string recoveryRoot=Path.Combine(Path.GetDirectoryName(move.Source)!,
                ".photosift-reborn-recovery");
            string operationRoot=Path.GetDirectoryName(move.OriginalArchive)!;
            string recoveryMarker=Path.Combine(recoveryRoot,".photosift-move-owner-v1");
            string operationMarker=Path.Combine(operationRoot,".photosift-operation-v1");
            if (!Directory.Exists(recoveryRoot) || !Directory.Exists(operationRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(recoveryRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(operationRoot) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(move.Destination)!) ||
                !File.Exists(recoveryMarker) || !File.Exists(operationMarker) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(recoveryMarker) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(operationMarker) ||
                File.ReadAllText(recoveryMarker)!=
                    "PhotoSift Reborn Move v1:"+move.InstallationId.ToString("N")+"\n" ||
                File.ReadAllText(operationMarker)!=
                    "PhotoSift Reborn Move operation "+move.Id.ToString("N")+"\n" ||
                !NativeFileCapability.IsSingleLinkRegularFile(move.OriginalArchive) ||
                !NativeFileCapability.IsSingleLinkRegularFile(move.Destination) ||
                new FileInfo(move.OriginalArchive).LastWriteTimeUtc.Ticks!=move.SourceWriteTicks ||
                !CheckCopyOwnedFile(move.OriginalArchive,move.SourceIdentity,move.SourceHash,move.SourceLength) ||
                !CheckCopyOwnedFile(move.Destination,move.TargetIdentity,move.SourceHash,move.SourceLength) ||
                NativeFileCapability.DeviceId(move.OriginalArchive)!=
                    NativeFileCapability.DeviceId(move.Destination))
                throw new IOException("P2C-P2 terminal Move markers, ownership or native time failed.");

            // Reobserve immediately before the journal-only state transition.
            // In-process DB lock is not a hostile external-writer exclusion.
            if (InspectM6JInterruptedCopyMoveReadOnly(moveSession)!=evidence ||
                !NativeFileCapability.IsSingleLinkRegularFile(move.Destination) ||
                !NativeFileCapability.IsSingleLinkRegularFile(move.OriginalArchive) ||
                !CheckCopyOwnedFile(move.Destination,move.TargetIdentity,move.SourceHash,move.SourceLength) ||
                !CheckCopyOwnedFile(move.OriginalArchive,move.SourceIdentity,move.SourceHash,move.SourceLength) ||
                new FileInfo(move.OriginalArchive).LastWriteTimeUtc.Ticks!=move.SourceWriteTicks ||
                File.ReadAllText(recoveryMarker)!=
                    "PhotoSift Reborn Move v1:"+move.InstallationId.ToString("N")+"\n" ||
                File.ReadAllText(operationMarker)!=
                    "PhotoSift Reborn Move operation "+move.Id.ToString("N")+"\n")
                throw new IOException("P2C-P2 current native proof changed before SQLite finalization.");
            var completed=move with {State="Committed"};
            PutMoveItem(completed); // exactly SourceArchived -> Committed, no FS mutation
            if (MoveItems(moveSession).Single()!=completed)
                throw new IOException("P2C-P2 final journal row did not persist exactly.");
            return new M6JCompletedMoveFinalizationReceipt(move.Id,"Committed",true);
        }
    }
}
