using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>A one-time current-native observation, never a transferable consent token.</summary>
internal sealed record M6JP4R23CopyAfterMoveObservation(bool IsFreshWitness,string Reason)
{
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool IsDurableProof => false;
    public bool CanReplay => false;
}

public sealed partial class WorkspaceSessionStore
{
    /// <summary>Only one generated Copy A plus one originally reviewed, durably
    /// Undone Move B. All other history, including undone third/fourth families,
    /// prevents this narrow path. No read observation alone authorizes mutation.</summary>
    internal M6JP4R23CopyAfterMoveObservation InspectM6JP4R23AStage(
        Guid session,string journalState,string physicalState)
    {
        lock(_dbGate)
        {
            try
            {
                M6JP4R12GeneratedRenameReject.RequireFixture(this);
                if(session==Guid.Empty || !NativeFileCapability.IsSupported ||
                   journalState is not ("Committed" or "UndoIntent" or "Undone") ||
                   physicalState is not ("BeforeArchive" or "AfterArchive") ||
                   (journalState=="Committed" && physicalState!="BeforeArchive") ||
                   (journalState=="Undone" && physicalState!="AfterArchive") ||
                   AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                   AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
                   CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy || HasAnyCopySuccessorGrant)
                    throw new IOException("R23 requires exactly one Copy and one terminal Move, no other history.");
                static (string Id,string Session,string Json) Raw(
                    Microsoft.Data.Sqlite.SqliteConnection cn,string table)
                {
                    if(table is not ("copy_items" or "move_items"))
                        throw new IOException("R23 unexpected journal table.");
                    using var cmd=cn.CreateCommand();
                    cmd.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                    using var rd=cmd.ExecuteReader();
                    if(!rd.Read())throw new IOException("R23 journal missing.");
                    var row=(rd.GetString(0),rd.GetString(1),rd.GetString(2));
                    if(rd.Read() || row.Item1.Length!=32 || row.Item2.Length!=32 || row.Item3.Length>32768)
                        throw new IOException("R23 ambiguous journal identity, size, or count.");
                    return row;
                }
                long version=M6JDataVersion();
                var ar=Raw(_connection,"copy_items");
                var br=Raw(_connection,"move_items");
                var a=JsonSerializer.Deserialize<DurableCopyItem>(ar.Json)
                    ?? throw new IOException("R23 Copy journal unreadable.");
                var b=JsonSerializer.Deserialize<DurableMoveItem>(br.Json)
                    ?? throw new IOException("R23 Move journal unreadable.");
                var installation=MoveInstallationId();
                if(a.Id==Guid.Empty || b.Id==Guid.Empty || a.Id==b.Id ||
                   a.SessionId!=session || b.SessionId!=session || a.PhotoId==b.PhotoId ||
                   ar.Id!=a.Id.ToString("N") || ar.Session!=session.ToString("N") ||
                   br.Id!=b.Id.ToString("N") || br.Session!=session.ToString("N") ||
                   a.State!=journalState || b.State!="Undone" ||
                   a.Error is not null || b.Error is not null ||
                   a.TargetIdentity is null || b.TargetIdentity is null ||
                   b.InstallationId!=installation || a.SourceIdentity==b.SourceIdentity ||
                   a.TargetIdentity==b.TargetIdentity ||
                   a.SourceIdentity==a.TargetIdentity || b.SourceIdentity==b.TargetIdentity)
                    throw new IOException("R23 terminal pair identity or state invalid.");

                // Verify original independently reviewed Move candidate AND the
                // immutable raw Copy history sealed before the Move intent. Only
                // Copy.State may differ during this exact R23 undo transition.
                var review=M6JReadReview(b.Id)
                    ?? throw new IOException("R23 consumed Move review missing.");
                if(review.State!="Consumed" || review.Family!="Move" ||
                   review.Installation!=installation.ToString("N") ||
                   review.Session!=session.ToString("N") ||
                   review.ReviewDigest!=M6JReviewDigest(review.CandidateDigest,
                       review.HistoryDigest,review.CheckpointDigest))
                    throw new IOException("R23 consumed reviewed Move seal invalid.");
                var reviewed=M6JDecodeStored(review);
                if(reviewed.Family!="Move" || reviewed.Id!=b.Id ||
                   reviewed.SessionId!=session || reviewed.PhotoId!=b.PhotoId ||
                   reviewed.Source!=b.Source || reviewed.Destination!=b.Destination ||
                   reviewed.SourceIdentity!=b.SourceIdentity ||
                   reviewed.SourceHash!=b.SourceHash || reviewed.SourceLength!=b.SourceLength ||
                   reviewed.SourceWriteTicks!=b.SourceWriteTicks)
                    throw new IOException("R23 frozen Move review does not match the terminal journal.");
                var original=JsonSerializer.Serialize(a with {State="Committed"});
                var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach(string item in new[]{ar.Id,ar.Session,original})
                    history.Append(item.Length).Append(':').Append(item);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                if(M6JHash(history.ToString())!=review.HistoryDigest)
                    throw new IOException("R23 reviewed Copy row has changed.");

                string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                string source=Path.Combine(root,"source");
                string output=Path.Combine(root,"output");
                string copyRecoveryRoot=Path.Combine(root,".photosift-reborn-recovery");
                string expectedCopyRecovery=Path.Combine(copyRecoveryRoot,session.ToString("N"),
                    a.Id.ToString("N"),Path.GetFileName(a.Destination));
                string op=Path.Combine(source,".photosift-reborn-recovery",
                    installation.ToString("N"),session.ToString("N"),b.Id.ToString("N"));
                string expectedBArchive=Path.Combine(op,"installed-"+Path.GetFileName(b.Destination));
                string expectedBOriginalArchive=Path.Combine(op,Path.GetFileName(b.Source));
                string moveRootMarker=Path.Combine(source,".photosift-reborn-recovery",
                    ".photosift-move-owner-v1");
                string moveOperationMarker=Path.Combine(op,".photosift-operation-v1");
                static bool Empty(string path)=>!File.Exists(path) && !Directory.Exists(path);
                if(!SortingWorkspace.PathComparer.Equals(a.Recovery,expectedCopyRecovery) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(a.Source),source) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.Source),source) ||
                   !SortingWorkspace.PathComparer.Equals(b.OutputRoot,output) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.OutputArchive),op) ||
                   !SortingWorkspace.PathComparer.Equals(b.OutputArchive,expectedBArchive) ||
                   !SortingWorkspace.PathComparer.Equals(b.OriginalArchive,expectedBOriginalArchive) ||
                   !SortingWorkspace.PathComparer.Equals(b.Temp,Path.Combine(
                       Path.GetDirectoryName(b.Destination)!,".photosift-move-"+b.Id.ToString("N")+".part")) ||
                   !a.Destination.StartsWith(output+Path.DirectorySeparatorChar,SortingWorkspace.PathComparison) ||
                   !b.Destination.StartsWith(output+Path.DirectorySeparatorChar,SortingWorkspace.PathComparison) ||
                   !Directory.Exists(source) || !Directory.Exists(output) || !Directory.Exists(op) ||
                   SortingWorkspace.HasLinkedExistingAncestor(source) ||
                   SortingWorkspace.HasLinkedExistingAncestor(output) ||
                   SortingWorkspace.HasLinkedExistingAncestor(op) ||
                   !File.Exists(moveRootMarker) || !File.Exists(moveOperationMarker) ||
                   SortingWorkspace.HasSymlinkOrReparsePoint(moveRootMarker) ||
                   SortingWorkspace.HasSymlinkOrReparsePoint(moveOperationMarker) ||
                   File.ReadAllText(moveRootMarker)!="PhotoSift Reborn Move v1:"+installation.ToString("N")+"\n" ||
                   File.ReadAllText(moveOperationMarker)!="PhotoSift Reborn Move operation "+b.Id.ToString("N")+"\n" ||
                   !Empty(a.Temp) || !Empty(b.Temp) || !Empty(b.Destination) ||
                   !Empty(b.OriginalArchive))
                    throw new IOException("R23 paths, recovery owner markers or undo terminal layout changed.");
                if(Directory.Exists(copyRecoveryRoot))
                {
                    string marker=Path.Combine(copyRecoveryRoot,".photosift-owner-v1");
                    if(SortingWorkspace.HasLinkedExistingAncestor(copyRecoveryRoot) ||
                       !File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
                       File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
                        throw new IOException("R23 Copy recovery root belongs to another owner.");
                }
                string active=physicalState=="AfterArchive"?a.Recovery:a.Destination;
                string absent=physicalState=="AfterArchive"?a.Destination:a.Recovery;
                var four=new[]{a.Source,active,b.Source,b.OutputArchive};
                if(four.Distinct(SortingWorkspace.PathComparer).Count()!=4 ||
                   !Empty(absent) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(a.Source) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(active) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(b.Source) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(b.OutputArchive) ||
                   NativeFileCapability.Identity(a.Source)!=a.SourceIdentity ||
                   NativeFileCapability.Identity(active)!=a.TargetIdentity ||
                   NativeFileCapability.Identity(b.Source)!=b.SourceIdentity ||
                   NativeFileCapability.Identity(b.OutputArchive)!=b.TargetIdentity ||
                   new FileInfo(a.Source).Length!=a.SourceLength ||
                   new FileInfo(a.Source).LastWriteTimeUtc.Ticks!=a.SourceWriteTicks ||
                   new FileInfo(active).Length!=a.SourceLength ||
                   new FileInfo(active).LastWriteTimeUtc.Ticks!=a.TargetWriteTicks ||
                   new FileInfo(b.Source).Length!=b.SourceLength ||
                   new FileInfo(b.Source).LastWriteTimeUtc.Ticks!=b.SourceWriteTicks ||
                   new FileInfo(b.OutputArchive).Length!=b.SourceLength ||
                   SortingWorkspace.ComputeSha256Async(a.Source).GetAwaiter().GetResult()!=a.SourceHash ||
                   SortingWorkspace.ComputeSha256Async(active).GetAwaiter().GetResult()!=a.SourceHash ||
                   SortingWorkspace.ComputeSha256Async(b.Source).GetAwaiter().GetResult()!=b.SourceHash ||
                   SortingWorkspace.ComputeSha256Async(b.OutputArchive).GetAwaiter().GetResult()!=b.SourceHash)
                    throw new IOException("R23 original/installed native ownership is no longer current.");
                var pinned=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
                    new(a.Source,a.SourceIdentity,a.SourceHash,a.SourceLength,a.SourceWriteTicks),
                    new(active,a.TargetIdentity,a.SourceHash,a.SourceLength),
                    new(b.Source,b.SourceIdentity,b.SourceHash,b.SourceLength,b.SourceWriteTicks),
                    new(b.OutputArchive,b.TargetIdentity,b.SourceHash,b.SourceLength));
                if(!pinned.IsFreshWitness || pinned.IsDurableProof ||
                   pinned.AuthorizesNativeOperation || pinned.AuthorizesSelectiveUndo ||
                   Raw(_connection,"copy_items")!=ar || Raw(_connection,"move_items")!=br ||
                   M6JReadReview(b.Id)!=review || M6JDataVersion()!=version)
                    throw new IOException("R23 pinned ownership or raw journal changed during observation.");
                return new(true,"Exact reviewed Copy/terminal Move native paths witnessed once; no reusable authority.");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or NotSupportedException or
                JsonException or OverflowException or NullReferenceException)
            {
                return new(false,"R23 read-only refusal ("+ex.GetType().Name+").");
            }
        }
    }
}
