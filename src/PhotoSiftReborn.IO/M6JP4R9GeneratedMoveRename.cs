using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R9: a deliberately internal, marked-generated-fixture-only THIRD family
/// execution lane. Copy A and Move B must be exactly committed and mutually
/// independently native-proved before Rename C can write an Intent. The
/// generated Move output is renamed IN ITS OWN DIRECTORY using the existing
/// pinned-root, no-replace native Rename primitive. The completed Rename is
/// a dependency: existing Move and Rename Undo entry points remain locked.
/// Not ordinary-photo approval, not generalized epoch authority, and not
/// automatic recovery after an interrupted relocation.
/// </summary>
internal sealed record M6JP4R9GeneratedRenamePlan(Guid Id, Guid SessionId,
    string Source, string Target, string NativeIdentity, string Hash,
    long Length, long WriteTicks, string DirectoryIdentity,
    DurableCopyItem Copy, DurableMoveItem Move);

internal static class M6JP4R9GeneratedMoveRename
{
    private static bool Empty(string path) => !File.Exists(path) && !Directory.Exists(path);

    private static void RequireMarkedFixture(WorkspaceSessionStore db)
    {
        if (!OperatingSystem.IsWindows() || !NativeFileCapability.IsSupported)
            throw new IOException("P4-R9 native third-family operation requires Windows.");
        var state=Path.GetDirectoryName(db.DatabasePath)
            ?? throw new IOException("P4-R9 SQLite state path unavailable.");
        var root=Directory.GetParent(state)?.FullName
            ?? throw new IOException("P4-R9 fixture root missing.");
        string marker=Path.Combine(root,"P3B2_TEST_ONLY.marker");
        if (Path.GetFileName(state)!="state" ||
            !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal) ||
            !File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
            File.ReadAllText(marker)!="synthetic-interruption-only" ||
            SortingWorkspace.HasLinkedExistingAncestor(root) ||
            !Directory.Exists(Path.Combine(root,"source")) ||
            !Directory.Exists(Path.Combine(root,"output")))
            throw new IOException("P4-R9 third-family execution is restricted to generated-only fixtures.");
    }

    private static void RequireOwnedPlan(WorkspaceSessionStore db,M6JP4R9GeneratedRenamePlan plan,
        bool alreadyIntent)
    {
        RequireMarkedFixture(db);
        if (plan.Id==Guid.Empty || plan.SessionId==Guid.Empty || plan.Copy is null ||
            plan.Move is null || plan.Move.Id==Guid.Empty || plan.Move.PhotoId==plan.Copy.PhotoId ||
            plan.Move.State!="Committed" || plan.Move.Error is not null ||
            plan.Move.TargetIdentity is null || plan.Copy.TargetIdentity is null ||
            plan.Move.SessionId!=plan.SessionId || plan.Copy.SessionId!=plan.SessionId ||
            plan.NativeIdentity!=plan.Move.TargetIdentity ||
            plan.Hash!=plan.Move.SourceHash || plan.Length!=plan.Move.SourceLength ||
            plan.Source!=plan.Move.Destination ||
            plan.WriteTicks<=0 ||
            db.AllCopyHistoryCount()!=1 || db.AllMoveHistoryCount()!=1 ||
            db.AllRenameHistoryCount()!=(alreadyIntent?1:0) ||
            db.AllRejectHistoryCount()!=0 || db.HasUnreconciledCopy)
            throw new IOException("P4-R9 generated predecessor and Rename proposal do not match.");
        var savedCopies=db.CopyItems(plan.SessionId);
        var savedMoves=db.MoveItems(plan.SessionId);
        if (savedCopies.Count!=1 || savedMoves.Count!=1 ||
            savedCopies[0]!=plan.Copy || savedMoves[0]!=plan.Move ||
            plan.Copy.State!="Committed" || plan.Copy.Error is not null)
            throw new IOException("P4-R9 predecessor journal rows changed during generated Rename.");
        if (alreadyIntent)
        {
            var renames=db.RenameItems(plan.SessionId);
            if (renames.Count!=1 || renames[0].Id!=plan.Id ||
                renames[0].State!="Intent" || renames[0].Error is not null ||
                renames[0].Source!=plan.Source || renames[0].Target!=plan.Target ||
                renames[0].NativeIdentity!=plan.NativeIdentity ||
                renames[0].ReviewedDirectoryIdentity!=plan.DirectoryIdentity)
                throw new IOException("P4-R9 durable Rename Intent was changed.");
        }
        var root=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        var output=Path.Combine(root,"output");
        string parent=Path.GetDirectoryName(plan.Source)!;
        string targetParent=Path.GetDirectoryName(plan.Target)!;
        if (!SortingWorkspace.PathComparer.Equals(parent,targetParent) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(parent),output) ||
            !Path.IsPathFullyQualified(plan.Target) ||
            !string.Equals(Path.GetFullPath(plan.Target),plan.Target,StringComparison.Ordinal) ||
            !string.Equals(plan.Target.Normalize(System.Text.NormalizationForm.FormC),plan.Target,StringComparison.Ordinal) ||
            SortingWorkspace.HasLinkedExistingAncestor(parent) ||
            NativeFileCapability.DirectoryIdentity(parent)!=plan.DirectoryIdentity ||
            !Empty(plan.Target) ||
            Directory.EnumerateFileSystemEntries(parent).Any(p=>
                string.Equals(Path.GetFileName(p).Normalize(System.Text.NormalizationForm.FormC),
                    Path.GetFileName(plan.Target).Normalize(System.Text.NormalizationForm.FormC),
                    StringComparison.OrdinalIgnoreCase)) ||
            !NativeFileCapability.IsSingleLinkRegularFile(plan.Source) ||
            !NativeFileCapability.IsSingleLinkRegularFile(plan.Move.OriginalArchive) ||
            NativeFileCapability.DeviceId(plan.Source)!=NativeFileCapability.DeviceId(parent) ||
            NativeFileCapability.Identity(plan.Source)!=plan.NativeIdentity ||
            new FileInfo(plan.Source).LastWriteTimeUtc.Ticks!=plan.WriteTicks ||
            DurableRenameCoordinator.ValidateFilename(plan.Source,Path.GetFileName(plan.Target))!=Path.GetFileName(plan.Target))
            throw new IOException("P4-R9 installed Move file, Rename destination or parent is no longer owned.");
        var pin=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
            new(plan.Copy.Source,plan.Copy.SourceIdentity,plan.Copy.SourceHash,
                plan.Copy.SourceLength,plan.Copy.SourceWriteTicks),
            new(plan.Copy.Destination,plan.Copy.TargetIdentity!,plan.Copy.SourceHash,plan.Copy.SourceLength),
            new(plan.Move.OriginalArchive,plan.Move.SourceIdentity,plan.Move.SourceHash,
                plan.Move.SourceLength,plan.Move.SourceWriteTicks),
            new(plan.Source,plan.NativeIdentity,plan.Hash,plan.Length,plan.WriteTicks));
        if (!pin.IsFreshWitness || pin.IsDurableProof || pin.AuthorizesNativeOperation ||
            pin.AuthorizesSelectiveUndo)
            throw new IOException("P4-R9 four-file native Copy/Move predecessor proof refused: "+pin.Explanation);
    }

    internal static M6JP4R9GeneratedRenamePlan Prepare(WorkspaceSessionStore db,
        Guid session,string filename)
    {
        RequireMarkedFixture(db);
        // Existing P4-R7 prerequisite still proves the exact consumed Move
        // review and original Copy journal/history seal. Reuse it rather than
        // bypass the predecessor's original reviewed authorization.
        var move=db.RequireM6JConsumedCopyMoveForSelectiveUndo(session);
        var copies=db.CopyItems(session);
        if (copies.Count!=1 || !NativeFileCapability.IsSingleLinkRegularFile(move.Destination))
            throw new IOException("P4-R9 exact Copy and installed Move artifact unavailable.");
        var clean=DurableRenameCoordinator.ValidateFilename(move.Destination,filename);
        string parent=Path.GetDirectoryName(move.Destination)!;
        var plan=new M6JP4R9GeneratedRenamePlan(Guid.NewGuid(),session,move.Destination,
            Path.Combine(parent,clean),move.TargetIdentity!,move.SourceHash,move.SourceLength,
            new FileInfo(move.Destination).LastWriteTimeUtc.Ticks,
            NativeFileCapability.DirectoryIdentity(parent),copies[0],move);
        RequireOwnedPlan(db,plan,false);
        return plan;
    }

    internal static DurableRenameItem Apply(WorkspaceSessionStore db,
        M6JP4R9GeneratedRenamePlan plan,Action<string>? testCut=null)
    {
        // The generated caller must have retained the exact reviewed plan.
        // No generic Rename API, GUI, ordinary-photo capability or epoch grant
        // is modified. The durable Intent is a one-shot crash boundary.
        RequireOwnedPlan(db,plan,false);
        var row=new DurableRenameItem(plan.Id,db.MoveInstallationId(),plan.SessionId,
            plan.Move.PhotoId,plan.Move.PlanRevision,DateTime.UtcNow.Ticks,
            plan.Source,plan.Target,plan.Hash,plan.NativeIdentity,plan.Length,
            plan.WriteTicks,"Intent",null,plan.DirectoryIdentity);
        db.PutRenameItem(row);
        testCut?.Invoke("P4R9_RENAME_INTENT_DURABLE");
        try
        {
            RequireOwnedPlan(db,plan,true);
            NativeFileCapability.RelocateNoReplaceOwned(plan.Source,plan.Target,
                plan.NativeIdentity,plan.Hash,plan.Length,plan.WriteTicks,
                plan.DirectoryIdentity);
            testCut?.Invoke("P4R9_RENAME_RELOCATED_UNJOURNALLED");
            var committed=row with {State="Committed"};
            if (!DurableRenameCoordinator.ProveState(committed))
                throw new IOException("P4-R9 generated Rename relocated file is no longer owned.");
            db.PutRenameItem(committed);
            if (db.RenameItems(plan.SessionId).Single()!=committed)
                throw new IOException("P4-R9 committed Rename record could not be verified.");
            return committed;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            try {db.PutRenameItem(row with {State="NeedsAttention",Error=ex.Message});} catch { }
            throw;
        }
    }

    // P4-R10 -- a generated-disposable-only selective Undo for exactly ONE
    // committed Rename C whose installed file came from committed Move B
    // and whose separate committed Copy A predecessor remains intact.
    // This does not authorize generic mixed-family Undo or predecessor Move Undo.
    private static void RequireOwnedRenameUndo(
        WorkspaceSessionStore db, DurableRenameItem rename, string expectedState)
    {
        RequireMarkedFixture(db);
        if (rename is null || rename.Id==Guid.Empty || rename.SessionId==Guid.Empty ||
            rename.State!=expectedState || rename.Error is not null ||
            rename.InstallationId!=db.MoveInstallationId() ||
            rename.ReviewedDirectoryIdentity is null ||
            rename.SupersedesRenameId is not null ||
            db.AllRenameHistoryCount()!=1 || db.AllRejectHistoryCount()!=0 ||
            db.RenameItems(rename.SessionId).Count!=1 ||
            db.RenameItems(rename.SessionId)[0]!=rename)
            throw new IOException("P4-R10 exact third-family committed Rename journal missing.");

        // This shared proof retains the P2A consumed-review seal AND checks
        // immutable Copy raw-history bytes. The baseline Move-Undo entry point
        // remains unchanged and still requires ZERO Rename history.
        var move=db.RequireM6JConsumedCopyMoveWithGeneratedRename(
            rename.SessionId,rename,expectedState);
        var copies=db.CopyItems(rename.SessionId);
        if (copies.Count!=1) throw new IOException("P4-R10 Copy predecessor missing.");
        var copy=copies[0];
        var fixture=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        var parent=Path.GetDirectoryName(rename.Target)!;
        if (rename.Source!=move.Destination ||
            !SortingWorkspace.PathComparer.Equals(parent,Path.GetDirectoryName(rename.Source)) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(parent),Path.Combine(fixture,"output")) ||
            !Path.IsPathFullyQualified(rename.Target) ||
            !string.Equals(Path.GetFullPath(rename.Target),rename.Target,StringComparison.Ordinal) ||
            !string.Equals(rename.Target.Normalize(System.Text.NormalizationForm.FormC),
                rename.Target,StringComparison.Ordinal) ||
            SortingWorkspace.HasLinkedExistingAncestor(parent) ||
            NativeFileCapability.DirectoryIdentity(parent)!=rename.ReviewedDirectoryIdentity ||
            !Empty(rename.Source) ||
            DurableRenameCoordinator.ValidateFilename(rename.Source,Path.GetFileName(rename.Target))
                !=Path.GetFileName(rename.Target) ||
            !NativeFileCapability.IsSingleLinkRegularFile(rename.Target) ||
            NativeFileCapability.Identity(rename.Target)!=rename.NativeIdentity ||
            new FileInfo(rename.Target).LastWriteTimeUtc.Ticks!=rename.WriteTicks ||
            NativeFileCapability.DeviceId(rename.Target)!=NativeFileCapability.DeviceId(parent))
            throw new IOException("P4-R10 renamed Move output/parent or original source not owned.");
        // Multiple actual Windows handles are pinned concurrently during this
        // read-only proof. Native RelocateNoReplaceOwned independently pins the
        // source/destination directory again immediately before the OS rename.
        var pin=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
            new(copy.Source,copy.SourceIdentity,copy.SourceHash,
                copy.SourceLength,copy.SourceWriteTicks),
            new(copy.Destination,copy.TargetIdentity!,copy.SourceHash,copy.SourceLength),
            new(move.OriginalArchive,move.SourceIdentity,move.SourceHash,
                move.SourceLength,move.SourceWriteTicks),
            new(rename.Target,rename.NativeIdentity,rename.Hash,rename.Length,rename.WriteTicks));
        if (!pin.IsFreshWitness || pin.IsDurableProof || pin.AuthorizesNativeOperation ||
            pin.AuthorizesSelectiveUndo)
            throw new IOException("P4-R10 four simultaneous native ownership pins refused: "+pin.Explanation);
    }

    // P4-R12 fourth-family preparation reuses the exact accepted R10/R11
    // committed-Rename predecessor proof BEFORE its own Reject journal exists.
    internal static DurableRenameItem AttestCommittedForGeneratedFourthFamily(
        WorkspaceSessionStore db,Guid session)
    {
        var rows=db.RenameItems(session);
        if(rows.Count!=1) throw new IOException("P4-R12 committed Rename is not unique.");
        RequireOwnedRenameUndo(db,rows[0],"Committed");
        return rows[0];
    }

    internal static DurableRenameItem UndoGeneratedThirdFamily(
        WorkspaceSessionStore db, Guid session, Action<string>? testCut=null)
    {
        RequireMarkedFixture(db);
        var rows=db.RenameItems(session);
        if(rows.Count!=1) throw new IOException("P4-R10 requires one exact Rename C.");
        var committed=rows[0];
        RequireOwnedRenameUndo(db,committed,"Committed");
        var pending=committed with {State="UndoIntent"};
        // There is no replay path for an interrupted UndoIntent; a test cut
        // here leaves an observable journal requiring manual inspection.
        db.PutRenameItem(pending);
        testCut?.Invoke("P4R10_RENAME_UNDO_INTENT_DURABLE");
        try
        {
            RequireOwnedRenameUndo(db,pending,"UndoIntent");
            NativeFileCapability.RelocateNoReplaceOwned(
                pending.Target,pending.Source,pending.NativeIdentity,pending.Hash,
                pending.Length,pending.WriteTicks,pending.ReviewedDirectoryIdentity!);
            testCut?.Invoke("P4R10_RENAME_UNDO_RELOCATED_UNJOURNALLED");
            var undone=committed with {State="Undone"};
            if(!DurableRenameCoordinator.ProveState(undone))
                throw new IOException("P4-R10 restored native Rename original cannot be proved owned.");
            db.PutRenameItem(undone);
            if(db.RenameItems(session).Count!=1 || db.RenameItems(session)[0]!=undone ||
               db.MoveItems(session).Count!=1 || db.MoveItems(session)[0].State!="Committed" ||
               db.CopyItems(session).Count!=1 || db.CopyItems(session)[0].State!="Committed")
                throw new IOException("P4-R10 terminal Rename Undo or predecessor journal inconsistent.");
            return undone;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            try {db.PutRenameItem(pending with {State="NeedsAttention",Error=ex.Message});} catch { }
            throw;
        }
    }

}
