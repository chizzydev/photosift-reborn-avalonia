using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PhotoSiftReborn;

/// <summary>Generated-only Copy B Undo after R27 terminal third Move C Undo.
/// No Copy A Undo, general multirow epoch, automatic replay or ordinary authority.</summary>
internal static class M6JP4R28GeneratedSecondCopyUndo
{
    private static void PrepareOwnedRecovery(DurableCopyItem b,WorkspaceSessionStore db)
    {
        string root=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        string recovery=Path.Combine(root,".photosift-reborn-recovery");
        string operation=Path.Combine(recovery,b.SessionId.ToString("N"),b.Id.ToString("N"));
        string expected=Path.Combine(operation,Path.GetFileName(b.Destination));
        if(!SortingWorkspace.PathComparer.Equals(expected,b.Recovery) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(b.Destination)!) ||
           File.Exists(b.Recovery) || Directory.Exists(b.Recovery) ||
           File.Exists(operation) || Directory.Exists(operation))
            throw new IOException("R28 unapproved second Copy output recovery location.");
        bool fresh=!Directory.Exists(recovery);
        if(fresh)Directory.CreateDirectory(recovery);
        if(SortingWorkspace.HasLinkedExistingAncestor(recovery))
            throw new IOException("R28 linked output recovery root.");
        string marker=Path.Combine(recovery,".photosift-owner-v1");
        if(!fresh && !File.Exists(marker))
            throw new IOException("R28 preexisting unmarked recovery refused.");
        if(fresh)
        {
            using var stream=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var bytes=Encoding.UTF8.GetBytes("PhotoSift Reborn managed reversible output archives v1\n");
            stream.Write(bytes);stream.Flush(flushToDisk:true);
        }
        if(SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
           File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
            throw new IOException("R28 untrusted ownership marker.");
        Directory.CreateDirectory(operation);
        if(SortingWorkspace.HasLinkedExistingAncestor(operation) ||
           File.Exists(b.Recovery) || Directory.Exists(b.Recovery))
            throw new IOException("R28 recovery hierarchy modified.");
    }

    internal static DurableCopyItem UndoExactlySecondCopyBAfterThirdMoveC(
        WorkspaceSessionStore db,Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        var copies=db.CopyItems(session);
        if(copies.Count!=2 || copies[0].State!="Committed" || copies[1].State!="Committed")
            throw new IOException("R28 exact two committed Copy predecessors required.");
        var a=copies[0];var b=copies[1];
        var moves=db.MoveItems(session);
        if(moves.Count!=1 || moves[0].State!="Undone")
            throw new IOException("R28 reviewed third Move C must already be Undone.");
        var c=moves[0];
        db.RequireM6JP4R28SecondCopyUndoStage(b,"BeforeArchive");
        PrepareOwnedRecovery(b,db);
        db.RequireM6JP4R28SecondCopyUndoStage(b,"BeforeArchive");
        var intent=b with {State="UndoIntent"};
        db.PutM6JP4R28SecondCopyUndo(b,intent,"BeforeArchive");
        testCut?.Invoke("P4R28_B_UNDO_INTENT_DURABLE");
        db.RequireM6JP4R28SecondCopyUndoStage(intent,"BeforeArchive");
        NativeFileCapability.RelocateNoReplace(b.Destination,b.Recovery);
        testCut?.Invoke("P4R28_B_OUTPUT_RELOCATED_UNJOURNALLED");
        db.RequireM6JP4R28SecondCopyUndoStage(intent,"AfterArchive");
        var terminal=b with {State="Undone"};
        db.PutM6JP4R28SecondCopyUndo(intent,terminal,"AfterArchive");
        testCut?.Invoke("P4R28_B_UNDONE_DURABLE");
        db.RequireM6JP4R28SecondCopyUndoStage(terminal,"AfterArchive");
        var updated=db.CopyItems(session);
        if(updated.Count!=2 || updated[0]!=a || updated[1]!=terminal ||
           db.MoveItems(session).Single()!=c ||
           db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0)
            throw new IOException("R28 terminal Copy B Undo changed protected histories.");
        return terminal;
    }
}
