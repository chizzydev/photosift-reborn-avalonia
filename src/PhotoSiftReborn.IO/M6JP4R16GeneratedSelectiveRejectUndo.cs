using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R16: explicit test-only D Undo, and only for the exact generated
/// Copy A -> Move B -> Rename C -> Reject D committed history.
/// No GUI hook, no public generic grant, no unfinished operation replay,
/// no predecessor Undo, no ordinary photos. DO NOT call on historical labs.
/// </summary>
internal static class M6JP4R16GeneratedSelectiveRejectUndo
{
    private static bool Empty(string path) => !File.Exists(path) && !Directory.Exists(path);

    private static void RequireRestored(WorkspaceSessionStore db,
        DurableCopyItem copy, DurableMoveItem move, DurableRenameItem rename,
        DurableRejectItem reject, string expectedRejectState)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        if(reject.State!=expectedRejectState || reject.Error is not null ||
            reject.Source!=rename.Target || reject.SourceIdentity!=rename.NativeIdentity ||
            reject.SourceHash!=rename.Hash || reject.SourceLength!=rename.Length ||
            reject.SourceWriteTicks!=rename.WriteTicks ||
            rename.ReviewedDirectoryIdentity is null ||
            db.AllCopyHistoryCount()!=1 || db.AllMoveHistoryCount()!=1 ||
            db.AllRenameHistoryCount()!=1 || db.AllRejectHistoryCount()!=1 ||
            db.HasUnreconciledCopy ||
            db.CopyItems(reject.SessionId).SingleOrDefault()!=copy ||
            db.MoveItems(reject.SessionId).SingleOrDefault()!=move ||
            db.RenameItems(reject.SessionId).SingleOrDefault()!=rename ||
            db.RejectItems().SingleOrDefault()!=reject ||
            !Empty(reject.Archive) || !Empty(rename.Source) ||
            !Empty(move.Temp) || !Empty(move.OutputArchive) ||
            !Empty(copy.Temp) || !Empty(copy.Recovery) ||
            !Path.IsPathFullyQualified(reject.Source) ||
            Path.GetFullPath(reject.Source)!=reject.Source ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(reject.Source)!) ||
            NativeFileCapability.DirectoryIdentity(Path.GetDirectoryName(reject.Source)!)!=
                rename.ReviewedDirectoryIdentity ||
            !NativeFileCapability.IsSingleLinkRegularFile(reject.Source) ||
            NativeFileCapability.Identity(reject.Source)!=reject.SourceIdentity ||
            new FileInfo(reject.Source).Length!=reject.SourceLength ||
            new FileInfo(reject.Source).LastWriteTimeUtc.Ticks!=reject.SourceWriteTicks ||
            NativeFileCapability.DeviceId(reject.Source)!=
                NativeFileCapability.DeviceId(Path.GetDirectoryName(reject.Source)!) ||
            SortingWorkspace.ComputeSha256Async(reject.Source).GetAwaiter().GetResult()!=reject.SourceHash)
            throw new IOException("P4-R16 restored D and exact predecessor state not owned.");
        // Four concurrent native handles, including predecessor Copy/Move files
        // and the restored D output. No positive report is used as a durable
        // capability or authorization to start another operation.
        var pin=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
            new(copy.Source,copy.SourceIdentity,copy.SourceHash,
                copy.SourceLength,copy.SourceWriteTicks),
            new(copy.Destination,copy.TargetIdentity!,copy.SourceHash,copy.SourceLength),
            new(move.OriginalArchive,move.SourceIdentity,move.SourceHash,
                move.SourceLength,move.SourceWriteTicks),
            new(reject.Source,reject.SourceIdentity,reject.SourceHash,
                reject.SourceLength,reject.SourceWriteTicks));
        if(!pin.IsFreshWitness || pin.IsDurableProof || pin.AuthorizesNativeOperation ||
            pin.AuthorizesSelectiveUndo)
            throw new IOException("P4-R16 restored four-pin native ownership refused.");
    }

    internal static DurableRejectItem UndoExactlyOneGeneratedFourth(
        WorkspaceSessionStore db,Guid session,Action<string>? testCut=null)
    {
        // This read-only observation itself grants NO Undo capability. The
        // only write authority is the isolated internal test-only method plus
        // its own full fresh preflight, durable CAS and post-intent re-check.
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        var before=db.InspectM6JP4R15CommittedGeneratedFourth(session);
        if(!before.IsFreshWitness || before.RejectId==Guid.Empty ||
            before.IsDurableProof || before.AuthorizesNativeOperation ||
            before.AuthorizesSelectiveUndo)
            throw new IOException("P4-R16 exact committed four-family D preflight refused.");
        var copy=db.CopyItems(session).Single();
        var move=db.MoveItems(session).Single();
        var rename=db.RenameItems(session).Single();
        var reject=db.RejectItems().Single();
        if(reject.Id!=before.RejectId || reject.State!="Committed" ||
            rename.State!="Committed" || move.State!="Committed" ||
            copy.State!="Committed" ||
            !db.InspectM6JP4R15CommittedGeneratedFourth(session).IsFreshWitness)
            throw new IOException("P4-R16 predecessor changed after preflight.");

        // Explicit, durable one-way decision: no subsequent retry is safe.
        var pending=reject with {State="UndoIntent"};
        db.PutM6JP4R16GeneratedRejectUndo(reject,pending);
        testCut?.Invoke("P4R16_REJECT_UNDO_INTENT_DURABLE");

        // Independent, fresh FOUR-family proof with exact UndoIntent journal;
        // no replay and no reliance on stale R15 Committed witness token.
        var fresh=db.InspectM6JP4R16UndoIntent(session);
        if(!fresh.IsFreshWitness || fresh.RejectId!=reject.Id ||
            fresh.IsDurableProof || fresh.AuthorizesNativeOperation ||
            fresh.AuthorizesSelectiveUndo || db.CopyItems(session).Single()!=copy ||
            db.MoveItems(session).Single()!=move || db.RenameItems(session).Single()!=rename ||
            db.RejectItems().Single()!=pending)
            throw new IOException("P4-R16 D UndoIntent physical ownership or lineage changed.");

        // Native Windows no-replace relocation independently re-pins source,
        // parent and target. Never call File.Move with overwrite, delete a
        // foreign occupant, or auto-finalize a prior interrupted UndoIntent.
        NativeFileCapability.RelocateNoReplaceOwned(pending.Archive,pending.Source,
            pending.SourceIdentity,pending.SourceHash,pending.SourceLength,
            pending.SourceWriteTicks,rename.ReviewedDirectoryIdentity!);
        testCut?.Invoke("P4R16_REJECT_UNDO_RELOCATED_UNJOURNALLED");

        // Verify identical native file in exact C path before terminal row.
        RequireRestored(db,copy,move,rename,pending,"UndoIntent");
        var undone=reject with {State="Undone"};
        db.PutM6JP4R16GeneratedRejectUndo(pending,undone);
        if(db.RejectItems().Single()!=undone)
            throw new IOException("P4-R16 D Undone journal not saved exactly.");
        RequireRestored(db,copy,move,rename,undone,"Undone");
        return undone;
    }
}
