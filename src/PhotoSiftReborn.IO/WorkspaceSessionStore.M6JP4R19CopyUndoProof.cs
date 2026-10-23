using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>Ephemeral observation, never an approval or replay token.</summary>
internal sealed record M6JP4R19CopyObservation(bool IsFreshWitness,string Reason)
{
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool IsDurableProof => false;
    public bool CanReplay => false;
}

public sealed partial class WorkspaceSessionStore
{
    internal M6JP4R19CopyObservation InspectM6JP4R19AStage(
        Guid session,string journalState,string physicalState)
    {
        lock(_dbGate)
        {
            try
            {
                M6JP4R12GeneratedRenameReject.RequireFixture(this);
                if(session==Guid.Empty || journalState is not ("Committed" or "UndoIntent" or "Undone") ||
                    physicalState is not ("BeforeArchive" or "AfterArchive") ||
                    (journalState=="Committed" && physicalState!="BeforeArchive") ||
                    (journalState=="Undone" && physicalState!="AfterArchive") ||
                    AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                    AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                    CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy)
                    throw new IOException("P4-R19 exact four-family state refused.");
                static (string Id,string Session,string Json) Raw(
                    Microsoft.Data.Sqlite.SqliteConnection cn,string table)
                {
                    if(table is not ("copy_items" or "move_items" or "rename_items" or "reject_items"))
                        throw new IOException("Unexpected journal table.");
                    using var cmd=cn.CreateCommand();
                    cmd.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                    using var rd=cmd.ExecuteReader();
                    if(!rd.Read())throw new IOException("Journal missing.");
                    var row=(rd.GetString(0),rd.GetString(1),rd.GetString(2));
                    if(rd.Read() || row.Item1.Length!=32 || row.Item2.Length!=32 || row.Item3.Length>32768)
                        throw new IOException("Journal has ambiguous identity or size.");
                    return row;
                }
                long version=M6JDataVersion();
                var ar=Raw(_connection,"copy_items");
                var br=Raw(_connection,"move_items");
                var cr=Raw(_connection,"rename_items");
                var dr=Raw(_connection,"reject_items");
                var a=JsonSerializer.Deserialize<DurableCopyItem>(ar.Json) ?? throw new IOException("Copy unreadable.");
                var b=JsonSerializer.Deserialize<DurableMoveItem>(br.Json) ?? throw new IOException("Move unreadable.");
                var c=JsonSerializer.Deserialize<DurableRenameItem>(cr.Json) ?? throw new IOException("Rename unreadable.");
                var d=JsonSerializer.Deserialize<DurableRejectItem>(dr.Json) ?? throw new IOException("Reject unreadable.");
                bool Row(Guid id,Guid owner,(string Id,string Session,string Json) row)
                    => id!=Guid.Empty && owner==session && id.ToString("N")==row.Id && owner.ToString("N")==row.Session;
                var installation=MoveInstallationId();
                if(!Row(a.Id,a.SessionId,ar) || !Row(b.Id,b.SessionId,br) ||
                    !Row(c.Id,c.SessionId,cr) || !Row(d.Id,d.SessionId,dr) ||
                    a.State!=journalState || b.State!="Undone" || c.State!="Undone" || d.State!="Undone" ||
                    a.Error is not null || b.Error is not null || c.Error is not null || d.Error is not null ||
                    a.TargetIdentity is null || b.SourceIdentity is null || b.TargetIdentity is null ||
                    b.InstallationId!=installation || c.InstallationId!=installation || d.InstallationId!=installation ||
                    a.PhotoId==b.PhotoId || b.PhotoId!=c.PhotoId || c.PhotoId!=d.PhotoId ||
                    b.Destination!=c.Source || b.TargetIdentity!=c.NativeIdentity ||
                    c.Target!=d.Source || c.NativeIdentity!=d.SourceIdentity ||
                    b.SourceHash!=c.Hash || c.Hash!=d.SourceHash ||
                    b.SourceLength!=c.Length || c.Length!=d.SourceLength ||
                    c.WriteTicks!=d.SourceWriteTicks || d.SessionRevision!=c.Revision)
                    throw new IOException("P4-R19 terminal predecessor/history mismatch.");
                var review=M6JReadReview(b.Id) ?? throw new IOException("Original Move review missing.");
                if(review.State!="Consumed" || review.Family!="Move" ||
                    review.Installation!=installation.ToString("N") ||
                    review.Session!=session.ToString("N") ||
                    review.ReviewDigest!=M6JReviewDigest(review.CandidateDigest,
                        review.HistoryDigest,review.CheckpointDigest))
                    throw new IOException("Original reviewed Move approval changed.");
                var plan=M6JDecodeStored(review);
                if(plan.Family!="Move" || plan.Id!=b.Id || plan.SessionId!=session ||
                    plan.PhotoId!=b.PhotoId || plan.Source!=b.Source ||
                    plan.Destination!=b.Destination || plan.SourceIdentity!=b.SourceIdentity ||
                    plan.SourceHash!=b.SourceHash || plan.SourceLength!=b.SourceLength ||
                    plan.SourceWriteTicks!=b.SourceWriteTicks)
                    throw new IOException("Consumed Move candidate changed.");
                // Normalize ONLY the state that this exact R19 transaction is permitted
                // to change: reconstruct the original reviewed committed Copy JSON.
                var originalCopyJson=JsonSerializer.Serialize(a with {State="Committed"});
                var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach(string value in new[]{ar.Id,ar.Session,originalCopyJson})
                    history.Append(value.Length).Append(':').Append(value);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                if(M6JHash(history.ToString())!=review.HistoryDigest)
                    throw new IOException("P4-R19 original sealed Copy predecessor no longer matches.");
                string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                string sourceParent=Path.Combine(root,"source");
                string output=Path.Combine(root,"output");
                // DurableCopyCoordinator records recovery one level ABOVE the configured output root.
                // The root/output fixture has recoveryParent = root, not root/output.
                string recoveryRoot=Path.Combine(root,".photosift-reborn-recovery");
                string expectedRecovery=Path.Combine(recoveryRoot,session.ToString("N"),
                    a.Id.ToString("N"),Path.GetFileName(a.Destination));
                string destinationParent=Path.GetDirectoryName(a.Destination)!;
                string operation=Path.GetDirectoryName(b.OriginalArchive)!;
                string moveRootMarker=Path.Combine(sourceParent,".photosift-reborn-recovery",".photosift-move-owner-v1");
                string moveOperationMarker=Path.Combine(operation,".photosift-operation-v1");
                static bool Empty(string path)=>!File.Exists(path) && !Directory.Exists(path);
                if(!SortingWorkspace.PathComparer.Equals(a.Recovery,expectedRecovery) ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(a.Source),sourceParent) ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(destinationParent),output) ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.Source),sourceParent) ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.OutputArchive),operation) ||
                    !Directory.Exists(sourceParent) || !Directory.Exists(destinationParent) ||
                    !Directory.Exists(operation) ||
                    SortingWorkspace.HasLinkedExistingAncestor(sourceParent) ||
                    SortingWorkspace.HasLinkedExistingAncestor(output) ||
                    SortingWorkspace.HasLinkedExistingAncestor(operation) ||
                    !File.Exists(moveRootMarker) || !File.Exists(moveOperationMarker) ||
                    SortingWorkspace.HasSymlinkOrReparsePoint(moveRootMarker) ||
                    SortingWorkspace.HasSymlinkOrReparsePoint(moveOperationMarker) ||
                    File.ReadAllText(moveRootMarker)!="PhotoSift Reborn Move v1:"+installation.ToString("N")+"\n" ||
                    File.ReadAllText(moveOperationMarker)!="PhotoSift Reborn Move operation "+b.Id.ToString("N")+"\n" ||
                    !Empty(b.Destination) || !Empty(b.OriginalArchive) ||
                    !Empty(c.Target) || !Empty(d.Archive) || !Empty(a.Temp) || !Empty(b.Temp))
                    throw new IOException("P4-R19 generated path or B/C/D ownership marker invalid.");
                // No marker is created in this read-only method. Creation is a
                // separate non-authoritative pre-intent preparation step.
                if(Directory.Exists(recoveryRoot))
                {
                    string marker=Path.Combine(recoveryRoot,".photosift-owner-v1");
                    if(!File.Exists(marker) || SortingWorkspace.HasLinkedExistingAncestor(recoveryRoot) ||
                        File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
                        throw new IOException("P4-R19 recovery marker is foreign or modified.");
                }
                string actualOutput=physicalState=="AfterArchive"?a.Recovery:a.Destination;
                string absentOutput=physicalState=="AfterArchive"?a.Destination:a.Recovery;
                if(!Empty(absentOutput) || !NativeFileCapability.IsSingleLinkRegularFile(a.Source) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(actualOutput) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(b.Source) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(b.OutputArchive) ||
                    NativeFileCapability.Identity(a.Source)!=a.SourceIdentity ||
                    NativeFileCapability.Identity(actualOutput)!=a.TargetIdentity ||
                    NativeFileCapability.Identity(b.Source)!=b.SourceIdentity ||
                    NativeFileCapability.Identity(b.OutputArchive)!=b.TargetIdentity ||
                    new FileInfo(a.Source).Length!=a.SourceLength ||
                    new FileInfo(a.Source).LastWriteTimeUtc.Ticks!=a.SourceWriteTicks ||
                    new FileInfo(actualOutput).Length!=a.SourceLength ||
                    new FileInfo(actualOutput).LastWriteTimeUtc.Ticks!=a.TargetWriteTicks ||
                    new FileInfo(b.Source).Length!=b.SourceLength ||
                    new FileInfo(b.Source).LastWriteTimeUtc.Ticks!=b.SourceWriteTicks ||
                    new FileInfo(b.OutputArchive).Length!=b.SourceLength ||
                    new FileInfo(b.OutputArchive).LastWriteTimeUtc.Ticks!=c.WriteTicks ||
                    SortingWorkspace.ComputeSha256Async(a.Source).GetAwaiter().GetResult()!=a.SourceHash ||
                    SortingWorkspace.ComputeSha256Async(actualOutput).GetAwaiter().GetResult()!=a.SourceHash ||
                    SortingWorkspace.ComputeSha256Async(b.Source).GetAwaiter().GetResult()!=b.SourceHash ||
                    SortingWorkspace.ComputeSha256Async(b.OutputArchive).GetAwaiter().GetResult()!=b.SourceHash)
                    throw new IOException("P4-R19 native physical ownership changed.");
                var pins=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
                    new(a.Source,a.SourceIdentity,a.SourceHash,a.SourceLength,a.SourceWriteTicks),
                    new(actualOutput,a.TargetIdentity,a.SourceHash,a.SourceLength),
                    new(b.Source,b.SourceIdentity,b.SourceHash,b.SourceLength,b.SourceWriteTicks),
                    new(b.OutputArchive,b.TargetIdentity,b.SourceHash,b.SourceLength,c.WriteTicks));
                if(!pins.IsFreshWitness || pins.IsDurableProof ||
                    pins.AuthorizesNativeOperation || pins.AuthorizesSelectiveUndo ||
                    Raw(_connection,"copy_items")!=ar || Raw(_connection,"move_items")!=br ||
                    Raw(_connection,"rename_items")!=cr || Raw(_connection,"reject_items")!=dr ||
                    M6JReadReview(b.Id)!=review || M6JDataVersion()!=version)
                    throw new IOException("P4-R19 native pinned predecessor or journal drift.");
                return new(true,"Time-local exact A ownership witnessed; no grant, replay or mutation permission.");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or NotSupportedException or
                JsonException or OverflowException or NullReferenceException)
            {
                return new(false,"P4-R19 read-only refusal ("+ex.GetType().Name+").");
            }
        }
    }
}
