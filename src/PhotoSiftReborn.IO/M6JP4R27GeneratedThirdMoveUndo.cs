using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>Generated-only two-Copy + third-Move C terminal selective Undo.
/// No additional row creation, generic Move Undo, restart replay or ordinary
/// photograph authority. Every physical action follows durable Undo intent.</summary>
internal static class M6JP4R27GeneratedThirdMoveUndo
{
    internal static DurableMoveItem UndoExactlyReviewedGeneratedThirdMoveC(
        WorkspaceSessionStore db,Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        var current=db.MoveItems(session);
        if(current.Count!=1)
            throw new IOException("R27 requires exactly one reviewed Move C journal.");
        var c=current[0];
        if(c.State!="Committed" || c.Error is not null)
            throw new IOException("R27 third generated Move not durably committed.");
        var beforeCopies=db.CopyItems(session).ToArray();
        if(beforeCopies.Length!=2 || beforeCopies.Any(x=>x.State!="Committed"))
            throw new IOException("R27 required Copy predecessor set missing.");
        db.RequireM6JP4R27ThirdMoveUndoStage(c,"BeforeRestore");
        var intent=c with {State="UndoRestoreIntent"};
        db.PutM6JP4R27ThirdMoveUndo(c,intent,"BeforeRestore");
        testCut?.Invoke("P4R27_C_RESTORE_INTENT_DURABLE");
        db.RequireM6JP4R27ThirdMoveUndoStage(intent,"BeforeRestore");
        NativeFileCapability.RelocateNoReplace(intent.OriginalArchive,intent.Source);
        testCut?.Invoke("P4R27_C_SOURCE_RELOCATED_UNJOURNALLED");
        db.RequireM6JP4R27ThirdMoveUndoStage(intent,"AfterRestore");
        var restored=c with {State="SourceRestored"};
        db.PutM6JP4R27ThirdMoveUndo(intent,restored,"AfterRestore");
        testCut?.Invoke("P4R27_C_SOURCE_RESTORED_DURABLE");
        db.RequireM6JP4R27ThirdMoveUndoStage(restored,"AfterRestore");
        var outputIntent=c with {State="UndoOutputArchiveIntent"};
        db.PutM6JP4R27ThirdMoveUndo(restored,outputIntent,"AfterRestore");
        testCut?.Invoke("P4R27_C_OUTPUT_ARCHIVE_INTENT_DURABLE");
        db.RequireM6JP4R27ThirdMoveUndoStage(outputIntent,"AfterRestore");
        NativeFileCapability.RelocateNoReplace(outputIntent.Destination,outputIntent.OutputArchive);
        testCut?.Invoke("P4R27_C_OUTPUT_RELOCATED_UNJOURNALLED");
        db.RequireM6JP4R27ThirdMoveUndoStage(outputIntent,"AfterOutputArchive");
        var terminal=c with {State="Undone"};
        db.PutM6JP4R27ThirdMoveUndo(outputIntent,terminal,"AfterOutputArchive");
        db.RequireM6JP4R27ThirdMoveUndoStage(terminal,"AfterOutputArchive");
        if(db.MoveItems(session).Single()!=terminal ||
           !beforeCopies.SequenceEqual(db.CopyItems(session)) ||
           db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0)
            throw new IOException("R27 terminal two-Copy/Move native journal changed.");
        return terminal;
    }
}
