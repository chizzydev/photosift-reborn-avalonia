using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>Final generated-only Copy A Undo in the reviewed A/B/C three-row
/// lifecycle.  Never resumes an interrupted Undo without new authorization.</summary>
internal static class M6JP4R29GeneratedFirstCopyUndo
{
    private static void PrepareFirstRecovery(DurableCopyItem a,WorkspaceSessionStore db)
    {
        string root=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        string recovery=Path.Combine(root,".photosift-reborn-recovery");
        string operation=Path.Combine(recovery,a.SessionId.ToString("N"),a.Id.ToString("N"));
        string expected=Path.Combine(operation,Path.GetFileName(a.Destination));
        if(!SortingWorkspace.PathComparer.Equals(expected,a.Recovery) ||
           !Directory.Exists(recovery) ||
           SortingWorkspace.HasLinkedExistingAncestor(recovery) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(a.Destination)!) ||
           File.Exists(operation) || Directory.Exists(operation) ||
           File.Exists(a.Recovery) || Directory.Exists(a.Recovery))
            throw new IOException("R29 first Copy recovery directory not vacant/owned.");
        string marker=Path.Combine(recovery,".photosift-owner-v1");
        if(!File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
           File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
            throw new IOException("R29 managed Copy recovery marker mismatch.");
        Directory.CreateDirectory(operation);
        if(SortingWorkspace.HasLinkedExistingAncestor(operation) ||
           File.Exists(a.Recovery) || Directory.Exists(a.Recovery))
            throw new IOException("R29 recovery changed after empty operation creation.");
    }

    internal static DurableCopyItem UndoExactlyFirstCopyAAfterBAndC(
        WorkspaceSessionStore db,Guid session,Action<string>? testCut=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(db);
        var copies=db.CopyItems(session);
        if(copies.Count!=2 || copies[0].State!="Committed" || copies[1].State!="Undone")
            throw new IOException("R29 exact terminal Copy B and committed Copy A required.");
        var a=copies[0];var b=copies[1];
        var moves=db.MoveItems(session);
        if(moves.Count!=1 || moves[0].State!="Undone")
            throw new IOException("R29 exact third Move C terminal state required.");
        var c=moves[0];
        db.RequireM6JP4R29FirstCopyUndoStage(a,"BeforeArchive");
        PrepareFirstRecovery(a,db);
        db.RequireM6JP4R29FirstCopyUndoStage(a,"BeforeArchive");
        var intent=a with {State="UndoIntent"};
        db.PutM6JP4R29FirstCopyUndo(a,intent,"BeforeArchive");
        testCut?.Invoke("P4R29_A_UNDO_INTENT_DURABLE");
        db.RequireM6JP4R29FirstCopyUndoStage(intent,"BeforeArchive");
        NativeFileCapability.RelocateNoReplace(a.Destination,a.Recovery);
        testCut?.Invoke("P4R29_A_OUTPUT_RELOCATED_UNJOURNALLED");
        db.RequireM6JP4R29FirstCopyUndoStage(intent,"AfterArchive");
        var terminal=a with {State="Undone"};
        db.PutM6JP4R29FirstCopyUndo(intent,terminal,"AfterArchive");
        testCut?.Invoke("P4R29_A_UNDONE_DURABLE");
        db.RequireM6JP4R29FirstCopyUndoStage(terminal,"AfterArchive");
        var now=db.CopyItems(session);
        if(now.Count!=2 || now[0]!=terminal || now[1]!=b ||
           db.MoveItems(session).Single()!=c ||
           db.AllRenameHistoryCount()!=0 || db.AllRejectHistoryCount()!=0)
            throw new IOException("R29 terminal reverse changed protected predecessor history.");
        return terminal;
    }
}
