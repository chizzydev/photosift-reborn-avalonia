using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// P4-R12: FOURTH family on ONE disposable marked generated Copy A ->
// Move B -> Rename C predecessor chain. The physical Reject D moves the
// renamed installed file into an exact, unoccupied SAME-DIRECTORY archive
// name through the pinned Windows native no-replace relocation primitive.
// This is NOT the ordinary Reject coordinator or user-photos authority.
// Deliberately no auto-replay, no Reject Undo and no general epoch grant.
internal sealed record M6JP4R12GeneratedRejectPlan(Guid Id,Guid SessionId,
    string Source,string Archive,string NativeIdentity,string Hash,
    long Length,long WriteTicks,string DirectoryIdentity,
    DurableCopyItem Copy,DurableMoveItem Move,DurableRenameItem Rename);

internal static class M6JP4R12GeneratedRenameReject
{
    private static bool Empty(string path) => !File.Exists(path) && !Directory.Exists(path);

    internal static void RequireFixture(WorkspaceSessionStore db)
    {
        if (!OperatingSystem.IsWindows() || !NativeFileCapability.IsSupported ||
            !P5CGuiFixturePolicy.IsMarkedStore(db))
            throw new IOException("P4-R12 fourth-family native Reject is generated-only Windows.");
        string state=Path.GetDirectoryName(db.DatabasePath)
            ?? throw new IOException("P4-R12 state directory unavailable.");
        string root=Directory.GetParent(state)?.FullName
            ?? throw new IOException("P4-R12 fixture root unavailable.");
        string marker=Path.Combine(root,"P3B2_TEST_ONLY.marker");
        if (Path.GetFileName(state)!="state" ||
            !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal) ||
            SortingWorkspace.HasLinkedExistingAncestor(root) ||
            !File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
            File.ReadAllText(marker)!="synthetic-interruption-only")
            throw new IOException("P4-R12 unmarked or linked fixture refused.");
    }

    private static void Prove(WorkspaceSessionStore db,M6JP4R12GeneratedRejectPlan plan,
        string? requiredRejectState)
    {
        RequireFixture(db);
        if (plan.Id==Guid.Empty || plan.SessionId==Guid.Empty || plan.Copy is null ||
            plan.Move is null || plan.Rename is null ||
            plan.Copy.SessionId!=plan.SessionId || plan.Move.SessionId!=plan.SessionId ||
            plan.Rename.SessionId!=plan.SessionId || plan.Move.PhotoId==plan.Copy.PhotoId ||
            plan.Rename.PhotoId!=plan.Move.PhotoId ||
            plan.Copy.State!="Committed" || plan.Move.State!="Committed" ||
            plan.Rename.State!="Committed" ||
            plan.Copy.Error is not null || plan.Move.Error is not null ||
            plan.Rename.Error is not null || plan.Move.TargetIdentity is null ||
            plan.Copy.TargetIdentity is null ||
            plan.Source!=plan.Rename.Target || plan.NativeIdentity!=plan.Rename.NativeIdentity ||
            plan.Hash!=plan.Rename.Hash || plan.Length!=plan.Rename.Length ||
            plan.WriteTicks!=plan.Rename.WriteTicks ||
            plan.DirectoryIdentity!=plan.Rename.ReviewedDirectoryIdentity ||
            db.HasUnreconciledCopy ||
            db.AllCopyHistoryCount()!=1 || db.AllMoveHistoryCount()!=1 ||
            db.AllRenameHistoryCount()!=1 ||
            db.AllRejectHistoryCount()!=(requiredRejectState is null?0:1) ||
            db.CopyItems(plan.SessionId).SingleOrDefault()!=plan.Copy ||
            db.MoveItems(plan.SessionId).SingleOrDefault()!=plan.Move ||
            db.RenameItems(plan.SessionId).SingleOrDefault()!=plan.Rename)
            throw new IOException("P4-R12 exact fourth-family predecessor journal changed.");
        if(requiredRejectState is not null)
        {
            var rejected=db.RejectItems();
            if(rejected.Count!=1 || rejected[0].Id!=plan.Id ||
                rejected[0].SessionId!=plan.SessionId ||
                rejected[0].Source!=plan.Source || rejected[0].Archive!=plan.Archive ||
                rejected[0].SourceIdentity!=plan.NativeIdentity ||
                rejected[0].SourceHash!=plan.Hash ||
                rejected[0].SourceLength!=plan.Length ||
                rejected[0].SourceWriteTicks!=plan.WriteTicks ||
                rejected[0].State!=requiredRejectState || rejected[0].Error is not null)
                throw new IOException("P4-R12 durable fourth-family Reject Intent was altered.");
        }
        var fixture=Directory.GetParent(Path.GetDirectoryName(db.DatabasePath)!)!.FullName;
        var output=Path.Combine(fixture,"output");
        string parent=Path.GetDirectoryName(plan.Source)!;
        if (!SortingWorkspace.PathComparer.Equals(parent,Path.GetDirectoryName(plan.Archive)) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(parent),output) ||
            !Path.IsPathFullyQualified(plan.Archive) ||
            !string.Equals(Path.GetFullPath(plan.Archive),plan.Archive,StringComparison.Ordinal) ||
            !string.Equals(plan.Archive.Normalize(System.Text.NormalizationForm.FormC),
                plan.Archive,StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(plan.Archive),Path.GetExtension(plan.Source),StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(plan.Archive).StartsWith(".photosift-reborn-generated-reject-"+plan.Id.ToString("N"),StringComparison.Ordinal) ||
            SortingWorkspace.HasLinkedExistingAncestor(parent) ||
            NativeFileCapability.DirectoryIdentity(parent)!=plan.DirectoryIdentity ||
            !Empty(plan.Archive) ||
            Directory.EnumerateFileSystemEntries(parent).Any(p=>
                string.Equals(Path.GetFileName(p).Normalize(System.Text.NormalizationForm.FormC),
                    Path.GetFileName(plan.Archive).Normalize(System.Text.NormalizationForm.FormC),
                    StringComparison.OrdinalIgnoreCase)) ||
            !NativeFileCapability.IsSingleLinkRegularFile(plan.Source) ||
            NativeFileCapability.Identity(plan.Source)!=plan.NativeIdentity ||
            new FileInfo(plan.Source).LastWriteTimeUtc.Ticks!=plan.WriteTicks ||
            NativeFileCapability.DeviceId(plan.Source)!=NativeFileCapability.DeviceId(parent) ||
            !Empty(plan.Rename.Source))
            throw new IOException("P4-R12 renamed native source or recovery destination is not owned.");
        // All four Windows handles are concurrently pinned. This native
        // observation is re-acquired after each durable journal transition,
        // immediately before the independent handle-based no-replace move.
        var pin=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
            new(plan.Copy.Source,plan.Copy.SourceIdentity,plan.Copy.SourceHash,
                plan.Copy.SourceLength,plan.Copy.SourceWriteTicks),
            new(plan.Copy.Destination,plan.Copy.TargetIdentity!,plan.Copy.SourceHash,
                plan.Copy.SourceLength),
            new(plan.Move.OriginalArchive,plan.Move.SourceIdentity,plan.Move.SourceHash,
                plan.Move.SourceLength,plan.Move.SourceWriteTicks),
            new(plan.Source,plan.NativeIdentity,plan.Hash,plan.Length,plan.WriteTicks));
        if(!pin.IsFreshWitness || pin.IsDurableProof || pin.AuthorizesNativeOperation ||
            pin.AuthorizesSelectiveUndo)
            throw new IOException("P4-R12 exact four-file native chain refused: "+pin.Explanation);
    }

    internal static M6JP4R12GeneratedRejectPlan Prepare(WorkspaceSessionStore db,Guid session)
    {
        RequireFixture(db);
        if (db.AllRejectHistoryCount()!=0 || db.AllRenameHistoryCount()!=1)
            throw new IOException("P4-R12 requires an untouched fourth-family history.");
        var renamed=db.RenameItems(session);
        if(renamed.Count!=1 || renamed[0].State!="Committed")
            throw new IOException("P4-R12 exact committed third-family Rename missing.");
        // Reuse the independently accepted third-family root-proof machinery
        // before inserting any fourth-family journal row. In particular this
        // verifies the original consumed Copy->Move review seal and raw Copy
        // predecessor digest, not merely the three current journal states.
        M6JP4R9GeneratedMoveRename.AttestCommittedForGeneratedFourthFamily(db,session);
        var copies=db.CopyItems(session);var moves=db.MoveItems(session);
        if(copies.Count!=1 || moves.Count!=1)
            throw new IOException("P4-R12 exact Copy/Move predecessor rows missing.");
        var row=renamed[0];var id=Guid.NewGuid();
        string parent=Path.GetDirectoryName(row.Target)!;
        string archive=Path.Combine(parent,
            ".photosift-reborn-generated-reject-"+id.ToString("N")+Path.GetExtension(row.Target));
        var plan=new M6JP4R12GeneratedRejectPlan(id,session,row.Target,archive,
            row.NativeIdentity,row.Hash,row.Length,row.WriteTicks,
            row.ReviewedDirectoryIdentity!,copies[0],moves[0],row);
        Prove(db,plan,null);
        return plan;
    }

    internal static DurableRejectItem Apply(WorkspaceSessionStore db,
        M6JP4R12GeneratedRejectPlan plan,Action<string>? testCut=null)
    {
        Prove(db,plan,null);
        var row=new DurableRejectItem(plan.Id,db.MoveInstallationId(),plan.SessionId,
            plan.Rename.PhotoId,plan.Rename.Revision,plan.Source,plan.Archive,
            plan.Hash,plan.NativeIdentity,plan.Length,plan.WriteTicks,"Intent");
        // No previous Reject may exist. The special internal writer is restricted
        // to this one generated four-family chain; generic PutRejectItem remains
        // locked against any mixed history, even synthetic.
        db.PutM6JP4R12GeneratedReject(row,null);
        testCut?.Invoke("P4R12_REJECT_INTENT_DURABLE");
        try
        {
            Prove(db,plan,"Intent");
            row=row with {State="RelocateIntent"};
            db.PutM6JP4R12GeneratedReject(row,"Intent");
            testCut?.Invoke("P4R12_REJECT_RELOCATE_INTENT_DURABLE");
            Prove(db,plan,"RelocateIntent");
            NativeFileCapability.RelocateNoReplaceOwned(plan.Source,plan.Archive,
                plan.NativeIdentity,plan.Hash,plan.Length,plan.WriteTicks,
                plan.DirectoryIdentity);
            testCut?.Invoke("P4R12_REJECT_RELOCATED_UNJOURNALLED");
            if(!Empty(plan.Source) || !NativeFileCapability.IsSingleLinkRegularFile(plan.Archive) ||
                NativeFileCapability.Identity(plan.Archive)!=plan.NativeIdentity ||
                new FileInfo(plan.Archive).Length!=plan.Length ||
                SortingWorkspace.ComputeSha256Async(plan.Archive).GetAwaiter().GetResult()!=plan.Hash)
                throw new IOException("P4-R12 committed Reject archive does not contain the owned file.");
            row=row with {State="Committed"};
            db.PutM6JP4R12GeneratedReject(row,"RelocateIntent");
            if(db.RejectItems().SingleOrDefault()!=row)
                throw new IOException("P4-R12 terminal Reject journal did not persist exactly.");
            return row;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            try {db.PutM6JP4R12GeneratedReject(
                row with {State="NeedsAttention",Error=ex.Message},row.State);} catch { }
            throw;
        }
    }
}
