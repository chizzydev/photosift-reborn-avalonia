using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R18: test-only exact B Undo after both D Reject and C Rename are durably
/// Undone. Two independent native no-replace relocations with four persisted
/// B states; no general authorization, GUI, retry or interrupted replay.
/// Cooperative local Windows generated fixtures only.
/// </summary>
internal static class M6JP4R18GeneratedMoveUndoAfterCD
{
    private static void Witness(WorkspaceSessionStore db,Guid session,
        string journal,string physical)
    {
        var seen=db.InspectM6JP4R18BStage(session,journal,physical);
        if(!seen.IsFreshWitness || seen.IsDurableProof || seen.CanReplay ||
            seen.AuthorizesNativeOperation || seen.AuthorizesSelectiveUndo)
            throw new IOException("P4-R18 fresh owned B stage refused.");
    }

    internal static DurableMoveItem UndoExactlyOneGeneratedB(WorkspaceSessionStore db,
        Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        if(!db.InspectM6JP4R17AfterDUndone(session,"Undone").IsFreshWitness)
            throw new IOException("P4-R18 requires completed C and D Undo under R17 observation.");
        var a=db.CopyItems(session).Single();
        var b=db.MoveItems(session).Single();
        var c=db.RenameItems(session).Single();
        var d=db.RejectItems().Single();
        if(a.State!="Committed" || b.State!="Committed" ||
            c.State!="Undone" || d.State!="Undone")
            throw new IOException("P4-R18 predecessor state not terminal.");
        Witness(db,session,"Committed","BeforeRestore");

        // Durable intent always precedes physical modification. A crashed
        // UndoRestoreIntent is an unresolved state, NEVER an implicit retry.
        var restore=b with {State="UndoRestoreIntent"};
        db.PutM6JP4R18GeneratedBUndo(b,restore,a,c,d);
        testCut?.Invoke("P4R18_B_RESTORE_INTENT_DURABLE");
        Witness(db,session,"UndoRestoreIntent","BeforeRestore");
        // Unlike same-directory C/D Undo, Move's recovery directory differs
        // from its source parent. The existing native same-volume no-replace
        // primitive is combined with immediate ownership/marker rechecks.
        NativeFileCapability.RelocateNoReplace(restore.OriginalArchive,restore.Source);
        testCut?.Invoke("P4R18_B_SOURCE_RELOCATED_UNJOURNALLED");
        Witness(db,session,"UndoRestoreIntent","AfterRestore");
        var restored=b with {State="SourceRestored"};
        db.PutM6JP4R18GeneratedBUndo(restore,restored,a,c,d);
        testCut?.Invoke("P4R18_B_SOURCE_RESTORED_DURABLE");
        Witness(db,session,"SourceRestored","AfterRestore");

        var archiveIntent=b with {State="UndoOutputArchiveIntent"};
        db.PutM6JP4R18GeneratedBUndo(restored,archiveIntent,a,c,d);
        testCut?.Invoke("P4R18_B_OUTPUT_ARCHIVE_INTENT_DURABLE");
        Witness(db,session,"UndoOutputArchiveIntent","AfterRestore");
        NativeFileCapability.RelocateNoReplace(archiveIntent.Destination,archiveIntent.OutputArchive);
        testCut?.Invoke("P4R18_B_OUTPUT_RELOCATED_UNJOURNALLED");
        Witness(db,session,"UndoOutputArchiveIntent","AfterOutputArchive");
        var undone=b with {State="Undone"};
        db.PutM6JP4R18GeneratedBUndo(archiveIntent,undone,a,c,d);
        Witness(db,session,"Undone","AfterOutputArchive");
        if(db.CopyItems(session).Single()!=a || db.RenameItems(session).Single()!=c ||
            db.RejectItems().Single()!=d || db.MoveItems(session).Single()!=undone)
            throw new IOException("P4-R18 terminal predecessor/journal drift.");
        return undone;
    }
}
