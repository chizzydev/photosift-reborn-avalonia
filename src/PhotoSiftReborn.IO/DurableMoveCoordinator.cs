using System.Text;

namespace PhotoSiftReborn;

public sealed record MoveObservation(Guid Id, string State, string Source, string Destination,
    string OriginalArchive, string OutputArchive, string Evidence, string? Error);

/// <summary>
/// Durable no-replace Move and reversible owned source archive. The legacy public
/// constructor remains disposable-fixture-only; the separate explicitly reviewed
/// ordinary FIRST-epoch factory independently enforces native-policy constraints.
/// Never silently complete ambiguous interrupted states.
/// This is NOT arbitrary power-loss / hostile-concurrent-writer certification.
/// </summary>
public sealed class DurableMoveCoordinator
{
    private readonly WorkspaceSessionStore _db;
    private readonly Action<string>? _testCut;
    private readonly string? _fixtureRoot;
    private readonly bool _ordinaryFirstEpoch;

    // Production mutation of personal photographs is intentionally disabled.
    // Require exactly the generated SelfTests fixture marker and isolated state.
    public DurableMoveCoordinator(WorkspaceSessionStore db, Action<string>? testCut=null)
    {
        var state = Path.GetDirectoryName(db.DatabasePath)
            ?? throw new InvalidOperationException("Missing Move fixture state folder.");
        var root = Directory.GetParent(state)?.FullName;
        if (root is null || Path.GetFileName(state)!="state" ||
            !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")) ||
            File.ReadAllText(Path.Combine(root,"P3B2_TEST_ONLY.marker"))!="synthetic-interruption-only" ||
            SortingWorkspace.HasLinkedExistingAncestor(root))
            throw new InvalidOperationException("P5B Move execution is locked to generated disposable self-test fixtures.");
        _db=db;
        _testCut=testCut;
        _fixtureRoot=root;
    }

    private DurableMoveCoordinator(WorkspaceSessionStore db,bool ordinary)
    {
        if(!ordinary || !NativeFileCapability.IsSupported ||
            P5CGuiFixturePolicy.IsMarkedStore(db))
            throw new IOException("An ordinary Move executor cannot be created for a fixture or unsupported platform.");
        _db=db;_ordinaryFirstEpoch=true;
    }

    /// <summary>Fresh ordinary single-Move execution only. Never a second epoch.</summary>
    public static DurableMoveCoordinator ForOrdinaryFirstEpoch(WorkspaceSessionStore db)
        => new(db,true);

    private void CheckOrigin(SortingWorkspace work,PreparedCopy p)
    {
        if(_ordinaryFirstEpoch) OrdinaryFirstMovePolicy.RequireExact(_db,work,p);
        else CheckFixture(p);
    }

    private void Cut(string name) => _testCut?.Invoke(name);

    private static void GuardFile(string path)
    {
        // HasLinkedExistingAncestor is deliberately a DIRECTORY-path validator:
        // applying it to an existing file reports the file itself as a bogus
        // "destination ancestor". Inspect the parent chain separately from
        // the final regular file (whose reparse status is checked below).
        if (!File.Exists(path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(Path.GetFullPath(path))!) ||
            SortingWorkspace.HasSymlinkOrReparsePoint(path))
            throw new IOException("Move requires an existing non-linked regular file: " + path);
    }
    private static bool Empty(string path) => !File.Exists(path) && !Directory.Exists(path);

    private static bool Matches(string path, string? id, string hash, long length)
    {
        if (id is null) return false;
        try
        {
            GuardFile(path);
            return new FileInfo(path).Length==length && NativeFileCapability.Identity(path)==id &&
                SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==hash;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void CheckSource(PreparedCopy p)
    {
        GuardFile(p.Source);
        var info = new FileInfo(p.Source);
        if (info.Length!=p.Length || info.LastWriteTimeUtc.Ticks!=p.WriteTicks ||
            NativeFileCapability.Identity(p.Source)!=p.Identity ||
            SortingWorkspace.ComputeSha256Async(p.Source).GetAwaiter().GetResult()!=p.Hash)
            throw new IOException("Move original changed since exact Review. No mutation authorized.");
    }

    private void CheckFixture(PreparedCopy p)
    {
        if (_ordinaryFirstEpoch || _fixtureRoot is null)
            throw new IOException("Fixture-only Move epoch method refused outside a generated test fixture.");
        string fixtureRoot=_fixtureRoot;
        var sourceDir=Path.Combine(fixtureRoot,"source");
        var outputDir=Path.Combine(fixtureRoot,"output");
        if (!SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Source),sourceDir) ||
            !SortingWorkspace.PathComparer.Equals(p.OutputRoot,outputDir) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetFullPath(p.Destination),
                Path.Combine(outputDir,Path.GetFileName(Path.GetDirectoryName(p.Destination)!),Path.GetFileName(p.Source))) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetFullPath(Path.GetDirectoryName(p.Destination)!),
                Path.Combine(outputDir,Path.GetFileName(Path.GetDirectoryName(p.Destination)!))))
            throw new IOException("Only isolated test source and standard destination paths may be moved in P5B.");
        if (!Directory.Exists(sourceDir) || !Directory.Exists(outputDir) ||
            SortingWorkspace.HasLinkedExistingAncestor(sourceDir) || SortingWorkspace.HasLinkedExistingAncestor(outputDir))
            throw new IOException("Fixture folders are missing or linked.");
        if (NativeFileCapability.DeviceId(p.Source)!=NativeFileCapability.DeviceId(outputDir))
            throw new IOException("Cross-volume Move is disabled until tested independently.");
    }

    private static void ConfirmPaths(DurableMoveItem m)
    {
        string sourceParent=Path.GetDirectoryName(m.Source)!;
        var recovered=Path.Combine(sourceParent,".photosift-reborn-recovery",m.InstallationId.ToString("N"),
            m.SessionId.ToString("N"),m.Id.ToString("N"));
        if (!SortingWorkspace.PathComparer.Equals(m.OriginalArchive,Path.Combine(recovered,Path.GetFileName(m.Source))) ||
            !SortingWorkspace.PathComparer.Equals(m.OutputArchive,Path.Combine(recovered,"installed-"+Path.GetFileName(m.Destination))) ||
            !SortingWorkspace.PathComparer.Equals(m.Temp,Path.Combine(Path.GetDirectoryName(m.Destination)!,
                ".photosift-move-"+m.Id.ToString("N")+".part")))
            throw new InvalidDataException("Move journal recovery paths diverge from frozen operation.");
    }

    // Own a source-volume recovery root using a token anchored in the SQLite installation.
    // Never claim an existing directory lacking a matching marker.
    private void EnsureArchiveDirectories(DurableMoveItem m, bool originalMayExist=false)
    {
        ConfirmPaths(m);
        string parent=Path.GetDirectoryName(m.Source)!;
        string root=Path.Combine(parent,".photosift-reborn-recovery");
        string install=Path.Combine(root,m.InstallationId.ToString("N"));
        string marker=Path.Combine(root,".photosift-move-owner-v1");
        if (SortingWorkspace.HasLinkedExistingAncestor(parent) || File.Exists(root))
            throw new IOException("Source-volume recovery root is linked or occupied.");
        if (Directory.Exists(root))
        {
            if (SortingWorkspace.HasLinkedExistingAncestor(root) || !File.Exists(marker) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
                File.ReadAllText(marker)!= "PhotoSift Reborn Move v1:"+m.InstallationId.ToString("N")+"\n")
                throw new IOException("Recovery directory is not owned by this installation; no takeover.");
        }
        else
        {
            Directory.CreateDirectory(root);
            if (SortingWorkspace.HasLinkedExistingAncestor(root))
                throw new IOException("New recovery root includes a linked directory.");
            using var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var bytes=Encoding.UTF8.GetBytes("PhotoSift Reborn Move v1:"+m.InstallationId.ToString("N")+"\n");
            stream.Write(bytes);stream.Flush(true);
        }
        if (NativeFileCapability.DeviceId(parent)!=NativeFileCapability.DeviceId(root))
            throw new IOException("Recovery directory is not on the source volume.");
        if (SortingWorkspace.HasLinkedExistingAncestor(install))
            throw new IOException("Linked installation path in recovery root.");
        string operation=Path.GetDirectoryName(m.OriginalArchive)!;
        string opMarker=Path.Combine(operation,".photosift-operation-v1");
        string signature="PhotoSift Reborn Move operation "+m.Id.ToString("N")+"\n";
        if (Directory.Exists(operation))
        {
            if (SortingWorkspace.HasLinkedExistingAncestor(operation) ||
                !File.Exists(opMarker) || SortingWorkspace.HasSymlinkOrReparsePoint(opMarker) ||
                File.ReadAllText(opMarker)!=signature)
                throw new IOException("Existing operation recovery folder has no matching ownership marker.");
        }
        else
        {
            Directory.CreateDirectory(operation);
            if (SortingWorkspace.HasLinkedExistingAncestor(operation))
                throw new IOException("Linked operation recovery directory.");
            using var stream=new FileStream(opMarker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var bytes=Encoding.UTF8.GetBytes(signature);
            stream.Write(bytes);stream.Flush(true);
        }
        if (SortingWorkspace.HasLinkedExistingAncestor(operation) ||
            NativeFileCapability.DeviceId(parent)!=NativeFileCapability.DeviceId(operation))
            throw new IOException("Recovery operation directory is linked or on another volume.");
        if ((!originalMayExist && !Empty(m.OriginalArchive)) || !Empty(m.OutputArchive))
            throw new IOException("Recovery archive location already occupied.");
    }

    public async Task<PreparedCopy> PrepareApprovedAsync(SortingWorkspace work,
        IReadOnlyList<SortPlanItem> reviewed, CancellationToken cancel=default)
    {
        // A different SessionId must never bypass retained Copy/Move/Rename history.
        _db.RequireFreshMoveEpoch();
        if (_db.HasUnreconciledCopy || _db.CopyItems(work.SessionId).Count!=0 ||
            _db.MoveItems(work.SessionId).Count!=0)
            throw new IOException("Move requires a fresh session without Copy or Move history.");
        if (!work.IsCurrentPlanApproved || work.CurrentPlanId is null ||
            reviewed.Count!=1 || work.SkippedReview.Count!=0)
            throw new IOException("Exactly one explicitly approved Move is required.");
        var prepared=await DurableCopyCoordinator.PrepareAsync(work,reviewed,work.CurrentPlanId.Value,work.Revision,cancel);
        var p=prepared.Single();
        CheckOrigin(work,p);
        CheckSource(p);
        return p;
    }

    // Separately bound B3 synthetic successor path. Ordinary Move retains
    // RequireFreshMoveEpoch, including its all-session permanent-history veto.
    public async Task<PreparedCopy> PrepareApprovedStagedSuccessorAsync(SortingWorkspace work,
        IReadOnlyList<SortPlanItem> reviewed,DurableMoveItem ticket,CancellationToken cancel=default)
    {
        if (!work.IsCurrentPlanApproved || work.CurrentPlanId is null ||
            reviewed.Count!=1 || work.SkippedReview.Count!=0 || _db.HasUnreconciledCopy)
            throw new IOException("Successor Move requires exactly one freshly approved plan.");
        var prepared=await DurableCopyCoordinator.PrepareAsync(work,reviewed,
            work.CurrentPlanId.Value,work.Revision,cancel);
        var p=prepared.Single();
        CheckFixture(p);CheckSource(p);
        RequireExactSuccessor(p,ticket);
        if (!_db.InspectStagedMoveEpoch().Verified)
            throw new IOException("Staged successor history or file identity is stale.");
        return p;
    }

    // B4B1A: bounded later-epoch native execution uses the same exact-path,
    // no-replace Move core as B3, but distinct immutable authorization rows.
    // No normal/personal-photo Move is enabled by this backend entry point.
    public async Task<PreparedCopy> PrepareApprovedAdditionalEpochAsync(SortingWorkspace work,
        IReadOnlyList<SortPlanItem> reviewed,DurableMoveItem candidate,CancellationToken cancel=default)
    {
        if(!work.IsCurrentPlanApproved || work.CurrentPlanId is null ||
            reviewed.Count!=1 || work.SkippedReview.Count!=0 || _db.HasUnreconciledCopy)
            throw new IOException("Later Move needs exactly one currently approved reviewed path.");
        var prepared=await DurableCopyCoordinator.PrepareAsync(work,reviewed,
            work.CurrentPlanId.Value,work.Revision,cancel);
        var p=prepared.Single();
        CheckFixture(p);CheckSource(p);RequireExactSuccessor(p,candidate);
        if(!_db.InspectAdditionalEpochAuthorization(candidate).Verified)
            throw new IOException("Later-epoch proposal is stale or unowned.");
        return p;
    }

    public void ApproveAdditionalEpoch(SortingWorkspace work,PreparedCopy prepared,
        DurableMoveItem candidate,bool explicitlyAcknowledged)
    {
        if(!explicitlyAcknowledged || !work.IsCurrentPlanApproved ||
            work.CurrentPlanId!=prepared.PlanId || work.Revision!=prepared.Revision ||
            work.SessionId!=prepared.SessionId)
            throw new IOException("Explicit later-epoch Move review acknowledgement missing.");
        CheckFixture(prepared);CheckSource(prepared);RequireExactSuccessor(prepared,candidate);
        _db.ApproveAdditionalEpochAuthorization(candidate,true);
    }

    public Task<DurableMoveItem> ExecuteApprovedAdditionalEpochAsync(
        SortingWorkspace work,PreparedCopy prepared,DurableMoveItem candidate,
        CancellationToken cancel=default)
        => ExecuteApprovedCoreAsync(work,prepared,cancel,candidate.Id,candidate);

    private static void RequireExactSuccessor(PreparedCopy p,DurableMoveItem row)
    {
        if (row.SessionId!=p.SessionId || row.PhotoId!=p.PhotoId || row.PlanId!=p.PlanId ||
            row.PlanRevision!=p.Revision || row.RouteKey!=p.Key ||
            row.Source!=p.Source || row.Destination!=p.Destination ||
            row.OutputRoot!=p.OutputRoot || row.SourceHash!=p.Hash ||
            row.SourceIdentity!=p.Identity || row.SourceLength!=p.Length ||
            row.SourceWriteTicks!=p.WriteTicks || row.State!="Intent" ||
            row.Error is not null || row.Id==Guid.Empty ||
            row.TempIdentity is not null || row.TargetIdentity is not null)
            throw new IOException("Move ticket differs from freshly reviewed exact plan.");
        ConfirmPaths(row);
    }

    public void ApproveStagedSuccessor(SortingWorkspace work,PreparedCopy p,
        DurableMoveItem ticket,bool explicitlyAcknowledged)
    {
        if (!explicitlyAcknowledged || !work.IsCurrentPlanApproved ||
            work.CurrentPlanId!=p.PlanId || work.Revision!=p.Revision ||
            work.SessionId!=p.SessionId)
            throw new IOException("Explicit reviewed Move authorization missing or stale.");
        CheckFixture(p);CheckSource(p);RequireExactSuccessor(p,ticket);
        _db.ApproveMoveEpochGrant(ticket);
    }

    public Task<DurableMoveItem> ExecuteApprovedStagedSuccessorAsync(
        SortingWorkspace work,PreparedCopy p,DurableMoveItem ticket,
        CancellationToken cancel=default)
        => ExecuteApprovedCoreAsync(work,p,cancel,ticket.Id,ticket);

    // M6J-A3-P2B: test-only exact Copy->Move integration. The ordinary path,
    // staged same-family successor, and all production permission checks remain
    // unchanged. Review consumption + FIRST durable Move Intent are atomic;
    // no file-system mutation occurs until after that SQLite commit succeeds.
    internal Task<DurableMoveItem> ExecuteM6JReviewedGeneratedCopyMoveAsync(
        SortingWorkspace work,PreparedCopy prepared,DurableMoveItem reviewedIntent,
        CancellationToken cancel=default)
    {
        if (_ordinaryFirstEpoch || _fixtureRoot is null)
            throw new IOException("M6J cross-family Move is disposable-fixture-only.");
        return ExecuteApprovedCoreAsync(work,prepared,cancel,reviewedIntent.Id,
            reviewedIntent,reviewedCrossFamily:true);
    }

    // P4-R26: exactly TWO separately approved generated Copies and the R25
    // operation-specific Reviewed seal. The generic/ordinary Move paths remain locked.
    internal Task<DurableMoveItem> ExecuteM6JP4R26ThirdGeneratedMoveAsync(
        SortingWorkspace work,PreparedCopy prepared,M6JProspectiveCandidate candidate,
        M6JP4R25ThirdMoveReviewReceipt reviewed,DurableMoveItem exactIntent,
        CancellationToken cancel=default)
    {
        if(_ordinaryFirstEpoch || _fixtureRoot is null || candidate is null ||
           reviewed is null || exactIntent is null || exactIntent.Id!=reviewed.ReviewId ||
           candidate.Id!=reviewed.ReviewId || exactIntent.State!="Intent")
            throw new IOException("R26 generated-only third-Move authorization refused.");
        return ExecuteApprovedCoreAsync(work,prepared,cancel,exactIntent.Id,exactIntent,
            twoCopyThirdMove:true,r26Candidate:candidate,r26Reviewed:reviewed);
    }

    // P4-R6: ONLY the exact, precommitted singleton generated Copy -> Move
    // Intent may continue through the existing no-replace Move coordinator.
    // This is not recovery from an in-progress filesystem operation: the
    // persisted row MUST still be Intent with zero owned output/archive bytes.
    // The P4-R5 proof is freshly repeated inside the coordinator BEFORE any
    // directory creation. Ordinary user photos and generic Move paths remain
    // bound to their original all-installation history restrictions.
    internal Task<DurableMoveItem> ExecuteM6JP4R6ColdPrecommittedGeneratedCopyMoveAsync(
        SortingWorkspace work, PreparedCopy prepared, DurableMoveItem exactIntent,
        CancellationToken cancel=default)
    {
        if (_ordinaryFirstEpoch || _fixtureRoot is null ||
            exactIntent is null || exactIntent.State!="Intent" ||
            !_db.InspectM6JP4R5GeneratedConsumedLineage(exactIntent.Id).Verified)
            throw new IOException("P4-R6 cold generated Intent lacks exact native predecessor lineage.");
        return ExecuteApprovedCoreAsync(work,prepared,cancel,exactIntent.Id,
            exactIntent,reviewedCrossFamily:true,precommittedGeneratedIntent:true);
    }

    public Task<DurableMoveItem> ExecuteApprovedAsync(SortingWorkspace work, PreparedCopy p,
        CancellationToken cancel=default, Guid? reviewedOperationId=null)
        => ExecuteApprovedCoreAsync(work,p,cancel,reviewedOperationId,null);

    private async Task<DurableMoveItem> ExecuteApprovedCoreAsync(SortingWorkspace work,PreparedCopy p,
        CancellationToken cancel,Guid? reviewedOperationId,DurableMoveItem? successorTicket,
        bool reviewedCrossFamily=false, bool precommittedGeneratedIntent=false,
        bool twoCopyThirdMove=false,M6JProspectiveCandidate? r26Candidate=null,
        M6JP4R25ThirdMoveReviewReceipt? r26Reviewed=null)
    {
        _db.RequireNoR30Writer();
        cancel.ThrowIfCancellationRequested();
        if (!work.IsCurrentPlanApproved || work.SessionId!=p.SessionId ||
            work.CurrentPlanId!=p.PlanId || work.Revision!=p.Revision)
            throw new IOException("Move plan no longer matches explicit approval; no files changed.");
        // A3 cross-family is an entirely separate, marked-fixture-only path.
        // Never weaken the generic all-session Intent/history lock.
        if (twoCopyThirdMove)
        {
            if (_fixtureRoot is null || _ordinaryFirstEpoch || successorTicket is null ||
                r26Candidate is null || r26Reviewed is null ||
                _db.HasUnreconciledCopy || _db.AllCopyHistoryCount()!=2 ||
                _db.AllMoveHistoryCount()!=0 || _db.AllRenameHistoryCount()!=0 ||
                _db.AllRejectHistoryCount()!=0 ||
                successorTicket.Id!=r26Candidate.Id || r26Reviewed.ReviewId!=successorTicket.Id)
                throw new IOException("R26 exact two-Copy native predecessor or review absent.");
            RequireExactSuccessor(p,successorTicket);
        }
        else if (reviewedCrossFamily)
        {
            if (_fixtureRoot is null || _ordinaryFirstEpoch || successorTicket is null ||
                _db.HasUnreconciledCopy || _db.AllCopyHistoryCount()!=1 ||
                _db.AllMoveHistoryCount()!=(precommittedGeneratedIntent ? 1 : 0) ||
                _db.AllRenameHistoryCount()!=0 || _db.AllRejectHistoryCount()!=0)
                throw new IOException("Exact generated Copy predecessor is unavailable.");
            RequireExactSuccessor(p,successorTicket);
            if (precommittedGeneratedIntent)
            {
                // Distinguish the durable consumed Intent from a merely
                // reviewed proposal. An Intent with partial execution state
                // is always refused; do not auto-replay interrupted steps.
                var saved=_db.MoveItems(successorTicket.SessionId);
                if (saved.Count!=1 || saved[0]!=successorTicket ||
                    !_db.InspectM6JP4R5GeneratedConsumedLineage(successorTicket.Id).Verified)
                    throw new IOException("Persisted Move Intent differs from the cold native lineage proof.");
            }
            else
            {
                var readiness=_db.InspectM6JMoveAfterCopyIntent(successorTicket.Id,successorTicket);
                if (!readiness.IsExactPreparedShape)
                    throw new IOException("Exact reviewed cross-family native shape is stale: "+
                        readiness.Explanation);
            }
        }
        else if (precommittedGeneratedIntent)
            throw new IOException("P4-R6 precommitted Intent cannot enter generic Move execution.");
        else
        {
            // Frozen original behavior for ordinary and same-family Move.
            if (successorTicket is null) _db.RequireFreshMoveEpoch();
            if (_db.HasUnreconciledCopy || _db.CopyItems(p.SessionId).Count!=0 ||
                _db.MoveItems(p.SessionId).Count!=0)
                throw new IOException("Existing current-session history blocks a new Move.");
            if (successorTicket is not null)
            {
                RequireExactSuccessor(p,successorTicket);
                if (_db.AllMoveHistoryCount()>=2)
                    _db.RequireApprovedAdditionalEpochAuthorization(successorTicket);
                else
                    _db.RequireApprovedMoveEpochGrant(successorTicket);
            }
        }
        CheckOrigin(work,p);
        CheckSource(p);
        if (!Empty(p.Destination)) throw new IOException("Exact destination is occupied. Nothing moved.");
        var free=new DriveInfo(Path.GetPathRoot(p.Source)!).AvailableFreeSpace;
        if (free < p.Length + 16L*1024*1024)
            throw new IOException("Insufficient source-volume free space for verified Move and recovery reserve.");
        string folder=Path.GetDirectoryName(p.Destination)!;
        if (SortingWorkspace.HasLinkedExistingAncestor(folder))
            throw new IOException("Output path has a link/reparse component.");
        string sourceParent=Path.GetDirectoryName(p.Source)!;
        string recoveryRoot=Path.Combine(sourceParent,".photosift-reborn-recovery");
        var installId=_db.MoveInstallationId();
        if (Directory.Exists(recoveryRoot) &&
            (!File.Exists(Path.Combine(recoveryRoot,".photosift-move-owner-v1")) ||
             SortingWorkspace.HasLinkedExistingAncestor(recoveryRoot)))
            throw new IOException("Existing unowned or linked recovery root; files left unchanged.");
        var id=reviewedOperationId ?? Guid.NewGuid();
        if (id==Guid.Empty) throw new IOException("Invalid reviewed Move operation identity.");
        string operationRoot=Path.Combine(recoveryRoot,installId.ToString("N"),p.SessionId.ToString("N"),id.ToString("N"));
        var row = successorTicket ?? new DurableMoveItem(id,installId,p.SessionId,p.PhotoId,p.PlanId,p.Revision,
            DateTime.UtcNow.Ticks,p.Source,p.Destination,Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part"),
            Path.Combine(operationRoot,Path.GetFileName(p.Source)),
            Path.Combine(operationRoot,"installed-"+Path.GetFileName(p.Destination)),p.OutputRoot,
            p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,"Intent",null,p.Key);
        ConfirmPaths(row);
        if (successorTicket is not null)
        {
            if (successorTicket.InstallationId!=installId || successorTicket.Id!=id ||
                !SortingWorkspace.PathComparer.Equals(successorTicket.OriginalArchive,
                    Path.Combine(operationRoot,Path.GetFileName(p.Source))) ||
                !SortingWorkspace.PathComparer.Equals(successorTicket.Temp,
                    Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part")))
                throw new IOException("Successor paths or installation differ from the executable Move operation.");
            if (!reviewedCrossFamily && !twoCopyThirdMove)
            {
                if (_db.AllMoveHistoryCount()>=2)
                    _db.RequireApprovedAdditionalEpochAuthorization(row);
                else
                    _db.RequireApprovedMoveEpochGrant(row);
            }
        }
        if (twoCopyThirdMove)
        {
            if (successorTicket is null || r26Candidate is null || r26Reviewed is null ||
                !_db.RecordM6JP4R26ThirdMoveIntent(work,p,r26Candidate,r26Reviewed,row)
                    .IntentDurable)
                throw new IOException("R26 reviewed two-Copy third Move atomic Intent refused.");
        }
        else if (reviewedCrossFamily)
        {
            if (precommittedGeneratedIntent)
            {
                // BEFORE first filesystem change re-read the exactly
                // persisted raw Copy and Intent lineage + current file bytes.
                // Never insert a duplicate Intent or reconsume the review.
                // No second-generation filesystem step is inferred from an
                // interrupted TempReady/TargetPublished/ArchiveIntent row.
                if (successorTicket is null ||
                    !_db.InspectM6JP4R5GeneratedConsumedLineage(row.Id).Verified ||
                    _db.MoveItems(row.SessionId).Count!=1 ||
                    _db.MoveItems(row.SessionId)[0]!=row)
                    throw new IOException("P4-R6 precommitted generated Intent changed before filesystem execution.");
            }
            else
            {
                // Frozen existing path: atomic review consumption + Intent.
                if (successorTicket is null ||
                    !_db.RecordM6JGeneratedOnlyCopyMoveIntent(row.Id,row).JournalCommitted)
                    throw new IOException("Cross-family atomic Intent commit was refused.");
            }
        }
        else _db.PutMoveItem(row); // frozen native path
        void PutNativeRow(DurableMoveItem next)
        {
            if (twoCopyThirdMove) _db.PutM6JP4R26ThirdMoveState(next);
            else _db.PutMoveItem(next);
        }
        Cut("MOVE_INTENT_RECORDED");
        try
        {
            EnsureArchiveDirectories(row);
            if (SortingWorkspace.HasLinkedExistingAncestor(folder)) throw new IOException("Linked output directory.");
            Directory.CreateDirectory(folder);
            if (SortingWorkspace.HasLinkedExistingAncestor(folder) ||
                NativeFileCapability.DeviceId(folder)!=NativeFileCapability.DeviceId(p.Source))
                throw new IOException("Output and source must be on the same verified local volume.");
            Cut("MOVE_DIRECTORIES_READY");
            CheckSource(p);
            if (!Empty(p.Destination) || !Empty(row.Temp)) throw new IOException("Output or temporary path unexpectedly occupied.");
            await using (var input=new FileStream(p.Source,FileMode.Open,FileAccess.Read,FileShare.Read))
            await using (var temp=new FileStream(row.Temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                await input.CopyToAsync(temp,cancel);
                await temp.FlushAsync(cancel);
                temp.Flush(flushToDisk:true);
            }
            Cut("MOVE_TEMP_FLUSHED_UNRECORDED");
            string tempId=NativeFileCapability.Identity(row.Temp);
            row=row with { TempIdentity=tempId,State="TempReady" }; PutNativeRow(row);
            Cut("MOVE_TEMP_RECORDED");
            if (!Matches(row.Temp,tempId,p.Hash,p.Length))
                throw new IOException("Output temporary bytes changed.");
            CheckSource(p);
            if (!Empty(row.Destination)) throw new IOException("Destination appeared during Move.");
            NativeFileCapability.PublishLinkNoReplace(row.Temp,row.Destination);
            Cut("MOVE_OUTPUT_PUBLISHED_UNJOURNALLED");
            if (!Matches(row.Destination,tempId,p.Hash,p.Length))
                throw new IOException("Published output cannot be proven owned.");
            row=row with {TargetIdentity=tempId,State="TargetPublished"};PutNativeRow(row);
            Cut("MOVE_OUTPUT_PUBLISHED_RECORDED");
            // No original may be relocated until target is durably observed.
            CheckSource(p);
            if (!work.IsCurrentPlanApproved || work.SessionId!=p.SessionId ||
                work.CurrentPlanId!=p.PlanId || work.Revision!=p.Revision)
                throw new IOException("Review approval changed before original relocation.");
            EnsureArchiveDirectories(row);
            if (!Matches(row.Destination,row.TargetIdentity,p.Hash,p.Length))
                throw new IOException("Verified output changed before source relocation.");
            row=row with {State="ArchiveIntent"};PutNativeRow(row);
            Cut("MOVE_SOURCE_ARCHIVE_INTENT");
            NativeFileCapability.RelocateNoReplace(row.Source,row.OriginalArchive);
            Cut("MOVE_SOURCE_RELOCATED_UNJOURNALLED");
            if (!Matches(row.OriginalArchive,p.Identity,p.Hash,p.Length) || !Empty(row.Source))
                throw new IOException("Archived original cannot be verified. Manual recovery required.");
            row=row with {State="SourceArchived"};PutNativeRow(row);
            Cut("MOVE_SOURCE_ARCHIVED_RECORDED");
            // Temp hard link is owned only when its identity and full hash still match.
            if (Matches(row.Temp,tempId,p.Hash,p.Length)) File.Delete(row.Temp);
            else throw new IOException("Temporary output ownership is uncertain; preserve it and the journal.");
            row=row with {State="Committed"};PutNativeRow(row);
            Cut("MOVE_COMMITTED");
            return row;
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            try { PutNativeRow(row with {State="NeedsAttention",Error=ex.GetType().Name+": "+ex.Message}); }
            catch { /* Preserve the original exception; never guess-delete. */ }
            throw;
        }
    }

    public Task<DurableMoveItem> UndoAsync(Guid session, CancellationToken cancel=default)
        => UndoCoreAsync(session,cancel,reviewedCrossFamily:false);

    // The ONLY mixed-family selective Undo in P2B is for the exact
    // consumed, single-Copy + single-Move synthetic ledger. An ordinary Undo
    // still rejects Copy history; no production UI entry point is added.
    internal Task<DurableMoveItem> UndoM6JReviewedGeneratedCopyMoveAsync(
        Guid session,CancellationToken cancel=default)
    {
        if (_ordinaryFirstEpoch || _fixtureRoot is null)
            throw new IOException("M6J selective Undo is limited to marked disposable fixtures.");
        return UndoCoreAsync(session,cancel,reviewedCrossFamily:true);
    }

    // P4-R7: the existing narrow Copy -> Move selective Undo additionally
    // requires a time-local four-handle proof of the still-preserved Copy
    // and the committed Move's two owned filesystem artifacts. No ordinary
    // Undo bypass; all other family histories remain vetoed in the frozen DB
    // prerequisite. The independent second observation runs after durable
    // UndoRestoreIntent is written and just before the first native relocation.
    private void RequireM6JP4R7FreshGeneratedUndo(DurableMoveItem move)
    {
        if(_ordinaryFirstEpoch || _fixtureRoot is null ||
           move.State is not ("Committed" or "UndoRestoreIntent") ||
           move.Error is not null || move.TargetIdentity is null ||
           _db.HasUnreconciledCopy || _db.AllCopyHistoryCount()!=1 ||
           _db.AllMoveHistoryCount()!=1 || _db.AllRenameHistoryCount()!=0 ||
           _db.AllRejectHistoryCount()!=0)
            throw new IOException("P4-R7 generated selective Undo history is not exact.");
        var copies=_db.CopyItems(move.SessionId);
        var moves=_db.MoveItems(move.SessionId);
        if(copies.Count!=1 || moves.Count!=1 || moves[0]!=move ||
           copies[0].State!="Committed" || copies[0].Error is not null ||
           copies[0].TargetIdentity is null || copies[0].PhotoId==move.PhotoId)
            throw new IOException("P4-R7 saved Copy/Move predecessor differs from the expected terminal pair.");
        var copy=copies[0];
        var report=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
            new(copy.Source,copy.SourceIdentity,copy.SourceHash,copy.SourceLength,copy.SourceWriteTicks),
            new(copy.Destination,copy.TargetIdentity!,copy.SourceHash,copy.SourceLength),
            new(move.OriginalArchive,move.SourceIdentity,move.SourceHash,move.SourceLength,move.SourceWriteTicks),
            new(move.Destination,move.TargetIdentity!,move.SourceHash,move.SourceLength));
        if(!report.IsFreshWitness || report.AuthorizesNativeOperation ||
           report.AuthorizesSelectiveUndo || report.IsDurableProof)
            throw new IOException("P4-R7 current Copy/Move four-handle Undo ownership refused: "+report.Explanation);
    }

    // P4-R8: the four live handles used before UndoRestoreIntent cannot be
    // reused after the source has been relocated. Before archiving the Move
    // output, obtain a NEW four-handle witness of the Copy source/output,
    // the now-restored Move source, and the still-installed Move output.
    // Execute the same observation again AFTER UndoOutputArchiveIntent is
    // persisted and BEFORE the second relocation. This is an instantaneous,
    // generated-only fail-closed observation, never an Undo grant.
    private void RequireM6JP4R8FreshGeneratedPostRestore(DurableMoveItem move)
    {
        if (_ordinaryFirstEpoch || _fixtureRoot is null ||
            move.State is not ("SourceRestored" or "UndoOutputArchiveIntent") ||
            move.Error is not null || move.TargetIdentity is null ||
            _db.HasUnreconciledCopy || _db.AllCopyHistoryCount()!=1 ||
            _db.AllMoveHistoryCount()!=1 || _db.AllRenameHistoryCount()!=0 ||
            _db.AllRejectHistoryCount()!=0)
            throw new IOException("P4-R8 post-restore generated Undo history not exact.");
        var copies=_db.CopyItems(move.SessionId);
        var moves=_db.MoveItems(move.SessionId);
        if (copies.Count!=1 || moves.Count!=1 || moves[0]!=move ||
            copies[0].State!="Committed" || copies[0].Error is not null ||
            copies[0].TargetIdentity is null || copies[0].PhotoId==move.PhotoId ||
            !Empty(move.OriginalArchive) || !Empty(move.OutputArchive) || !Empty(move.Temp))
            throw new IOException("P4-R8 post-restore Copy/Move evidence changed or a recovery path is occupied.");
        var copy=copies[0];
        var proof=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
            new(copy.Source,copy.SourceIdentity,copy.SourceHash,copy.SourceLength,copy.SourceWriteTicks),
            new(copy.Destination,copy.TargetIdentity!,copy.SourceHash,copy.SourceLength),
            new(move.Source,move.SourceIdentity,move.SourceHash,move.SourceLength,move.SourceWriteTicks),
            new(move.Destination,move.TargetIdentity!,move.SourceHash,move.SourceLength));
        if(!proof.IsFreshWitness || proof.AuthorizesNativeOperation ||
           proof.AuthorizesSelectiveUndo || proof.IsDurableProof)
            throw new IOException("P4-R8 post-restore four-handle ownership REFUSED: "+proof.Explanation);
    }

    private async Task<DurableMoveItem> UndoCoreAsync(Guid session,CancellationToken cancel,
        bool reviewedCrossFamily)
    {
        _db.RequireNoR30Writer();
        cancel.ThrowIfCancellationRequested();
        DurableMoveItem? authorized = null;
        if (reviewedCrossFamily)
            authorized=_db.RequireM6JConsumedCopyMoveForSelectiveUndo(session);
        else if (_db.HasUnreconciledCopy || _db.CopyItems(session).Count!=0)
            throw new IOException("Existing Copy history blocks Move Undo.");
        var items=_db.MoveItems(session);
        if (items.Count!=1 || items[0].State!="Committed" ||
            (reviewedCrossFamily && items[0]!=authorized))
            throw new IOException("Only one fully committed, exactly verified Move can be undone.");
        var row=items[0]; ConfirmPaths(row);
        if(reviewedCrossFamily) RequireM6JP4R7FreshGeneratedUndo(row);
        try
        {
            EnsureArchiveDirectories(row,originalMayExist:true); // retained original is expected
            if (!Empty(row.Source)) throw new IOException("Original path has been occupied; do not replace it.");
            if (!Matches(row.OriginalArchive,row.SourceIdentity,row.SourceHash,row.SourceLength) ||
                !Matches(row.Destination,row.TargetIdentity,row.SourceHash,row.SourceLength))
                throw new IOException("Original archive or installed output changed; preserve both.");
            row=row with {State="UndoRestoreIntent"};_db.PutMoveItem(row);
            Cut("MOVE_UNDO_RESTORE_INTENT");
            if(reviewedCrossFamily)
            {
                // The prior native files may change between inspection and
                // persisted Undo Intent. Revalidate before the FIRST relocation.
                // Fail closed with intact archives and NeedsAttention journal.
                RequireM6JP4R7FreshGeneratedUndo(row);
                if(!Empty(row.Source) || !Empty(row.OutputArchive))
                    throw new IOException("P4-R7 selective Undo output/original became occupied after Intent.");
            }
            NativeFileCapability.RelocateNoReplace(row.OriginalArchive,row.Source);
            Cut("MOVE_UNDO_SOURCE_RESTORED_UNJOURNALLED");
            if (!Matches(row.Source,row.SourceIdentity,row.SourceHash,row.SourceLength) || !Empty(row.OriginalArchive))
                throw new IOException("Restored source identity cannot be verified.");
            row=row with {State="SourceRestored"};_db.PutMoveItem(row);
            Cut("MOVE_UNDO_SOURCE_RESTORED_RECORDED");
            if(reviewedCrossFamily)
                RequireM6JP4R8FreshGeneratedPostRestore(row);
            if (!Matches(row.Destination,row.TargetIdentity,row.SourceHash,row.SourceLength) || !Empty(row.OutputArchive))
                throw new IOException("Output changed before Undo archive.");
            row=row with {State="UndoOutputArchiveIntent"};_db.PutMoveItem(row);
            Cut("MOVE_UNDO_OUTPUT_ARCHIVE_INTENT");
            if(reviewedCrossFamily)
                RequireM6JP4R8FreshGeneratedPostRestore(row);
            NativeFileCapability.RelocateNoReplace(row.Destination,row.OutputArchive);
            Cut("MOVE_UNDO_OUTPUT_RELOCATED_UNJOURNALLED");
            if (!Matches(row.OutputArchive,row.TargetIdentity,row.SourceHash,row.SourceLength) || !Empty(row.Destination))
                throw new IOException("Undone output archive ownership cannot be verified.");
            row=row with {State="Undone"};_db.PutMoveItem(row);
            Cut("MOVE_UNDO_COMMITTED");
            await Task.CompletedTask;
            return row;
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            try { _db.PutMoveItem(row with {State="NeedsAttention",Error=ex.GetType().Name+": "+ex.Message}); }
            catch { /* Do not overwrite a more important filesystem failure. */ }
            throw;
        }
    }

    // P6C2-B: POSITIVE HISTORICAL EVIDENCE ONLY, not new Move authority.
    // This native check never creates an archive, replays a journal, moves a
    // photo, or clears the P6C2-A installation-wide Intent veto. It applies
    // only to a completed UNDONE Move, not to a committed/unresolved operation.
    internal static bool ProveTerminalUndoneEvidence(DurableMoveItem row)
    {
        if (row.State!="Undone" || row.Error is not null ||
            row.TargetIdentity is null || row.Id==Guid.Empty ||
            row.InstallationId==Guid.Empty || row.SessionId==Guid.Empty ||
            row.SourceIdentity==row.TargetIdentity)
            return false;
        try
        {
            ConfirmPaths(row);
            var sourceParent=Path.GetDirectoryName(row.Source)!;
            var root=Path.Combine(sourceParent,".photosift-reborn-recovery");
            var operation=Path.GetDirectoryName(row.OriginalArchive)!;
            var rootMarker=Path.Combine(root,".photosift-move-owner-v1");
            var operationMarker=Path.Combine(operation,".photosift-operation-v1");
            if (!Directory.Exists(root) || !Directory.Exists(operation) ||
                SortingWorkspace.HasLinkedExistingAncestor(root) ||
                SortingWorkspace.HasLinkedExistingAncestor(operation) ||
                !File.Exists(rootMarker) || !File.Exists(operationMarker) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(rootMarker) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(operationMarker) ||
                File.ReadAllText(rootMarker)!=
                    "PhotoSift Reborn Move v1:"+row.InstallationId.ToString("N")+"\n" ||
                File.ReadAllText(operationMarker)!=
                    "PhotoSift Reborn Move operation "+row.Id.ToString("N")+"\n")
                return false;
            if (NativeFileCapability.DeviceId(row.Source)!=NativeFileCapability.DeviceId(root) ||
                !Matches(row.Source,row.SourceIdentity,row.SourceHash,row.SourceLength) ||
                new FileInfo(row.Source).LastWriteTimeUtc.Ticks!=row.SourceWriteTicks ||
                !Matches(row.OutputArchive,row.TargetIdentity,row.SourceHash,row.SourceLength))
                return false;
            // Any old, occupied target/temp/original archive makes a new
            // epoch ambiguous. Never remove these paths to make the proof pass.
            return Empty(row.Destination) && Empty(row.Temp) && Empty(row.OriginalArchive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // Read-only: NEVER infer that an interrupted move should be replayed or undone.
    public IReadOnlyList<MoveObservation> Inspect(Guid session) => InspectRecorded(_db,session);

    // Observation is available even for a locked, non-disposable database.
    // This must NEVER construct a Move executor or replay a journal operation.
    public static IReadOnlyList<MoveObservation> InspectRecorded(WorkspaceSessionStore db, Guid session)
        => db.MoveItems(session).Select(row =>
    {
        string evidence="SOURCE="+(Matches(row.Source,row.SourceIdentity,row.SourceHash,row.SourceLength)?"OWNED":Empty(row.Source)?"ABSENT":"UNKNOWN")+
            "; TARGET="+(Matches(row.Destination,row.TargetIdentity ?? row.TempIdentity,row.SourceHash,row.SourceLength)?"OWNED":Empty(row.Destination)?"ABSENT":"UNKNOWN")+
            "; ORIGINAL_ARCHIVE="+(Matches(row.OriginalArchive,row.SourceIdentity,row.SourceHash,row.SourceLength)?"OWNED":Empty(row.OriginalArchive)?"ABSENT":"UNKNOWN")+
            "; OUTPUT_ARCHIVE="+(Matches(row.OutputArchive,row.TargetIdentity,row.SourceHash,row.SourceLength)?"OWNED":Empty(row.OutputArchive)?"ABSENT":"UNKNOWN");
        return new MoveObservation(row.Id,row.State,row.Source,row.Destination,
            row.OriginalArchive,row.OutputArchive,evidence,row.Error);
    }).ToArray();
}
