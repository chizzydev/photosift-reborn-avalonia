using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoSiftReborn;

/// <summary>Only exactly one reviewed generated Copy A and Move B,
/// after terminal B Undo; no Rename/Reject history permitted. No ordinary authority or automatic replay.</summary>
internal static class M6JP4R23GeneratedCopyUndoAfterB
{
    private static void Witness(WorkspaceSessionStore db,Guid session,string state,string physical)
    {
        var proof=db.InspectM6JP4R23AStage(session,state,physical);
        if(!proof.IsFreshWitness || proof.IsDurableProof || proof.CanReplay ||
           proof.AuthorizesSelectiveUndo || proof.AuthorizesNativeOperation)
            throw new IOException("P4-R23 A native ownership was not freshly witnessed: "+proof.Reason);
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
            throw new IOException("P4-R23 frozen output archive not empty or linked.");
        bool newOwned=!Directory.Exists(owned);
        if(newOwned)Directory.CreateDirectory(owned);
        if(SortingWorkspace.HasLinkedExistingAncestor(owned))
            throw new IOException("P4-R23 linked recovery root.");
        string marker=Path.Combine(owned,".photosift-owner-v1");
        if(!newOwned && !File.Exists(marker))
            throw new IOException("P4-R23 preexisting unmarked recovery root refused.");
        if(newOwned)
        {
            using var fs=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var data=Encoding.UTF8.GetBytes("PhotoSift Reborn managed reversible output archives v1\n");
            fs.Write(data);fs.Flush(flushToDisk:true);
        }
        if(SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
           File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
            throw new IOException("P4-R23 unowned output recovery marker.");
        if(Directory.Exists(operation) || File.Exists(operation))
            throw new IOException("P4-R23 nonempty or preexisting operation directory refused.");
        Directory.CreateDirectory(operation);
        if(SortingWorkspace.HasLinkedExistingAncestor(operation) ||
            File.Exists(a.Recovery) || Directory.Exists(a.Recovery))
            throw new IOException("P4-R23 recovery directory changed.");
    }

    internal static DurableCopyItem UndoExactlyOneGeneratedA(WorkspaceSessionStore db,
        Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        // Fresh exact two-family observation, NOT the four-family R18 branch.
        Witness(db,session,"Committed","BeforeArchive");
        var a=db.CopyItems(session).Single();
        var b=db.MoveItems(session).Single();
        if(a.State!="Committed" || b.State!="Undone" ||
           db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0)
            throw new IOException("P4-R23 reviewed two-family predecessor is not terminal.");
        Witness(db,session,"Committed","BeforeArchive");
        PrepareRecovery(a,db);
        Witness(db,session,"Committed","BeforeArchive");
        var intent=a with {State="UndoIntent"};
        db.PutM6JP4R23GeneratedAUndo(a,intent,b);
        testCut?.Invoke("P4R23_A_UNDO_INTENT_DURABLE");
        Witness(db,session,"UndoIntent","BeforeArchive");
        NativeFileCapability.RelocateNoReplace(a.Destination,a.Recovery);
        testCut?.Invoke("P4R23_A_OUTPUT_RELOCATED_UNJOURNALLED");
        Witness(db,session,"UndoIntent","AfterArchive");
        var undone=a with {State="Undone"};
        db.PutM6JP4R23GeneratedAUndo(intent,undone,b);
        testCut?.Invoke("P4R23_A_UNDO_TERMINAL_DURABLE");
        Witness(db,session,"Undone","AfterArchive");
        if(db.CopyItems(session).Single()!=undone ||
           db.MoveItems(session).Single()!=b ||
           db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0)
            throw new IOException("P4-R23 terminal predecessors altered.");
        return undone;
    }
}
