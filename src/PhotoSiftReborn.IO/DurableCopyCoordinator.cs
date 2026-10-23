using System.Security.Cryptography;

namespace PhotoSiftReborn;

public sealed record PreparedCopy(Guid PlanId, long Revision, Guid SessionId,
    Guid PhotoId, char Key, string Source, string Destination,
    long Length, long WriteTicks, string Hash, string Identity, string OutputRoot);

public sealed record CopyBatchResult(int Completed, int NeedsAttention, string? Detail);

// P3B2 test-only synthetic process loss: the test harness catches it OUTSIDE
// the executor, simulating an abrupt exit after a recorded filesystem boundary.
// This does NOT simulate a real power loss, fsync/rename durability or an OS kill.
public sealed class SimulatedInterruptionException : Exception
{
    public SimulatedInterruptionException(string cut) : base("Injected abrupt stop after " + cut) { }
}

public sealed record RecoveryCase(Guid OperationId, string State, string Guidance,
    string Source, string Destination, string Temporary, string Recovery, string? LastError,
    string Evidence);


/// <summary>
/// P3B bounded local copy/restart-undo journal. Serial executor: callers must not invoke
/// concurrently on one WorkspaceSessionStore; the Avalonia presenter owns that boundary.
/// Filesystem and SQLite are not one atomic transaction. Never erase an ambiguous file.
/// </summary>
public sealed class DurableCopyCoordinator
{
    private readonly WorkspaceSessionStore _db;
    private readonly Action<string>? _testCut;
    public DurableCopyCoordinator(WorkspaceSessionStore db) => _db=db;

    // Deliberately unavailable in normal application startup: test injection
    // requires the exact disposable-root marker created by SelfTests.Root().
    public DurableCopyCoordinator(WorkspaceSessionStore db, Action<string> testCut)
    {
        var fixtureRoot = Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)?.FullName;
        if (fixtureRoot is null ||
            !Path.GetFileName(fixtureRoot).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_", StringComparison.Ordinal) ||
            (!File.Exists(Path.Combine(fixtureRoot, "P3B2_TEST_ONLY.marker")) ||
             File.ReadAllText(Path.Combine(fixtureRoot, "P3B2_TEST_ONLY.marker")) != "synthetic-interruption-only"))
            throw new InvalidOperationException("Fault injection restricted to generated disposable self-test fixtures.");
        _db=db; _testCut=testCut;
    }
    private void Cut(string phase) => _testCut?.Invoke(phase);

    // A report must never update SQL or perform file operations. This is
    // intentionally diagnostic, NOT a permission to delete/move unknown paths.
    public IReadOnlyList<RecoveryCase> Inspect(Guid session) => _db.CopyItems(session).Select(item =>
        new RecoveryCase(item.Id, item.State, item.State switch
        {
            "Committed" => "Recorded as installed. Undo separately verifies file identity and bytes before archiving.",
            "Undone" => "Previously archived. Keep recovery contents until you choose to manage them outside this build.",
            "UndoIntent" => "Undo may have been interrupted. Preserve both destination and recovery; use Rescan to inspect current evidence.",
            "Intent" => "Copy may not have begun. Preserve any temp/output found at the recorded paths; no automatic replay.",
            "TempCreated" or "VerifiedTemp" => "A temporary copy may exist. Do not delete it or retry Apply before ownership is resolved.",
            "Published" => "Publication was recorded but full commit was not. Rescan can verify recorded identity; do not retry Apply.",
            _ => "NEEDS ATTENTION: ambiguous or changed file. Preserve all listed paths and journal; stop mutations."
        }, item.Source,item.Destination,item.Temp,item.Recovery,item.Error, ObserveEvidence(item))).ToArray();

    /// <summary>
    /// Observation ONLY: a historical Undone row remains Undone even when an
    /// archive is later deleted or replaced by a third party. It must never be
    /// labelled currently verified unless identity, size and bytes match.
    /// No SQL writes, no filesystem writes, no inferred ownership from digest alone.
    /// </summary>
    private static string ObserveEvidence(DurableCopyItem row)
    {
        if (row.State == "Undone")
        {
            if (File.Exists(row.Destination) || Directory.Exists(row.Destination))
                return "DESTINATION_PRESENT_AFTER_UNDO";
            if (!File.Exists(row.Recovery))
                return "ARCHIVE_MISSING";
            return MatchesOwnedFile(row.Recovery, row.TargetIdentity, row.SourceHash, row.SourceLength)
                ? "ARCHIVE_VERIFIED" : "ARCHIVE_CHANGED_OR_IDENTITY_MISMATCH";
        }
        if (row.State == "Committed")
            return MatchesOwnedFile(row.Destination, row.TargetIdentity, row.SourceHash, row.SourceLength)
                ? "OUTPUT_VERIFIED" : "OUTPUT_MISSING_OR_CHANGED";
        return "UNRESOLVED_NOT_AUTHORIZED";
    }


    public int Outstanding(Guid session) => _db.CopyItems(session).Count(i => i.State is "Committed" or "Published" or "VerifiedTemp" or "TempCreated" or "Intent" or "UndoIntent" or "NeedsAttention");
    public int Committed(Guid session) => _db.CopyItems(session).Count(i => i.State == "Committed");
    public int NeedsAttention(Guid session) => _db.CopyItems(session).Count(i => i.State is not ("Committed" or "Undone"));
    public IReadOnlyList<DurableCopyItem> CompletedItems(Guid session) => _db.CopyItems(session).Where(i=>i.State=="Committed").ToArray();

    private static void ValidateSafeRegularFile(string file)
    {
        if (!File.Exists(file) || SortingWorkspace.HasSymlinkOrReparsePoint(file))
            throw new IOException("File missing or path has symbolic-link/reparse component: " + file);
    }
    private static void VerifyOriginal(PreparedCopy p)
    {
        ValidateSafeRegularFile(p.Source);
        var info = new FileInfo(p.Source);
        if (info.Length!=p.Length || info.LastWriteTimeUtc.Ticks!=p.WriteTicks ||
            NativeFileCapability.Identity(p.Source)!=p.Identity)
            throw new IOException("Original has changed since review: " + p.Source);
        if (SortingWorkspace.ComputeSha256Async(p.Source).GetAwaiter().GetResult()!=p.Hash)
            throw new IOException("Original bytes changed since review: " + p.Source);
    }

    public static async Task<IReadOnlyList<PreparedCopy>> PrepareAsync(SortingWorkspace work,
        IReadOnlyList<SortPlanItem> items, Guid planId, long revision, CancellationToken cancel=default)
    {
        if (!NativeFileCapability.IsSupported)
            throw new PlatformNotSupportedException("Native ownership/no-replace support is not verified on this OS.");
        if (work.CurrentPlanId != planId || work.Revision != revision)
            throw new IOException("The requested plan changed before source verification.");
        var result = new List<PreparedCopy>();
        foreach (var item in items)
        {
            cancel.ThrowIfCancellationRequested();
            ValidateSafeRegularFile(item.Source);
            var photo = work.Photos.Single(p => SortingWorkspace.PathComparer.Equals(p.Path,item.Source));
            string identity = NativeFileCapability.Identity(item.Source);
            string hash = await SortingWorkspace.ComputeSha256Async(item.Source,cancel);
            var info=new FileInfo(item.Source);
            if (info.Length!=item.ExpectedLength || info.LastWriteTimeUtc.Ticks!=item.ExpectedWriteUtcTicks ||
                NativeFileCapability.Identity(item.Source)!=identity ||
                File.Exists(item.Destination) || Directory.Exists(item.Destination))
                throw new IOException("Source or exact destination changed during Review. Start a new review.");
            string root = work.CheckedRouteDirectory(item.Key);
            if (!SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(item.Destination),root))
                throw new IOException("Reviewed destination no longer matches its typed route.");
            result.Add(new PreparedCopy(planId,revision,work.SessionId,photo.Id,item.Key,
                item.Source,item.Destination,info.Length,info.LastWriteTimeUtc.Ticks,hash,identity,
                work.Routes.Single(r => r.Key == item.Key).Kind == RouteKind.Absolute ? root : work.DestinationRoot!));
        }
        return result;
    }

    private static bool MatchesOwnedFile(string path, string? expectedIdentity, string digest, long? length=null)
    {
        if (expectedIdentity is null) return false;
        try
        {
            ValidateSafeRegularFile(path);
            if (NativeFileCapability.Identity(path)!=expectedIdentity) return false;
            if (length is { } n && new FileInfo(path).Length != n) return false;
            return SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==digest;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Read-only filesystem reconciliation; only journal observations are updated.</summary>
    public string Reconcile(Guid session)
    {
        int committed=0, attention=0, undone=0;
        foreach (var row in _db.CopyItems(session))
        {
            if (row.State=="Undone") { undone++; continue; }
            var item=row;
            if (row.State is "Committed" or "Published")
            {
                var expected=row.TargetIdentity ?? row.TempIdentity;
                if (MatchesOwnedFile(row.Destination,expected,row.SourceHash,row.SourceLength))
                {
                    item=row with { State="Committed", TargetIdentity=expected, Error=null };
                    committed++;
                }
                else { item=row with { State="NeedsAttention", Error="Installed file missing, changed or cannot be proven owned. Left untouched." }; attention++; }
            }
            else if (row.State=="NeedsAttention" &&
                row.TempIdentity is not null &&
                MatchesOwnedFile(row.Destination,row.TargetIdentity ?? row.TempIdentity,row.SourceHash,row.SourceLength))
            {
                // The published hard link still has the exact recorded temp inode.
                // Do not remove a leftover temp until separately checked; preserve it.
                item=row with { State="Committed",TargetIdentity=row.TargetIdentity ?? row.TempIdentity,Error=null };
                committed++;
            }
            else if (row.State=="UndoIntent")
            {
                if (!File.Exists(row.Destination) && MatchesOwnedFile(row.Recovery,row.TargetIdentity,row.SourceHash,row.SourceLength))
                { item=row with { State="Undone", Error=null }; undone++; }
                else { item=row with { State="NeedsAttention", Error="Interrupted Undo: source/output/recovery requires manual review; no automatic file action." }; attention++; }
            }
            else
            {
                item=row with { State="NeedsAttention", Error="Interrupted copy at "+row.State+". No automatic cleanup or publication." };
                attention++;
            }
            if (item!=row) _db.PutCopyItem(item);
        }
        return $"Journal: {committed} completed, {undone} already undone, {attention} need attention.";
    }

    public async Task<CopyBatchResult> ApplyAsync(IReadOnlyList<PreparedCopy> plan,
        CancellationToken cancel=default, CopyCommittedSuccessorGrant? successor=null)
    {
        _db.RequireNoR30Writer();
        if (plan.Count==0) throw new InvalidOperationException("No approved photographs to copy.");
        Guid session = plan[0].SessionId;
        if (plan.Any(p=>p.SessionId!=session || p.PlanId!=plan[0].PlanId || p.Revision!=plan[0].Revision))
            throw new IOException("Mixed reviewed plans cannot be applied.");
        if (successor is not null) _db.RequireExactCopySuccessorPlan(successor,plan[0]);
        if (_db.HasUnreconciledCopy) throw new IOException("An older unjournalled copy remains blocked; no ownership evidence to continue.");
        if (successor is not null && (plan.Count!=1 || _db.AllCopyHistoryCount()!=1 ||
            successor.SessionId!=session || successor.PlanId!=plan[0].PlanId ||
            successor.Revision!=plan[0].Revision || successor.PhotoId!=plan[0].PhotoId))
            throw new IOException("Committed Copy successor requires one exact reviewed new photograph.");
        if (successor is null && _db.HasAnyCopySuccessorGrant)
            throw new IOException("This installation has a Copy successor epoch; further unreviewed Copy is locked.");
        if (_db.AllCopyHistoryCount()!=0 && successor is null)
        {
            // A subsequent Copy cannot rely on a stale Undone journal label.
            // This fresh native proof must succeed before ANY new intent row.
            var saved=_db.Load() ?? throw new IOException("No saved workspace for Copy continuation.");
            var restored=new SortingWorkspace { IdentityResolver = NativeFileCapability.Identity };
            restored.RestoreSession(saved);
            var proof=CopyUndoContinuation.Inspect(_db,restored);
            if (!proof.Verified || restored.SessionId!=session)
                throw new IOException("New Copy refused: prior Copy Undo has no fresh terminal ownership proof. "+proof.Reason);
        }

        if (_db.AllRenameHistoryCount()!=0 && !_db.IsRenameCloseoutVerified(session))
            throw new IOException("Copy refuses unclosed, changed or ambiguous Rename history; verify Undo and explicitly close out first.");
        if (_db.AllMoveHistoryCount()!=0)
            throw new IOException("Copy refuses existing Move history until separately authorized.");
        if (NeedsAttention(session)!=0) throw new IOException("Resolve unresolved journal entries before further file changes.");
        if (!NativeFileCapability.IsSupported) throw new PlatformNotSupportedException("No trusted native ownership adapter.");
        int completed=0;
        string? failure=null;
        int serial=0;
        foreach (var p in plan)
        {
            cancel.ThrowIfCancellationRequested();
            DurableCopyItem? current=null;
            try
            {
                VerifyOriginal(p);
                string folder=Path.GetDirectoryName(p.Destination)!;
                string outputRoot = p.OutputRoot;
                if (!Directory.Exists(outputRoot) || SortingWorkspace.HasLinkedExistingAncestor(outputRoot) ||
                    !(SortingWorkspace.PathComparer.Equals(folder,outputRoot) ||
                      folder.StartsWith(Path.TrimEndingDirectorySeparator(outputRoot) + Path.DirectorySeparatorChar,
                          SortingWorkspace.PathComparison)) ||
                    SortingWorkspace.HasLinkedExistingAncestor(folder))
                    throw new IOException("The chosen output root is missing or linked.");
                if (File.Exists(p.Destination) || Directory.Exists(p.Destination))
                    throw new IOException("A reviewed destination now exists: "+p.Destination);
                Guid id=Guid.NewGuid();
                string temp=Path.Combine(folder,".photosift-"+id.ToString("N")+".part");
                string recoveryParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(outputRoot))
                    ?? throw new IOException("No safe output-parent for reversible Copy Undo.");
                string recovery=Path.Combine(recoveryParent,".photosift-reborn-recovery",p.SessionId.ToString("N"),id.ToString("N"),Path.GetFileName(p.Destination));
                // Record exact ownership-intended paths before touching the filesystem.
                current=new DurableCopyItem(id,session,p.PhotoId,p.Key,p.PlanId,p.Revision,
                    DateTime.UtcNow.Ticks + serial++,p.Source,p.Destination,temp,recovery,
                    p.Hash,p.Identity,p.Length,p.WriteTicks,null,null,0,"Intent",null);
                if (successor is null) _db.PutCopyItem(current);
                else _db.PutCopySuccessorIntent(current,successor.Id);
                // Intent and single-use grant consumption are durably atomic.
                Cut("INTENT_RECORDED");
                if (successor is not null)
                    _db.RecheckCommittedCopyAfterIntent(successor.Id,current.Id);
                Directory.CreateDirectory(folder);
                if (SortingWorkspace.HasSymlinkOrReparsePoint(folder))
                    throw new IOException("Destination directory includes a symbolic link.");
                Cut("OUTPUT_DIRECTORY_READY");
                if (File.Exists(p.Destination) || Directory.Exists(p.Destination))
                    throw new IOException("Destination appeared after journal intent.");
                await using (var input=new FileStream(p.Source,FileMode.Open,FileAccess.Read,FileShare.Read))
                await using (var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    await input.CopyToAsync(output,cancel);
                    await output.FlushAsync(cancel);
                    output.Flush(flushToDisk:true);
                }
                Cut("TEMP_FLUSHED_UNRECORDED");
                string tempId=NativeFileCapability.Identity(temp);
                current=current with {State="TempCreated",TempIdentity=tempId}; _db.PutCopyItem(current);
                Cut("TEMP_RECORDED");
                if (await SortingWorkspace.ComputeSha256Async(temp,cancel)!=p.Hash ||
                    await SortingWorkspace.ComputeSha256Async(p.Source,cancel)!=p.Hash ||
                    NativeFileCapability.Identity(p.Source)!=p.Identity)
                    throw new IOException("Source or temporary file changed during verification.");
                current=current with { State="VerifiedTemp" }; _db.PutCopyItem(current);
                Cut("TEMP_VERIFIED");
                if (File.Exists(p.Destination) || Directory.Exists(p.Destination))
                    throw new IOException("Destination appeared during copy.");
                NativeFileCapability.PublishLinkNoReplace(temp,p.Destination);
                Cut("OUTPUT_PUBLISHED_UNJOURNALLED");
                // Even if process stops here the temp inode is journalled and the link
                // can be recognized on next startup; never delete an unknown pathname.
                string finalId=NativeFileCapability.Identity(p.Destination);
                if (finalId!=tempId || await SortingWorkspace.ComputeSha256Async(p.Destination,cancel)!=p.Hash)
                    throw new IOException("Published output verification failed; output preserved for investigation.");
                current=current with {State="Published",TargetIdentity=finalId,
                    TargetWriteTicks=new FileInfo(p.Destination).LastWriteTimeUtc.Ticks};
                _db.PutCopyItem(current);
                Cut("OUTPUT_PUBLISHED_RECORDED");
                if (File.Exists(temp) && NativeFileCapability.Identity(temp)==tempId)
                    File.Delete(temp); // known, verified hard-link twin only
                Cut("TEMP_REMOVED_BEFORE_COMMIT");
                current=current with {State="Committed"};_db.PutCopyItem(current);
                Cut("COPY_COMMITTED");
                completed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (current is not null)
                {
                    try { _db.PutCopyItem(current with {State="NeedsAttention", Error=ex.Message}); }
                    catch { /* Never hide the original failure or delete files on journal failure. */ }
                }
                failure=ex.GetType().Name+": "+ex.Message;
                break;
            }
        }
        return new CopyBatchResult(completed,NeedsAttention(session),failure);
    }

    private static string PrepareRecoveryPath(DurableCopyItem item)
    {
        // The intended archive was frozen in the durable intent BEFORE publication.
        // Never infer a root by counting route-depth components in Destination:
        // nested Relative and native-picked Absolute routes have different depth.
        string destination=Path.GetFullPath(item.Recovery);
        var itemDir=Path.GetDirectoryName(destination);
        var sessionDir=itemDir is null ? null : Path.GetDirectoryName(itemDir);
        var owned=sessionDir is null ? null : Path.GetDirectoryName(sessionDir);
        if (owned is null || itemDir is null || sessionDir is null ||
            Path.GetFileName(itemDir)!=item.Id.ToString("N") ||
            Path.GetFileName(sessionDir)!=item.SessionId.ToString("N") ||
            Path.GetFileName(owned)!=".photosift-reborn-recovery" ||
            Path.GetFileName(destination)!=Path.GetFileName(item.Destination))
            throw new IOException("Saved recovery path does not match its journal operation; preserve all files.");
        bool firstUse = !Directory.Exists(owned);
        if (firstUse && SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(owned)!))
            throw new IOException("Recovery parent path is linked or unsafe.");
        if (firstUse) Directory.CreateDirectory(owned);
        if (SortingWorkspace.HasLinkedExistingAncestor(owned)) throw new IOException("Recovery root contains a symbolic link.");
        string marker=Path.Combine(owned,".photosift-owner-v1");
        if (!firstUse && !File.Exists(marker))
            throw new IOException("Recovery directory predates this app but has no owner marker. Refusing to claim it.");
        if (!File.Exists(marker))
        {
            using var output=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var bytes=System.Text.Encoding.UTF8.GetBytes("PhotoSift Reborn managed reversible output archives v1\n");
            output.Write(bytes); output.Flush(flushToDisk:true);
        }
        if (File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
            throw new IOException("Recovery ownership marker missing or altered.");
        if (SortingWorkspace.HasLinkedExistingAncestor(sessionDir))
            throw new IOException("Recovery session parent has a symlink; preserve existing files.");
        Directory.CreateDirectory(itemDir);
        if (SortingWorkspace.HasLinkedExistingAncestor(itemDir))
            throw new IOException("Recovery directory is linked.");
        return destination;
    }

    // Only one newest committed Copy may be independently Undone. The currently
    // supported two-row successor has been proven disjoint from its predecessor.
    public Task<CopyBatchResult> UndoLatestIndependentCopyAsync(Guid session,
        CancellationToken cancel=default)
    {
        if (!_db.HasAnyCopySuccessorGrant || _db.AllCopyHistoryCount()!=2 ||
            _db.AllMoveHistoryCount()!=0 || _db.AllRenameHistoryCount()!=0 ||
            _db.AllRejectHistoryCount()!=0)
            throw new IOException("Selective Copy Undo requires the exact two-row independent epoch.");
        _db.RequireIndependentLatestCopyUndo(session);
        return UndoAsync(session,cancel,latestIndependentOnly:true);
    }

    public async Task<CopyBatchResult> UndoAsync(Guid session, CancellationToken cancel=default,
        bool latestIndependentOnly=false)
    {
        _db.RequireNoR30Writer();
        if (_db.HasUnreconciledCopy) throw new IOException("Older unjournalled copies cannot be undone from persisted history.");
        if (NeedsAttention(session)!=0) throw new IOException("Journal has unresolved operations. Preserve all files and review before Undo.");
        var completed = _db.CopyItems(session).Where(i=>i.State=="Committed").Reverse().ToArray();
        if (latestIndependentOnly)
        {
            var all=_db.CopyItems(session);
            if (all.Count!=2 || completed.Length!=2 ||
                all[1].State!="Committed" || all[0].State!="Committed")
                throw new IOException("Only a proven latest Copy with its predecessor still Committed may be selected.");
            completed=completed.Take(1).ToArray();
        }
        if (completed.Length==0) throw new IOException("No verified journalled copies available for Undo.");
        int undone=0; string? error=null;
        foreach(var row in completed)
        {
            DurableCopyItem? pendingUndo = null;
            try
            {
                cancel.ThrowIfCancellationRequested();
                if (!MatchesOwnedFile(row.Source,row.SourceIdentity,row.SourceHash,row.SourceLength))
                    throw new IOException("Original is missing or changed; preserving the output.");
                if (!MatchesOwnedFile(row.Destination,row.TargetIdentity,row.SourceHash,row.SourceLength))
                    throw new IOException("Output ownership/content changed; refusing to remove or archive it.");
                string recovery=PrepareRecoveryPath(row);
                if (File.Exists(recovery) || Directory.Exists(recovery))
                    throw new IOException("Recovery destination is already occupied.");
                pendingUndo=row with {State="UndoIntent",Recovery=recovery};
                _db.PutCopyItem(pendingUndo);
                Cut("UNDO_INTENT_RECORDED");
                NativeFileCapability.RelocateNoReplace(row.Destination,recovery);
                Cut("UNDO_RELOCATED_UNJOURNALLED");
                if (!MatchesOwnedFile(recovery,row.TargetIdentity,row.SourceHash,row.SourceLength))
                    throw new IOException("Archived output cannot be verified; leave journal unresolved.");
                _db.PutCopyItem(pendingUndo with {State="Undone",Error=null});
                Cut("UNDO_ARCHIVED_RECORDED");
                undone++;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                error=ex.GetType().Name+": "+ex.Message;
                // Do not keep advertising an unsafe record as ready for Undo.
                // Even a byte-identical replacement is NOT the original owned inode.
                // If relocation might have occurred, retain UndoIntent and exact
                // recovery path so the next startup can safely reconcile.
                try { _db.PutCopyItem(pendingUndo is null
                    ? row with {State="NeedsAttention",Error=error}
                    : pendingUndo with {Error=error}); }
                catch { /* Preserve paths and surface the failure; no guessed deletion. */ }
                break;
            }
        }
        await Task.CompletedTask;
        return new CopyBatchResult(undone,NeedsAttention(session),error);
    }
}

internal static class PreparedCopyExtensions
{
    // Destination root is explicit in PreparedCopy.OutputRoot for any route depth.
    // This type remains solely for compatibility with historical source references.
}
