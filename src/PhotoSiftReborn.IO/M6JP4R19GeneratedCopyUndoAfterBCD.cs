using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoSiftReborn;

/// <summary>Only the exact one-Copy/one-Move/one-Rename/one-Reject generated
/// history, after B/C/D terminal Undo. No ordinary authority or automatic replay.</summary>
internal static class M6JP4R19GeneratedCopyUndoAfterBCD
{
    private static void Witness(WorkspaceSessionStore db,Guid session,string state,string physical)
    {
        var proof=db.InspectM6JP4R19AStage(session,state,physical);
        if(!proof.IsFreshWitness || proof.IsDurableProof || proof.CanReplay ||
           proof.AuthorizesSelectiveUndo || proof.AuthorizesNativeOperation)
            throw new IOException("P4-R19 A native ownership was not freshly witnessed: "+proof.Reason);
    }

    /// <summary>Only mark an exact empty generated recovery hierarchy.
    /// No existing foreign directory may be claimed or overwritten.</summary>
    private static void PrepareRecovery(DurableCopyItem a,WorkspaceSessionStore db)
    {
        string root=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        string output=Path.Combine(root,"output");
        // Match DurableCopyCoordinator's journalled recoveryParent, not outputRoot.
        string owned=Path.Combine(root,".photosift-reborn-recovery");
        string session=Path.Combine(owned,a.SessionId.ToString("N"));
        string operation=Path.Combine(session,a.Id.ToString("N"));
        string expected=Path.Combine(operation,Path.GetFileName(a.Destination));
        if(!SortingWorkspace.PathComparer.Equals(a.Recovery,expected) ||
            SortingWorkspace.HasLinkedExistingAncestor(output) ||
            File.Exists(a.Recovery) || Directory.Exists(a.Recovery))
            throw new IOException("P4-R19 frozen output archive not empty or linked.");
        bool newOwned=!Directory.Exists(owned);
        if(newOwned)Directory.CreateDirectory(owned);
        if(SortingWorkspace.HasLinkedExistingAncestor(owned))
            throw new IOException("P4-R19 linked recovery root.");
        string marker=Path.Combine(owned,".photosift-owner-v1");
        if(!newOwned && !File.Exists(marker))
            throw new IOException("P4-R19 preexisting unmarked recovery root refused.");
        if(newOwned)
        {
            using var fs=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var data=Encoding.UTF8.GetBytes("PhotoSift Reborn managed reversible output archives v1\n");
            fs.Write(data);fs.Flush(flushToDisk:true);
        }
        if(SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
           File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
            throw new IOException("P4-R19 unowned output recovery marker.");
        if(Directory.Exists(operation) || File.Exists(operation))
            throw new IOException("P4-R19 nonempty or preexisting operation directory refused.");
        Directory.CreateDirectory(operation);
        if(SortingWorkspace.HasLinkedExistingAncestor(operation) ||
            File.Exists(a.Recovery) || Directory.Exists(a.Recovery))
            throw new IOException("P4-R19 recovery directory changed.");
    }

    internal static DurableCopyItem UndoExactlyOneGeneratedA(WorkspaceSessionStore db,
        Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        if(!db.InspectM6JP4R18BStage(session,"Undone","AfterOutputArchive").IsFreshWitness)
            throw new IOException("P4-R19 B/C/D Undo not durably resolved with native ownership.");
        var a=db.CopyItems(session).Single();
        var b=db.MoveItems(session).Single();
        var c=db.RenameItems(session).Single();
        var d=db.RejectItems().Single();
        if(a.State!="Committed" || b.State!="Undone" ||
           c.State!="Undone" || d.State!="Undone")
            throw new IOException("P4-R19 predecessor journal not terminal.");
        Witness(db,session,"Committed","BeforeArchive");
        PrepareRecovery(a,db);
        Witness(db,session,"Committed","BeforeArchive");
        var intent=a with {State="UndoIntent"};
        db.PutM6JP4R19GeneratedAUndo(a,intent,b,c,d);
        testCut?.Invoke("P4R19_A_UNDO_INTENT_DURABLE");
        Witness(db,session,"UndoIntent","BeforeArchive");
        NativeFileCapability.RelocateNoReplace(a.Destination,a.Recovery);
        testCut?.Invoke("P4R19_A_OUTPUT_RELOCATED_UNJOURNALLED");
        Witness(db,session,"UndoIntent","AfterArchive");
        var undone=a with {State="Undone"};
        db.PutM6JP4R19GeneratedAUndo(intent,undone,b,c,d);
        testCut?.Invoke("P4R19_A_UNDO_TERMINAL_DURABLE");
        Witness(db,session,"Undone","AfterArchive");
        if(db.CopyItems(session).Single()!=undone ||
           db.MoveItems(session).Single()!=b ||
           db.RenameItems(session).Single()!=c || db.RejectItems().Single()!=d)
            throw new IOException("P4-R19 terminal predecessors altered.");
        return undone;
    }
}
