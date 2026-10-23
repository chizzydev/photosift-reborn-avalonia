namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P2C-P1: one generated-fixture-only, evidence-bound SQLite journal
/// terminalization AFTER the filesystem has already finished selective Undo.
/// This is not a new Undo operation, Move replay, filesystem repair, or grant.
/// </summary>
public sealed record M6JCompletedUndoFinalizationReceipt(Guid MoveId,
    string FinalRecordedState, bool JournalUpdated)
{
    public bool AuthorizesFilesystemMutation => false;
    public bool AuthorizesAutomaticReplay => false;
    public bool AuthorizesNewMoveIntent => false;
    public bool AuthorizesOrdinaryPhotos => false;
}

public sealed partial class WorkspaceSessionStore
{
    /// <summary>
    /// The sole allowable recovery case is exactly one interrupted
    /// UndoOutputArchiveIntent after both native relocations already finished,
    /// with original source restored and installed output privately archived.
    /// All other cuts are still MANUAL_RECONCILIATION_REQUIRED.
    ///
    /// No path is created, deleted, moved, linked or overwritten. Only one
    /// journal row changes to Undone after independently proving the native
    /// filesystem already satisfies the preexisting terminal-Undo contract.
    /// No public GUI/ordinary-file entry point is supplied.
    /// </summary>
    internal M6JCompletedUndoFinalizationReceipt FinalizeM6JGeneratedOnlyCompletedUndoJournal(
        Guid moveSession)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            string stateFolder=Path.GetDirectoryName(DatabasePath)
                ?? throw new IOException("P2C-P1 state folder unavailable.");
            string root=Directory.GetParent(stateFolder)?.FullName
                ?? throw new IOException("P2C-P1 isolated fixture root unavailable.");
            if (Path.GetFileName(stateFolder)!="state" ||
                !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",
                    StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(Path.Combine(root,"P3B2_TEST_ONLY.marker")) ||
                SortingWorkspace.HasLinkedExistingAncestor(root) ||
                File.ReadAllText(Path.Combine(root,"P3B2_TEST_ONLY.marker"))!=
                    "synthetic-interruption-only")
                throw new IOException("P2C-P1 finalization is forbidden outside disposable tests.");

            M6JInterruptedCopyMoveObservation evidence=
                InspectM6JInterruptedCopyMoveReadOnly(moveSession);
            if (evidence.RecordedMoveState!="UndoOutputArchiveIntent" ||
                !evidence.ConsumedReviewSealAndCandidateMatch ||
                !evidence.OriginalCopyHistoryDigestMatches ||
                !evidence.CommittedCopyNativeOwnershipMatches ||
                !evidence.NativeClaimsAreDisjoint ||
                evidence.MoveSource!="OWNED" || evidence.MoveTarget!="ABSENT" ||
                evidence.MoveTemp!="ABSENT" ||
                evidence.MoveOriginalArchive!="ABSENT" ||
                evidence.MoveOutputArchive!="OWNED")
                throw new IOException("P2C-P1 native/interrupted history is not exactly terminal Undo.");

            var rows=MoveItems(moveSession);
            if (rows.Count!=1 || rows[0].Id!=evidence.MoveId ||
                rows[0].State!="UndoOutputArchiveIntent" ||
                rows[0].Error is not null || rows[0].TargetIdentity is null ||
                rows[0].TempIdentity is null || rows[0].SourceWriteTicks<=0)
                throw new IOException("P2C-P1 frozen Move journal cannot be finalized.");
            var pending=rows[0];
            var undone=pending with {State="Undone"};
            if (!NativeFileCapability.IsSingleLinkRegularFile(pending.Source) ||
                !NativeFileCapability.IsSingleLinkRegularFile(pending.OutputArchive) ||
                !DurableMoveCoordinator.ProveTerminalUndoneEvidence(undone))
                throw new IOException("P2C-P1 final state fails native terminal Undo ownership proof.");

            // Re-read evidence and recompute the existing terminal native proof
            // immediately before the single journal write. These checks do NOT
            // claim safety against a malicious unsynchronized external writer.
            if (InspectM6JInterruptedCopyMoveReadOnly(moveSession)!=evidence ||
                !DurableMoveCoordinator.ProveTerminalUndoneEvidence(undone))
                throw new IOException("P2C-P1 evidence changed before terminal journal update.");

            // PutMoveItem's existing durable state machine accepts only the
            // immediate UndoOutputArchiveIntent -> Undone transition here.
            // The holding _dbGate ensures concurrent in-process journal writers
            // cannot bypass this preflight. No on-disk file is changed.
            PutMoveItem(undone);
            if (MoveItems(moveSession).Single()!=undone)
                throw new IOException("P2C-P1 final journal row did not persist exactly.");
            return new M6JCompletedUndoFinalizationReceipt(pending.Id,"Undone",true);
        }
    }
}
