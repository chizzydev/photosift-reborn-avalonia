using System.Security.Cryptography;
using System.Text;

namespace PhotoSiftReborn;

public sealed record RenameProposal(Guid Id,Guid SessionId,Guid PhotoId,long Revision,
    string Source,string Target,string Hash,string NativeIdentity,long Length,long WriteTicks,string? ReviewedDirectoryIdentity=null);
public sealed record RenameObservation(Guid Id,string State,string Source,string Target,string Evidence,string? Error);

/// <summary>Same-directory, extension-preserving native Rename; multiple DISTINCT
/// generated fixture photos may be renamed, and verified Undo follows last-active-first.
/// NEVER authorizes personal-photo mutations. Synthetic cuts do not prove power loss.</summary>
public sealed class DurableRenameCoordinator
{
    private readonly WorkspaceSessionStore _db;
    private readonly Action<string>? _cut;
    // P6B-3C: privileged, internal self-test only. No user/GUI entry point.
    private readonly string? _ordinaryTrialRoot;
    private readonly bool _windowsProductionTrial;
    private readonly bool _ordinaryFirstEpoch;
    public DurableRenameCoordinator(WorkspaceSessionStore db,Action<string>? cut=null)
    {
        if (!P5CGuiFixturePolicy.IsMarkedStore(db))
            throw new IOException("Rename can execute only in a generated disposable fixture.");
        _db=db;_cut=cut;
    }
    private DurableRenameCoordinator(WorkspaceSessionStore db,string trialRoot,Action<string>? cut)
    {
        VerifyOrdinaryTrialRoot(db,trialRoot);
        _db=db;_cut=cut;_ordinaryTrialRoot=Path.GetFullPath(trialRoot);
    }
    private DurableRenameCoordinator(WorkspaceSessionStore db,bool windowsTrial,Action<string>? cut)
    {
        if(!windowsTrial) throw new IOException("Invalid native Rename authority.");
        WindowsLocalRenamePolicy.CheckStore(db);
        _db=db;_cut=cut;_windowsProductionTrial=true;
    }
    private DurableRenameCoordinator(WorkspaceSessionStore db,int ordinaryEpoch,Action<string>? cut)
    {
        if(ordinaryEpoch!=1 || !NativeFileCapability.IsSupported ||
            P5CGuiFixturePolicy.IsMarkedStore(db))
            throw new IOException("Ordinary first Rename executor requires native capability and a non-fixture store.");
        _db=db;_cut=cut;_ordinaryFirstEpoch=true;
    }
    public static DurableRenameCoordinator ForOrdinaryFirstEpoch(
        WorkspaceSessionStore db,Action<string>? cut=null)
        =>new(db,1,cut);

    public static DurableRenameCoordinator ForWindowsLocalTrial(
        WorkspaceSessionStore db,Action<string>? cut=null)
        =>new(db,true,cut);

    /// <summary>
    /// INTERNAL native-journal test lane, unreachable from normal GUI or public API.
    /// Exercises the actual durable coordinator on generated ordinary directories,
    /// without requiring the historical P5C marker. Never use real personal photos.
    /// </summary>
    internal static DurableRenameCoordinator ForUnmarkedDisposableTrial(
        WorkspaceSessionStore db,string trialRoot,Action<string>? cut=null)
        =>new(db,trialRoot,cut);

    private static void VerifyOrdinaryTrialRoot(WorkspaceSessionStore db,string root)
    {
        // Restrict this temporary test exception to an immediate child of the
        // operating system's temp folder, named with a fresh 32-character hex ID.
        var full=Path.GetFullPath(root);
        var name=Path.GetFileName(full);
        const string prefix="P6B3C_ORDINARY_";
        if(!name.StartsWith(prefix,StringComparison.Ordinal) ||
           name.Length!=prefix.Length+32 ||
           !name[prefix.Length..].All(Uri.IsHexDigit) ||
           !SortingWorkspace.PathComparer.Equals(
               Directory.GetParent(full)?.FullName,Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)) ||
           !SortingWorkspace.PathComparer.Equals(
               Path.GetDirectoryName(db.DatabasePath),Path.Combine(full,"state")) ||
           !Directory.Exists(Path.Combine(full,"source")) ||
           !Directory.Exists(Path.Combine(full,"output")) ||
           File.Exists(Path.Combine(full,"P5C_GUI_TEST_ONLY.marker")) ||
           File.Exists(Path.Combine(full,"P3B2_TEST_ONLY.marker")) ||
           SortingWorkspace.HasLinkedExistingAncestor(full) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.Combine(full,"source")) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.Combine(full,"output")))
            throw new IOException("Unmarked Rename trial limited to new, local, disposable test directories under OS temp.");
    }
    private void VerifyWorkspace(SortingWorkspace work)
    {
        if(_ordinaryFirstEpoch)
        {
            if(!OrdinaryFirstRenamePolicy.CanUseWorkspace(_db,work))
                throw new IOException("Ordinary Rename workspace scope or device is unsafe.");
            return;
        }
        if(_windowsProductionTrial)
        {
            WindowsLocalRenamePolicy.CheckWorkspace(_db,work);
            return;
        }
        if(_ordinaryTrialRoot is null)
        {
            P5CGuiFixturePolicy.VerifyExactWorkspace(_db,work);
            return;
        }
        VerifyOrdinaryTrialRoot(_db,_ordinaryTrialRoot);
        var source=Path.Combine(_ordinaryTrialRoot,"source");
        if(!SortingWorkspace.PathComparer.Equals(work.SourceRoot,source) ||
           !SortingWorkspace.PathComparer.Equals(work.DestinationRoot,Path.Combine(_ordinaryTrialRoot,"output")) ||
           work.SourceRoots.Count!=1 ||
           work.Photos.Any(photo=>!SortingWorkspace.PathComparer.Equals(
               Path.GetDirectoryName(photo.Path),source)))
            throw new IOException("Unmarked Rename trial workspace differs from isolated generated paths.");
    }
    private bool IsAuthorizedForUndo()
    {
        if(_ordinaryFirstEpoch)
        {
            return NativeFileCapability.IsSupported && !P5CGuiFixturePolicy.IsMarkedStore(_db);
        }
        if(_windowsProductionTrial)
        {
            WindowsLocalRenamePolicy.CheckStore(_db);
            return true;
        }
        if(_ordinaryTrialRoot is null) return P5CGuiFixturePolicy.IsMarkedStore(_db);
        VerifyOrdinaryTrialRoot(_db,_ordinaryTrialRoot);
        return true;
    }
    private void Cut(string name)=>_cut?.Invoke(name);
    private static bool Free(string path)=>!File.Exists(path) && !Directory.Exists(path);

    public static string ValidateFilename(string source,string proposed)
    {
        if(string.IsNullOrWhiteSpace(proposed) || proposed.Length>180 || proposed!=proposed.Trim() ||
           proposed.EndsWith('.') || proposed.EndsWith(' ') ||
           proposed is "." or ".." || proposed.Any(char.IsControl) ||
           proposed.IndexOfAny(new[]{'<','>',':','"','/','\\','|','?','*'})>=0 ||
           Path.GetFileName(proposed)!=proposed)
            throw new IOException("Rename requires one simple file name; no paths, reserved characters or trailing spaces/dots.");
        // Same conservative policy on Windows and Linux, with no ambiguous media extension changes.
        var stem=Path.GetFileNameWithoutExtension(proposed);
        if (string.IsNullOrWhiteSpace(stem) || stem.EndsWith(' ') || stem.EndsWith('.') ||
            new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                  "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}
                .Contains(stem,StringComparer.OrdinalIgnoreCase))
            throw new IOException("Reserved or ambiguous filename.");
        var ext=Path.GetExtension(source);
        if (!string.Equals(Path.GetExtension(proposed),ext,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Rename must preserve the original image file extension.");
        if (string.Equals(Path.GetFileName(source),proposed,StringComparison.OrdinalIgnoreCase))
            throw new IOException("No-op or case-only rename is not supported in this release.");
        return proposed;
    }
    private static bool Matches(string path,RenameProposal p)
    {
        try {
            if (!File.Exists(path) || SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(path)!)) return false;
            var f=new FileInfo(path);
            if(f.Length!=p.Length || f.LastWriteTimeUtc.Ticks!=p.WriteTicks ||
               NativeFileCapability.Identity(path)!=p.NativeIdentity) return false;
            using var stream=File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream))==p.Hash;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {return false;}
    }
    private static RenameProposal FromRow(DurableRenameItem row)
        =>new(row.Id,row.SessionId,row.PhotoId,row.Revision,row.Source,row.Target,
            row.Hash,row.NativeIdentity,row.Length,row.WriteTicks,row.ReviewedDirectoryIdentity);

    // P6B3B/3C: explicitly opted-in anchored Rename, scoped to generated test files.
    // Unlike the original Prepare(), this freezes native directory identity
    // in the durable proposal. The caller cannot turn it on for personal files:
    // the public constructor and normal GUI fixture policy remain unchanged.
    public RenameProposal PrepareOwnedFixture(SortingWorkspace work,Photo photo,string filename)
    {
        var ordinary=Prepare(work,photo,filename);
        var directory=Path.GetDirectoryName(ordinary.Source)!;
        var directoryId=NativeFileCapability.DirectoryIdentity(directory);
        if(string.IsNullOrWhiteSpace(directoryId))
            throw new IOException("Native directory identity unavailable; anchored Rename refused.");
        return ordinary with {ReviewedDirectoryIdentity=directoryId};
    }

    // P6B-3D: a narrow, dependency-checked *repeat after completed Undo* lane.
    // Retain the original strict behavior in the real GUI/marked-fixture constructor.
    // Only our internal unmarked disposable native-journal trial permits reuse.
    private IReadOnlyList<DurableRenameItem> VerifiedHistory(Guid session,Guid? pending=null)
    {
        var history=_db.RenameItems(session);
        if(history.Count!=_db.AllRenameHistoryCount())
            throw new IOException("Rename history from another session; refuse mutation.");
        if(_db.HasUnreconciledCopy || _db.AllMoveHistoryCount()!=0 ||
           _db.CopyItems(session).Count!=0)
            throw new IOException("Mixed Move/Copy/Rename recovery history; refuse mutation.");
        var terminal=history.Where(r=>r.Id!=pending).ToArray();
        foreach(var row in terminal)
            if(row.State is not ("Committed" or "Undone"))
                throw new IOException("Unresolved earlier Rename; inspect the durable journal.");
        foreach(var group in terminal.GroupBy(r=>r.PhotoId))
        {
            var rows=group.OrderBy(r=>r.Sequence).ThenBy(r=>r.Id).ToArray();
            for(int i=1;i<rows.Length;i++)
            {
                var old=rows[i-1];var next=rows[i];
                // A repeated Rename is authorized only after full previous Undo.
                // The row chain must be byte-/inode-identical and explicitly linked.
                if((_ordinaryTrialRoot is null && !_windowsProductionTrial) || old.State!="Undone" ||
                   next.SupersedesRenameId!=old.Id ||
                   !SortingWorkspace.PathComparer.Equals(old.Source,next.Source) ||
                   old.NativeIdentity!=next.NativeIdentity || old.Hash!=next.Hash ||
                   old.Length!=next.Length || old.WriteTicks!=next.WriteTicks ||
                   old.ReviewedDirectoryIdentity is null ||
                   old.ReviewedDirectoryIdentity!=next.ReviewedDirectoryIdentity)
                    throw new IOException("Renamed-photo chain has an unsupported or changed dependency.");
            }
            // Earlier terminal records are immutable historical evidence, not
            // additional live directory entries. Prove only the current tip.
            if(!ProveState(rows[^1]))
                throw new IOException("Latest Rename ownership cannot be proved.");
        }
        return terminal;
    }
    private void VerifyPriorHistory(SortingWorkspace work,Guid? pending=null)
    {
        var terminal=VerifiedHistory(work.SessionId,pending);
        foreach(var last in terminal.GroupBy(r=>r.PhotoId).Select(g=>g.Last()))
        {
            var expectedPath=last.State=="Committed"?last.Target:last.Source;
            var saved=work.Photos.SingleOrDefault(p=>p.Id==last.PhotoId);
            if(saved is null || !SortingWorkspace.PathComparer.Equals(saved.Path,expectedPath))
                throw new IOException("Workspace checkpoint diverges from latest verified Rename history.");
        }
    }
    private Guid? RejectDependentOperation(SortingWorkspace work,Guid photoId,string source,string target)
    {
        var history=_db.RenameItems(work.SessionId);
        var same=history.Where(r=>r.PhotoId==photoId).ToArray();
        Guid? previous=null;
        if(same.Length!=0)
        {
            var last=same[^1];
            if((_ordinaryTrialRoot is null && !_windowsProductionTrial) || last.State!="Undone" ||
               same.Any(r=>r.State!="Undone" || r.ReviewedDirectoryIdentity is null) ||
               !SortingWorkspace.PathComparer.Equals(last.Source,source) ||
               NativeFileCapability.Identity(source)!=last.NativeIdentity)
                throw new IOException("Earlier Rename must be completely undone and owned before reuse.");
            previous=last.Id;
        }
        foreach(var prior in history)
        {
            // Only the SAME verified photograph may reuse its historical path.
            // A different photograph may never take any prior name, including
            // names from fully undone records (keeps the journal unambiguous).
            if(prior.PhotoId==photoId) continue;
            if(prior.NativeIdentity==NativeFileCapability.Identity(source) ||
               new[]{prior.Source,prior.Target}.Any(a=>
                   string.Equals(a.Normalize(NormalizationForm.FormC),source.Normalize(NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a.Normalize(NormalizationForm.FormC),target.Normalize(NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Rename path or native identity belongs to another historical photograph.");
        }
        return previous;
    }

    public RenameProposal Prepare(SortingWorkspace work,Photo photo,string filename)
    {
        VerifyWorkspace(work);
        VerifyPriorHistory(work);
        if (work.Current?.Id!=photo.Id || work.AssignmentCount!=0 ||
            work.RemovedCount!=0 || work.HasAppliedCopies)
            throw new IOException("Rename needs one unassigned current fixture photograph and no file-operation/removal history.");
        if (photo.NativeIdentity is null || photo.Id==Guid.Empty)
            throw new IOException("Source lacks recorded native identity.");
        if(_ordinaryFirstEpoch) OrdinaryFirstRenamePolicy.RequireFresh(_db,work,photo,filename);
        if(_windowsProductionTrial) WindowsLocalRenamePolicy.CheckPhoto(_db,work,photo);
        var root=Directory.GetParent(Path.GetDirectoryName(_db.DatabasePath)!)!.FullName;
        if(((_windowsProductionTrial || _ordinaryFirstEpoch) && !SortingWorkspace.PathComparer.Equals(
                Path.GetDirectoryName(photo.Path),work.SourceRoot!)) ||
            (!_windowsProductionTrial && !_ordinaryFirstEpoch && !SortingWorkspace.PathComparer.Equals(
                Path.GetDirectoryName(photo.Path),Path.Combine(root,"source"))))
            throw new IOException("Rename cannot escape fixture source folder.");
        var cleanName=ValidateFilename(photo.Path,filename);
        var sourceFolder=Path.GetDirectoryName(photo.Path)!;
        // Refuse cross-platform case/Unicode aliases even when the current Linux
        // filesystem would permit them. They could collide after sharing with Windows.
        if (Directory.EnumerateFileSystemEntries(sourceFolder).Any(path=>
            string.Equals(Path.GetFileName(path).Normalize(NormalizationForm.FormC),
                cleanName.Normalize(NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Target aliases an existing file or directory (portable case/Unicode rules).");
        var target=Path.Combine(sourceFolder,cleanName);
        RejectDependentOperation(work,photo.Id,photo.Path,target);
        if (!Free(target) || NativeFileCapability.DeviceId(photo.Path)!=NativeFileCapability.DeviceId(Path.GetDirectoryName(target)!))
            throw new IOException("Target occupied or different filesystem. Rename refused.");
        if (SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(target)!) ||
            SortingWorkspace.HasSymlinkOrReparsePoint(photo.Path))
            throw new IOException("Linked source/destination is not supported.");
        var info=new FileInfo(photo.Path);
        if (info.Length!=photo.Length || info.LastWriteTimeUtc.Ticks!=photo.LastWriteUtcTicks ||
            NativeFileCapability.Identity(photo.Path)!=photo.NativeIdentity)
            throw new IOException("Photograph changed since import.");
        using var stream=File.OpenRead(photo.Path);
        var hash=Convert.ToHexString(SHA256.HashData(stream));
        return new RenameProposal(Guid.NewGuid(),work.SessionId,photo.Id,work.Revision,
            photo.Path,target,hash,photo.NativeIdentity,photo.Length,photo.LastWriteUtcTicks);
    }
    private void Recheck(SortingWorkspace work,RenameProposal p)
    {
        VerifyWorkspace(work);
        VerifyPriorHistory(work,p.Id);
        if(work.SessionId!=p.SessionId || work.Revision!=p.Revision ||
           work.Current?.Id!=p.PhotoId || !SortingWorkspace.PathComparer.Equals(work.Current.Path,p.Source) ||
           work.AssignmentCount!=0 || _db.AllMoveHistoryCount()!=0 ||
           _db.CopyItems(p.SessionId).Count!=0 || _db.HasUnreconciledCopy)
            throw new IOException("Rename selection/session/revision changed since Review.");
        if(_ordinaryFirstEpoch)
            OrdinaryFirstRenamePolicy.RequireFresh(_db,work,work.Current!,
                Path.GetFileName(p.Target),p.Id);
        if(_windowsProductionTrial)
        {
            if(p.ReviewedDirectoryIdentity is null)
                throw new IOException("Windows local Rename requires pinned native directory identity.");
            WindowsLocalRenamePolicy.CheckPhoto(_db,work,work.Current!);
        }
        ValidateFilename(p.Source,Path.GetFileName(p.Target));
        if (!Matches(p.Source,p) || !Free(p.Target))
            throw new IOException("Source identity changed or target occupied since Review.");
        if(p.ReviewedDirectoryIdentity is not null &&
           NativeFileCapability.DirectoryIdentity(Path.GetDirectoryName(p.Source)!)!=p.ReviewedDirectoryIdentity)
            throw new IOException("Reviewed parent-directory identity changed; refuse anchored Rename.");
    }
    public DurableRenameItem Apply(SortingWorkspace work,RenameProposal p)
    {
        _db.RequireNoR30Writer();
        if((_ordinaryTrialRoot is not null || _windowsProductionTrial) && p.ReviewedDirectoryIdentity is null)
            throw new IOException("Anchored trial Rename requires reviewed directory identity.");
        if(_ordinaryFirstEpoch && p.ReviewedDirectoryIdentity is null)
            throw new IOException("Anchored Rename requires a reviewed native directory identity.");
        Recheck(work,p);
        var supersedes=RejectDependentOperation(work,p.PhotoId,p.Source,p.Target);
        var previous=_db.RenameItems(p.SessionId);
        var sequence=Math.Max(DateTime.UtcNow.Ticks,previous.Count==0?1:previous.Max(r=>r.Sequence)+1);
        var row=new DurableRenameItem(p.Id,_db.MoveInstallationId(),p.SessionId,p.PhotoId,p.Revision,
            sequence,p.Source,p.Target,p.Hash,p.NativeIdentity,p.Length,p.WriteTicks,"Intent",null,
            p.ReviewedDirectoryIdentity,supersedes);
        _db.PutRenameItem(row);
        Cut("RENAME_INTENT_DURABLE");
        try
        {
            // No additional directory/file creation required. No cross-volume fallback.
            Recheck(work,p);
            if(p.ReviewedDirectoryIdentity is null)
                NativeFileCapability.RelocateNoReplace(p.Source,p.Target);
            else
                NativeFileCapability.RelocateNoReplaceOwned(p.Source,p.Target,
                    p.NativeIdentity,p.Hash,p.Length,p.WriteTicks,p.ReviewedDirectoryIdentity);
            Cut("RENAME_RELOCATED_UNJOURNALLED");
            if (!Free(p.Source) || !Matches(p.Target,p))
                throw new IOException("Renamed file cannot be proved owned; preserve journal and both paths.");
            row=row with {State="Committed"};_db.PutRenameItem(row);
            Cut("RENAME_COMMITTED");
            return row;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            try {_db.PutRenameItem(row with {State="NeedsAttention",Error=ex.Message});} catch { }
            throw;
        }
    }
    public DurableRenameItem Undo(Guid session)
    {
        _db.RequireNoR30Writer();
        var list=_db.RenameItems(session);
        if (list.Count==0 || list.Count!=_db.AllRenameHistoryCount() ||
            _db.AllMoveHistoryCount()!=0 || _db.CopyItems(session).Count!=0 ||
            _db.HasUnreconciledCopy || !IsAuthorizedForUndo())
            throw new IOException("Rename Undo requires a single fixture-owned, unmixed session.");
        if(_ordinaryFirstEpoch) OrdinaryFirstRenamePolicy.RequireUndo(_db,session);
        VerifiedHistory(session);
        // A fully undone previous generation remains in SQLite but is no longer
        // expected at its old pathname while a later generation is committed.
        var row=list.LastOrDefault(x=>x.State=="Committed")
            ?? throw new IOException("No committed Rename available for Undo.");
        var p=FromRow(row);
        if((_ordinaryTrialRoot is not null || _windowsProductionTrial) && p.ReviewedDirectoryIdentity is null)
            throw new IOException("Anchored trial Rename requires reviewed directory identity.");
        if(_ordinaryFirstEpoch && p.ReviewedDirectoryIdentity is null)
            throw new IOException("Anchored Rename refuses Undo of a legacy path-only Rename.");
        if (!Free(p.Source) || !Matches(p.Target,p))
            throw new IOException("Original name reoccupied or renamed file no longer owned: Undo refused.");
        try
        {
            row=row with {State="UndoIntent"};_db.PutRenameItem(row);
            Cut("RENAME_UNDO_INTENT_DURABLE");
            if(p.ReviewedDirectoryIdentity is null)
                NativeFileCapability.RelocateNoReplace(p.Target,p.Source);
            else
                NativeFileCapability.RelocateNoReplaceOwned(p.Target,p.Source,
                    p.NativeIdentity,p.Hash,p.Length,p.WriteTicks,p.ReviewedDirectoryIdentity);
            Cut("RENAME_UNDO_RELOCATED_UNJOURNALLED");
            if (!Free(p.Target) || !Matches(p.Source,p))
                throw new IOException("Restored Rename file is not verified; manual inspection required.");
            row=row with {State="Undone"};_db.PutRenameItem(row);
            Cut("RENAME_UNDONE");
            return row;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            try {_db.PutRenameItem(row with {State="NeedsAttention",Error=ex.Message});} catch { }
            throw;
        }
    }
    public static bool ProveState(DurableRenameItem row)
    {
        var p=FromRow(row);
        if(p.ReviewedDirectoryIdentity is not null)
        {
            try
            {
                if(NativeFileCapability.DirectoryIdentity(Path.GetDirectoryName(p.Source)!)
                    !=p.ReviewedDirectoryIdentity) return false;
            }
            catch (Exception ex) when(ex is IOException or UnauthorizedAccessException) {return false;}
        }
        return row.State switch {
            "Committed"=>Free(p.Source) && Matches(p.Target,p),
            "Undone"=>Free(p.Target) && Matches(p.Source,p), _=>false
        };
    }
    public static IReadOnlyList<RenameObservation> Inspect(WorkspaceSessionStore db,Guid session)
        =>db.RenameItems(session).Select(row=>
        {
            var p=FromRow(row);
            string evidence="SOURCE="+(Matches(row.Source,p)?"OWNED":Free(row.Source)?"ABSENT":"UNKNOWN")+
                "; TARGET="+(Matches(row.Target,p)?"OWNED":Free(row.Target)?"ABSENT":"UNKNOWN");
            return new RenameObservation(row.Id,row.State,row.Source,row.Target,evidence,row.Error);
        }).ToArray();
}
