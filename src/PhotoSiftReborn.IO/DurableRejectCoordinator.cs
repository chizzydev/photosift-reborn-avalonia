using System.Text.Json;

namespace PhotoSiftReborn;

public sealed record RejectRecoveryObservation(Guid Operation,string State,string Source,
    string Archive,string Evidence);

/// <summary>
/// P6D2A fixture lane plus P6F2B opt-in ordinary-folder backend lane.
/// Normal GUI remains disabled pending owner cross-platform/GUI acceptance.
/// Explicitly staged/approved irreversible-looking
/// actions instead preserve an owned, separately journalled native original.
/// No Trash, no permanent delete, no automatic retry/replay. Native no-replace
/// is required for Reject AND Undo. Ordinary mode is single-source-root,
/// single pending mark, journal-empty only; not production GUI authorization.
/// </summary>
public sealed class DurableRejectCoordinator
{
    private readonly WorkspaceSessionStore _db;
    private readonly string _root;
    private readonly string _sourceDirectory;
    private readonly bool _ordinary;
    private readonly Action<string>? _cut;
    public DurableRejectCoordinator(WorkspaceSessionStore db,Action<string>? interruptionCut=null)
    {
        if(!NativeFileCapability.IsSupported || !P5CGuiFixturePolicy.IsMarkedStore(db))
            throw new IOException("Native Reject locked to a marked, generated disposable GUI fixture.");
        _root=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        _sourceDirectory=Path.Combine(_root,"source");
        _ordinary=false;
        _db=db;_cut=interruptionCut;
    }
    // Production-style core is deliberately NOT wired to ordinary GUI controls in
    // P6F2B. Native test uses newly created disposable ordinary-shaped folders
    // *without* any historical P5C/P3B2 marker or environment grant.
    private DurableRejectCoordinator(WorkspaceSessionStore db,string sourceDirectory,
        Action<string>? interruptionCut)
    {
        if(!NativeFileCapability.IsSupported)
            throw new PlatformNotSupportedException("Ordinary Reject requires Windows/Linux native no-replace.");
        _db=db;_cut=interruptionCut;_ordinary=true;
        _root="";
        _sourceDirectory=Path.GetFullPath(sourceDirectory);
        RequireOrdinarySourceRoot(_sourceDirectory,db);
    }

    public static DurableRejectCoordinator ForOrdinaryWorkspace(WorkspaceSessionStore db,
        SortingWorkspace workspace, Action<string>? interruptionCut=null)
    {
        string? source=workspace.SourceRoot;
        if(source is null)
            throw new IOException("Ordinary Reject needs a saved single source folder.");
        RequireOrdinaryWorkspace(db,workspace,source);
        return new DurableRejectCoordinator(db,source,interruptionCut);
    }

    // P6F2D: historical ownership proof is read-only and does NOT provide
    // permission to stage/approve/execute another Reject. Unlike a live
    // coordinator it must remain able to audit earlier sessions after the
    // saved GUI checkpoint changes. All native ownership and identity checks
    // still apply; only the current-session binding is omitted for history.
    internal static DurableRejectCoordinator ForOrdinaryHistoricalEvidence(
        WorkspaceSessionStore db,string sourceDirectory) =>
        new DurableRejectCoordinator(db,sourceDirectory,null);

    private static bool SameOrInside(string child,string parent)
    {
        string rel=Path.GetRelativePath(parent,child);
        return rel=="." || (!Path.IsPathRooted(rel) && rel!=".." &&
            !rel.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
    }

    private static void RequireOrdinarySourceRoot(string source,WorkspaceSessionStore db)
    {
        var root=Path.GetPathRoot(source);
        if(string.IsNullOrWhiteSpace(root) ||
           SortingWorkspace.PathComparer.Equals(Path.TrimEndingDirectorySeparator(source),
               Path.TrimEndingDirectorySeparator(root!)) ||
           !Directory.Exists(source) || SortingWorkspace.HasLinkedExistingAncestor(source) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(db.DatabasePath)!) ||
           SameOrInside(Path.GetDirectoryName(db.DatabasePath)!,source) ||
           SameOrInside(source,Path.GetDirectoryName(db.DatabasePath)!))
            throw new IOException("Ordinary Reject source is a root, linked, missing or overlaps the workspace state directory.");
    }

    private static void RequireOrdinaryWorkspace(WorkspaceSessionStore db,
        SortingWorkspace work,string source)
    {
        RequireOrdinarySourceRoot(source,db);
        // A multi-source/mixed-folder/imported descendant is NOT eligible for this
        // first lane. Future expansion requires a separate explicit review contract.
        string? output=work.DestinationRoot;
        if(work.SourceRoots.Count!=1 || !SortingWorkspace.PathComparer.Equals(work.SourceRoots[0],source) ||
           work.Photos.Count==0 || work.Photos.Any(p=>
               !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Path),source)) ||
           output is null || !Directory.Exists(output) ||
           SortingWorkspace.HasLinkedExistingAncestor(output) ||
           SameOrInside(output,source) || SameOrInside(source,output))
            throw new IOException("Ordinary Reject requires one nonlinked flat source and a separate existing output folder.");
        var saved=db.Load() ?? throw new IOException("Ordinary Reject has no saved session checkpoint.");
        if(saved.SessionId!=work.SessionId ||
           !SortingWorkspace.PathComparer.Equals(saved.SourceRoot,source) ||
           !SortingWorkspace.PathComparer.Equals(saved.DestinationRoot,output) ||
           saved.SourceRoots is null || saved.SourceRoots.Length!=1 ||
           !SortingWorkspace.PathComparer.Equals(saved.SourceRoots[0],source))
            throw new IOException("Ordinary Reject source/output do not match the saved session.");
    }

    private void RequireOrdinarySavedScope()
    {
        if(!_ordinary)return;
        RequireOrdinarySourceRoot(SourceDir,_db);
        var saved=_db.Load() ?? throw new IOException("Missing ordinary Reject snapshot.");
        string? source=saved.SourceRoot;
        string? output=saved.DestinationRoot;
        if(source is null ||
           !SortingWorkspace.PathComparer.Equals(source,SourceDir) ||
           saved.SourceRoots is null || saved.SourceRoots.Length!=1 ||
           !SortingWorkspace.PathComparer.Equals(saved.SourceRoots[0],SourceDir) ||
           output is null || !Directory.Exists(output) ||
           SortingWorkspace.HasLinkedExistingAncestor(output) ||
           SameOrInside(output,SourceDir) || SameOrInside(SourceDir,output) ||
           saved.Photos is null || saved.Photos.Length==0 || saved.Photos.Any(p=>
               !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Path),SourceDir)))
            throw new IOException("Saved ordinary Reject source scope changed; preserve history.");
    }

    private void Cut(string phase)=>_cut?.Invoke(phase);
    private static bool Empty(string p)=>!File.Exists(p) && !Directory.Exists(p);
    private string SourceDir=>_sourceDirectory;
    private string ArchiveRoot=>Path.Combine(SourceDir,".photosift-reborn-reject-recovery");
    private string RootMarker=>Path.Combine(ArchiveRoot,".photosift-reject-owner-v1");
    private string MarkerText=>"PhotoSift Reborn Reject v1:"+_db.MoveInstallationId().ToString("N")+"\n";

    private void CheckPaths(DurableRejectItem r,Guid? verifiedInstallation=null,
        bool historicalOnly=false)
    {
        string opRoot=Path.Combine(ArchiveRoot,r.InstallationId.ToString("N"),
            r.SessionId.ToString("N"),r.Id.ToString("N"));
        if(r.InstallationId!=(verifiedInstallation ?? _db.MoveInstallationId()) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(r.Source),SourceDir) ||
            !SortingWorkspace.PathComparer.Equals(r.Archive,Path.Combine(opRoot,Path.GetFileName(r.Source))) ||
            (!_ordinary && !Directory.Exists(Path.Combine(_root,"output"))) ||
            SortingWorkspace.HasLinkedExistingAncestor(SourceDir) ||
            SortingWorkspace.HasLinkedExistingAncestor(ArchiveRoot) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(r.Archive)!))
            throw new IOException("Reject archive path/installation is outside the exact owned source folder.");
        if(!historicalOnly)RequireOrdinarySavedScope();
    }
    private static string Hash(string p) => SortingWorkspace.ComputeSha256Async(p).GetAwaiter().GetResult();
    private static void CheckRegular(string p)
    {
        if(!File.Exists(p) || SortingWorkspace.HasSymlinkOrReparsePoint(p) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(p)!))
            throw new IOException("Reject original or archive is absent, linked or not an ordinary file.");
    }
    private static void Prove(string path,DurableRejectItem row,bool checkTicks)
    {
        CheckRegular(path);
        var info=new FileInfo(path);
        if(info.Length!=row.SourceLength ||
           (checkTicks && info.LastWriteTimeUtc.Ticks!=row.SourceWriteTicks) ||
           NativeFileCapability.Identity(path)!=row.SourceIdentity ||
           Hash(path)!=row.SourceHash)
            throw new IOException("Reject native original identity/bytes changed; preserve existing files.");
    }
    private void CheckUnoccupied(DurableRejectItem r)
    {
        CheckPaths(r);
        if(!Empty(r.Archive) || File.Exists(Path.GetDirectoryName(r.Archive)!))
            throw new IOException("Reject recovery destination/operation path already occupied.");
        if(Directory.Exists(ArchiveRoot))
        {
            if(!File.Exists(RootMarker) || SortingWorkspace.HasSymlinkOrReparsePoint(RootMarker) ||
                File.ReadAllText(RootMarker)!=MarkerText)
                throw new IOException("Reject recovery root is unowned; no takeover.");
        }
    }
    private void EnsureOwnedDirectories(DurableRejectItem r)
    {
        CheckUnoccupied(r);
        var srcDevice=NativeFileCapability.DeviceId(SourceDir);
        if(!Directory.Exists(ArchiveRoot))
        {
            Directory.CreateDirectory(ArchiveRoot);
            if(SortingWorkspace.HasLinkedExistingAncestor(ArchiveRoot))
                throw new IOException("Reject recovery root gained a link.");
            using var mark=new FileStream(RootMarker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var bytes=System.Text.Encoding.UTF8.GetBytes(MarkerText);
            mark.Write(bytes);mark.Flush(true);
        }
        if(NativeFileCapability.DeviceId(ArchiveRoot)!=srcDevice)
            throw new IOException("Reject recovery root must be on source filesystem.");
        var operation=Path.GetDirectoryName(r.Archive)!;
        Directory.CreateDirectory(operation);
        if(SortingWorkspace.HasLinkedExistingAncestor(operation) ||
           NativeFileCapability.DeviceId(operation)!=srcDevice)
            throw new IOException("Reject operation archive is linked or on a different volume.");
        var opMarker=Path.Combine(operation,".photosift-reject-operation-v1");
        using(var fs=new FileStream(opMarker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            var bytes=System.Text.Encoding.UTF8.GetBytes("PhotoSift Reborn Reject operation "+r.Id.ToString("N")+"\n");
            fs.Write(bytes);fs.Flush(true);
        }
    }
    private void VerifyOwnedArchive(DurableRejectItem r)
    {
        CheckPaths(r);
        if(!File.Exists(RootMarker) || SortingWorkspace.HasSymlinkOrReparsePoint(RootMarker) ||
           File.ReadAllText(RootMarker)!=MarkerText ||
           NativeFileCapability.DeviceId(ArchiveRoot)!=NativeFileCapability.DeviceId(SourceDir))
            throw new IOException("Reject recovery ownership marker or filesystem differs.");
        var marker=Path.Combine(Path.GetDirectoryName(r.Archive)!,".photosift-reject-operation-v1");
        if(!File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
           File.ReadAllText(marker)!="PhotoSift Reborn Reject operation "+r.Id.ToString("N")+"\n")
            throw new IOException("Reject operation archive ownership marker changed.");
        Prove(r.Archive,r,false);
    }
    // Return the durable checkpoint authority: RestoreSession intentionally adds
    // exactly one in-memory revision on startup to revoke stale Copy approval.
    // This increment is NOT a saved change and must never be put into a Reject
    // journal record; future approval requires the actual persisted revision.
    private WorkspaceSessionSnapshot CheckWorkspace(SortingWorkspace work,Guid photoId)
    {
        if(_ordinary) RequireOrdinaryWorkspace(_db,work,SourceDir);
        else P5CGuiFixturePolicy.VerifyExactWorkspace(_db,work);
        var saved=_db.Load() ?? throw new IOException("Reject requires a durable saved session.");
        var live=work.CaptureSession();
        bool sameRevision=live.Revision==saved.Revision;
        bool restartOnlyRevision=saved.Revision<long.MaxValue-1 &&
            live.Revision==saved.Revision+1;
        // Require an exact checkpoint image in every other field. In particular,
        // a different cursor, route, photo metadata, mark, assignment or session
        // is not an authorized restarted GUI. Never save/overwrite the durable
        // revision merely to make this comparison pass.
        if((!sameRevision && !restartOnlyRevision) ||
           JsonSerializer.Serialize(live with {Revision=saved.Revision})!=
               JsonSerializer.Serialize(saved) ||
           saved.SessionId!=work.SessionId ||
           saved.PendingRejectIds is null || saved.PendingRejectIds.Length!=1 ||
           saved.PendingRejectIds[0]!=photoId || work.PendingRejectCount!=1 ||
           !work.IsPendingReject(photoId) || work.AssignmentCount!=0 ||
           work.HasAppliedCopies || saved.Assignments.Length!=0 ||
           saved.Photos.SingleOrDefault(x=>x.Id==photoId) is not { } recorded ||
           work.Photos.SingleOrDefault(x=>x.Id==photoId) is not { } photo ||
           !SortingWorkspace.PathComparer.Equals(recorded.Path,photo.Path) ||
           recorded.NativeIdentity!=photo.NativeIdentity ||
           recorded.Length!=photo.Length || recorded.LastWriteUtcTicks!=photo.LastWriteUtcTicks)
            throw new IOException("Reject-review mark/session changed or was not durably saved.");
        return saved;
    }
    public DurableRejectItem Stage(SortingWorkspace work,Guid photoId)
    {
        _db.RequireNoR30Writer();
        var checkpoint=CheckWorkspace(work,photoId);
        if(_db.HasUnreconciledCopy || _db.AllRejectHistoryCount()!=0 ||
           !_db.AssessFreshMoveEpoch().IsFresh)
            throw new IOException("Existing file-operation history prevents a new Reject recovery.");
        var photo=work.Photos.Single(p=>p.Id==photoId);
        CheckRegular(photo.Path);
        if(photo.NativeIdentity is null || NativeFileCapability.Identity(photo.Path)!=photo.NativeIdentity ||
           new FileInfo(photo.Path).Length!=photo.Length ||
           new FileInfo(photo.Path).LastWriteTimeUtc.Ticks!=photo.LastWriteUtcTicks)
            throw new IOException("Original photo identity/metadata changed before Reject review.");
        var id=Guid.NewGuid();var install=_db.MoveInstallationId();
        var archive=Path.Combine(ArchiveRoot,install.ToString("N"),work.SessionId.ToString("N"),
            id.ToString("N"),Path.GetFileName(photo.Path));
        var row=new DurableRejectItem(id,install,work.SessionId,photo.Id,checkpoint.Revision,
            photo.Path,archive,Hash(photo.Path),photo.NativeIdentity,photo.Length,photo.LastWriteUtcTicks,"Staged");
        CheckUnoccupied(row);_db.PutRejectItem(row);
        return row;
    }
    /// <summary>Inspect terminal Undo native evidence independently of the
    /// current session checkpoint, which can later belong to a distinct photo.
    /// The original inode/hash and retained owned-operation marker are binding.
    /// </summary>
    public bool ProveTerminalUndone(DurableRejectItem r,Guid installation,
        bool historicalOnly=false)
    {
        try
        {
            CheckPaths(r,installation,historicalOnly);
            if(r.State!="Undone" || !Empty(r.Archive) ||
               NativeFileCapability.DeviceId(ArchiveRoot)!=NativeFileCapability.DeviceId(SourceDir) ||
               !File.Exists(RootMarker) || SortingWorkspace.HasSymlinkOrReparsePoint(RootMarker) ||
               File.ReadAllText(RootMarker)!="PhotoSift Reborn Reject v1:"+installation.ToString("N")+"\n") return false;
            var marker=Path.Combine(Path.GetDirectoryName(r.Archive)!,".photosift-reject-operation-v1");
            if(!File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
               File.ReadAllText(marker)!="PhotoSift Reborn Reject operation "+r.Id.ToString("N")+"\n") return false;
            Prove(r.Source,r,false);
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or InvalidOperationException) {return false;}
    }

    /// <summary>Explicit independent successor staging after native sealing.
    /// This does not grant Execute; Approve and Execute remain separate consent.
    /// Fixture-only; no real photo or mixed-journal authority.
    /// </summary>
    public DurableRejectItem StageSuccessor(SortingWorkspace work,Guid photoId,bool acknowledgeNewEpoch)
    {
        _db.RequireNoR30Writer();
        if(!acknowledgeNewEpoch) throw new IOException("Explicit new Reject epoch acknowledgement required.");
        var checkpoint=CheckWorkspace(work,photoId);
        if(_db.HasUnreconciledCopy || !_db.AssessFreshMoveEpoch().IsFresh ||
           _db.AllRejectHistoryCount()==0)
            throw new IOException("Reject successor needs prior terminal history and no foreign journals.");
        var photo=work.Photos.Single(p=>p.Id==photoId);
        CheckRegular(photo.Path);
        if(photo.NativeIdentity is null || NativeFileCapability.Identity(photo.Path)!=photo.NativeIdentity ||
           new FileInfo(photo.Path).Length!=photo.Length ||
           new FileInfo(photo.Path).LastWriteTimeUtc.Ticks!=photo.LastWriteUtcTicks)
            throw new IOException("Next Reject original identity/metadata changed.");
        var id=Guid.NewGuid();var install=_db.MoveInstallationId();
        var archive=Path.Combine(ArchiveRoot,install.ToString("N"),work.SessionId.ToString("N"),
            id.ToString("N"),Path.GetFileName(photo.Path));
        var row=new DurableRejectItem(id,install,work.SessionId,photo.Id,checkpoint.Revision,
            photo.Path,archive,Hash(photo.Path),photo.NativeIdentity,photo.Length,photo.LastWriteUtcTicks,"Staged");
        CheckUnoccupied(row);
        _db.GrantNextRejectStage(row,explicitAcknowledgement:true,
            ordinarySourceRoot:_ordinary ? SourceDir : null);
        _db.PutRejectItem(row); // consumes single-use staged grant atomically
        return row;
    }

    private void RequireSnapshotStillBound(DurableRejectItem r)
    {
        if(_ordinary) RequireOrdinarySavedScope();
        else if(!P5CGuiFixturePolicy.IsMarkedStore(_db)) throw new IOException("Reject fixture authorization lost.");
        var snap=_db.Load() ?? throw new IOException("Reject saved session missing.");
        if(snap.SessionId!=r.SessionId || snap.Revision!=r.SessionRevision ||
           snap.PendingRejectIds is null || snap.PendingRejectIds.Length!=1 ||
           snap.PendingRejectIds[0]!=r.PhotoId || snap.Assignments.Length!=0 ||
           snap.Photos.SingleOrDefault(x=>x.Id==r.PhotoId) is not { } photo ||
           !SortingWorkspace.PathComparer.Equals(photo.Path,r.Source) ||
           photo.Length!=r.SourceLength || photo.LastWriteUtcTicks!=r.SourceWriteTicks ||
           photo.NativeIdentity!=r.SourceIdentity)
            throw new IOException("Saved Reject review, photograph or revision changed after approval.");
    }
    private bool VerifiedRejectPredecessors(int requiredPrefix) =>
        (_ordinary ? _db.InspectOrdinaryRejectTerminalReceipts(requiredPrefix)
                   : _db.InspectRejectTerminalReceipts(requiredPrefix)).Verified;

    private void RequireExactLatest(DurableRejectItem r,string state)
    {
        var all=_db.RejectItems();
        if(all.Count==0 || all[^1]!=r || r.State!=state ||
            (all.Count>1 && !VerifiedRejectPredecessors(all.Count-1)))
            throw new IOException("Reject journal/approval changed; no replay or replacement.");
        CheckPaths(r);RequireSnapshotStillBound(r);
    }
    public DurableRejectItem Approve(DurableRejectItem staged,bool explicitApproval)
    {
        if(!explicitApproval) throw new IOException("Reject requires independent explicit exact-path approval.");
        RequireExactLatest(staged,"Staged");Prove(staged.Source,staged,true);CheckUnoccupied(staged);
        var approved=staged with {State="Approved"}; _db.PutRejectItem(approved);return approved;
    }
    public DurableRejectItem Execute(DurableRejectItem approved)
    {
        _db.RequireNoR30Writer();
        RequireExactLatest(approved,"Approved");Prove(approved.Source,approved,true);
        CheckUnoccupied(approved);
        if(NativeFileCapability.DeviceId(approved.Source)!=NativeFileCapability.DeviceId(SourceDir))
            throw new IOException("Reject source is no longer on verified source filesystem.");
        var pending=approved with {State="Intent"};_db.PutRejectItem(pending);
        Cut("REJECT_INTENT_RECORDED");
        EnsureOwnedDirectories(pending);
        Prove(pending.Source,pending,true);
        if(!Empty(pending.Archive)) throw new IOException("Reject archive became occupied.");
        pending=pending with {State="RelocateIntent"};_db.PutRejectItem(pending);
        Cut("REJECT_RELOCATION_INTENT");
        NativeFileCapability.RelocateNoReplace(pending.Source,pending.Archive);
        Cut("REJECT_ORIGINAL_RELOCATED_UNRECORDED");
        if(!Empty(pending.Source)) throw new IOException("Original path unexpectedly reappeared after Reject relocation.");
        VerifyOwnedArchive(pending);
        pending=pending with {State="Committed"};_db.PutRejectItem(pending);
        Cut("REJECT_COMMITTED");return pending;
    }
    public DurableRejectItem Undo(Guid session)
    {
        _db.RequireNoR30Writer();
        var rows=_db.RejectItems();
        if(rows.Count==0 || rows[^1].SessionId!=session || rows[^1].State!="Committed" ||
           (rows.Count>1 && !VerifiedRejectPredecessors(rows.Count-1)))
            throw new IOException("Only the latest fully committed owned Reject can be undone with sealed predecessors.");
        var row=rows[^1];RequireSnapshotStillBound(row);
        VerifyOwnedArchive(row);
        if(!Empty(row.Source)) throw new IOException("Reject restore source path occupied; no overwrite.");
        row=row with {State="UndoIntent"};_db.PutRejectItem(row);
        Cut("REJECT_UNDO_INTENT");
        NativeFileCapability.RelocateNoReplace(row.Archive,row.Source);
        Cut("REJECT_UNDO_SOURCE_RESTORED_UNRECORDED");
        if(!Empty(row.Archive)) throw new IOException("Reject archived path unexpectedly occupied after restore.");
        Prove(row.Source,row,false);
        row=row with {State="Undone"};_db.PutRejectItem(row);
        Cut("REJECT_UNDONE");return row;
    }
    public RejectRecoveryObservation Inspect()
    {
        var rows=_db.RejectItems();
        if(rows.Count==0) return new RejectRecoveryObservation(Guid.Empty,"None","","","NO_REJECT_HISTORY");
        if(rows.Count>1 && !VerifiedRejectPredecessors(rows.Count-1))
            throw new InvalidDataException("Earlier Reject receipt/evidence changed; manual inspection required.");
        var row=rows[^1];
        string evidence;
        try
        {
            evidence=row.State switch
            {
                "Committed" when Empty(row.Source) => ArchiveProven(row)?"OWNED_ARCHIVE_VERIFIED":"ARCHIVE_CHANGED_OR_UNOWNED",
                "Undone" when Empty(row.Archive) => SourceProven(row)?"RESTORED_SOURCE_VERIFIED":"RESTORED_SOURCE_CHANGED",
                "Staged" or "Approved" when Empty(row.Archive) => SourceProven(row)?"UNTOUCHED_SOURCE_VERIFIED":"ORIGINAL_CHANGED",
                _=>"UNRESOLVED_PRESERVE_ALL_NO_REPLAY"
            };
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { evidence="NATIVE_EVIDENCE_UNAVAILABLE_PRESERVE_ALL"; }
        return new RejectRecoveryObservation(row.Id,row.State,row.Source,row.Archive,evidence);
    }
    private bool ArchiveProven(DurableRejectItem r)
    { try {VerifyOwnedArchive(r);return true;}catch(IOException){return false;} }
    private bool SourceProven(DurableRejectItem r)
    { try {Prove(r.Source,r,false);return true;}catch(IOException){return false;} }
}
