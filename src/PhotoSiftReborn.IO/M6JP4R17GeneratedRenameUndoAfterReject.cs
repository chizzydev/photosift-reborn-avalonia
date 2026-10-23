using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R17: exact disposable-generated, four-row, single C Undo ONLY after
/// P4-R16 D Reject Undo has durably finished. No modification to D, B or A,
/// no generalized predecessor Undo, no automatic recovery of an interrupted C.
/// </summary>
internal static class M6JP4R17GeneratedRenameUndoAfterReject
{
    private static void Prove(WorkspaceSessionStore db,Guid session,string cState,
        DurableCopyItem a,DurableMoveItem b,DurableRejectItem d)
    {
        var observed=db.InspectM6JP4R17AfterDUndone(session,cState);
        if(!observed.IsFreshWitness || observed.RejectId!=d.Id ||
            observed.IsDurableProof || observed.AuthorizesNativeOperation ||
            observed.AuthorizesSelectiveUndo || observed.CanModifyJournal ||
            db.CopyItems(session).Single()!=a || db.MoveItems(session).Single()!=b ||
            db.RejectItems().Single()!=d)
            throw new IOException("P4-R17 exact post-D terminal C state or predecessor ownership refused.");
    }

    internal static DurableRenameItem UndoExactlyOneGeneratedC(
        WorkspaceSessionStore db,Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        var a=db.CopyItems(session).Single();
        var b=db.MoveItems(session).Single();
        var c=db.RenameItems(session).Single();
        var d=db.RejectItems().Single();
        if(a.State!="Committed" || b.State!="Committed" ||
            c.State!="Committed" || d.State!="Undone" ||
            c.Error is not null || d.Error is not null)
            throw new IOException("P4-R17 C requires exact completed D Undo and committed A/B/C.");
        Prove(db,session,"Committed",a,b,d);
        var pending=c with {State="UndoIntent"};
        db.PutM6JP4R17GeneratedRenameUndoAfterReject(c,pending,d);
        testCut?.Invoke("P4R17_C_UNDO_INTENT_DURABLE");

        Prove(db,session,"UndoIntent",a,b,d);
        if(db.RenameItems(session).Single()!=pending)
            throw new IOException("P4-R17 new C UndoIntent not persisted exactly.");
        // Native primitive pins file + parent and refuses any destination occupant.
        NativeFileCapability.RelocateNoReplaceOwned(
            pending.Target,pending.Source,pending.NativeIdentity,pending.Hash,
            pending.Length,pending.WriteTicks,pending.ReviewedDirectoryIdentity!);
        testCut?.Invoke("P4R17_C_UNDO_RELOCATED_UNJOURNALLED");

        // Physical result and all 4 journal records must agree BEFORE terminal CAS.
        var undone=c with {State="Undone"};
        if(!DurableRenameCoordinator.ProveState(undone))
            throw new IOException("P4-R17 restored C location is not owned.");
        // Fresh pin despite durable C still being UndoIntent; check four files in
        // their new location with the separately bounded read-only witness
        // immediately after terminal record below. Validate physical state here.
        if(!NativeFileCapability.IsSingleLinkRegularFile(undone.Source) ||
            NativeFileCapability.Identity(undone.Source)!=undone.NativeIdentity ||
            new FileInfo(undone.Source).Length!=undone.Length ||
            new FileInfo(undone.Source).LastWriteTimeUtc.Ticks!=undone.WriteTicks ||
            SortingWorkspace.ComputeSha256Async(undone.Source).GetAwaiter().GetResult()!=undone.Hash ||
            File.Exists(undone.Target) || Directory.Exists(undone.Target) ||
            File.Exists(d.Archive) || Directory.Exists(d.Archive) ||
            db.RenameItems(session).Single()!=pending ||
            db.CopyItems(session).Single()!=a || db.MoveItems(session).Single()!=b ||
            db.RejectItems().Single()!=d)
            throw new IOException("P4-R17 restored physical C/predecessor boundary refused.");
        db.PutM6JP4R17GeneratedRenameUndoAfterReject(pending,undone,d);
        if(db.RenameItems(session).Single()!=undone)
            throw new IOException("P4-R17 C terminal Undo not persisted.");
        Prove(db,session,"Undone",a,b,d);
        return undone;
    }
}
