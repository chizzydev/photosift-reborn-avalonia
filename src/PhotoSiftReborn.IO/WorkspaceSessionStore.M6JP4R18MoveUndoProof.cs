using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>P4-R18: ephemeral stage observation only. Never an independent grant,
/// epoch rollover, ordinary-photo authority, or automatic-replay permission.</summary>
internal sealed record M6JP4R18MoveObservation(bool IsFreshWitness,string Reason)
{
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool IsDurableProof => false;
    public bool CanReplay => false;
}

public sealed partial class WorkspaceSessionStore
{
    internal M6JP4R18MoveObservation InspectM6JP4R18BStage(
        Guid session,string journalState,string physicalState)
    {
        lock(_dbGate)
        {
            try
            {
                M6JP4R12GeneratedRenameReject.RequireFixture(this);
                if(session==Guid.Empty || journalState is not
                    ("Committed" or "UndoRestoreIntent" or "SourceRestored" or
                     "UndoOutputArchiveIntent" or "Undone") ||
                    physicalState is not ("BeforeRestore" or "AfterRestore" or "AfterOutputArchive") ||
                    AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                    AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                    CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy)
                    throw new IOException("P4-R18 unexpected four-family history or stage.");
                static (string Id,string Session,string Json) Raw(
                    Microsoft.Data.Sqlite.SqliteConnection cn,string table)
                {
                    if(table is not ("copy_items" or "move_items" or "rename_items" or "reject_items"))
                        throw new IOException("Unrecognized journal family.");
                    using var cmd=cn.CreateCommand();
                    cmd.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                    using var reader=cmd.ExecuteReader();
                    if(!reader.Read())throw new IOException("History missing.");
                    var row=(reader.GetString(0),reader.GetString(1),reader.GetString(2));
                    if(reader.Read() || row.Item1.Length!=32 || row.Item2.Length!=32 || row.Item3.Length>32768)
                        throw new IOException("Invalid journal row identity or size.");
                    return row;
                }
                long version=M6JDataVersion();
                var ar=Raw(_connection,"copy_items");
                var br=Raw(_connection,"move_items");
                var cr=Raw(_connection,"rename_items");
                var dr=Raw(_connection,"reject_items");
                var a=JsonSerializer.Deserialize<DurableCopyItem>(ar.Json)
                    ?? throw new IOException("Copy JSON invalid.");
                var b=JsonSerializer.Deserialize<DurableMoveItem>(br.Json)
                    ?? throw new IOException("Move JSON invalid.");
                var c=JsonSerializer.Deserialize<DurableRenameItem>(cr.Json)
                    ?? throw new IOException("Rename JSON invalid.");
                var d=JsonSerializer.Deserialize<DurableRejectItem>(dr.Json)
                    ?? throw new IOException("Reject JSON invalid.");
                bool Row(Guid id,Guid owner,(string Id,string Session,string Json) r)
                    => id!=Guid.Empty && owner==session && r.Id==id.ToString("N") &&
                       r.Session==owner.ToString("N");
                var install=MoveInstallationId();
                if(!Row(a.Id,a.SessionId,ar) || !Row(b.Id,b.SessionId,br) ||
                   !Row(c.Id,c.SessionId,cr) || !Row(d.Id,d.SessionId,dr) ||
                   b.InstallationId!=install || c.InstallationId!=install ||
                   d.InstallationId!=install || a.State!="Committed" ||
                   b.State!=journalState || c.State!="Undone" || d.State!="Undone" ||
                   a.Error is not null || b.Error is not null ||
                   c.Error is not null || d.Error is not null ||
                   a.PhotoId==b.PhotoId || b.PhotoId!=c.PhotoId || c.PhotoId!=d.PhotoId ||
                   b.TargetIdentity is null || b.TempIdentity is null ||
                   a.TargetIdentity is null || b.Destination!=c.Source ||
                   b.TargetIdentity!=c.NativeIdentity || c.Target!=d.Source ||
                   c.NativeIdentity!=d.SourceIdentity ||
                   b.SourceHash!=c.Hash || c.Hash!=d.SourceHash ||
                   b.SourceLength!=c.Length || c.Length!=d.SourceLength ||
                   c.WriteTicks!=d.SourceWriteTicks ||
                   c.ReviewedDirectoryIdentity is null ||
                   d.SessionRevision!=c.Revision)
                    throw new IOException("P4-R18 four-family historical lineage invalid.");
                var review=M6JReadReview(b.Id)
                    ?? throw new IOException("P4-R18 consumed Move review absent.");
                if(review.State!="Consumed" || review.Family!="Move" ||
                   review.Installation!=install.ToString("N") ||
                   review.Session!=session.ToString("N") ||
                   review.ReviewDigest!=M6JReviewDigest(review.CandidateDigest,
                       review.HistoryDigest,review.CheckpointDigest))
                    throw new IOException("P4-R18 Move approval seal changed.");
                var plan=M6JDecodeStored(review);
                if(plan.Family!="Move" || plan.Id!=b.Id || plan.SessionId!=session ||
                   plan.PhotoId!=b.PhotoId || plan.Source!=b.Source ||
                   plan.Destination!=b.Destination || plan.SourceIdentity!=b.SourceIdentity ||
                   plan.SourceHash!=b.SourceHash || plan.SourceLength!=b.SourceLength ||
                   plan.SourceWriteTicks!=b.SourceWriteTicks)
                    throw new IOException("P4-R18 originally reviewed Move plan changed.");
                var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach(string s in new[]{ar.Id,ar.Session,ar.Json})
                    history.Append(s.Length).Append(':').Append(s);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                if(M6JHash(history.ToString())!=review.HistoryDigest)
                    throw new IOException("P4-R18 reviewed Copy history changed.");
                string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                string sourceParent=Path.Combine(root,"source");
                string outputParent=Path.GetDirectoryName(b.Destination)!;
                if(!SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(outputParent),
                    Path.Combine(root,"output")))
                    throw new IOException("P4-R18 destination is outside generated output hierarchy.");
                string operation=Path.Combine(sourceParent,".photosift-reborn-recovery",
                    b.InstallationId.ToString("N"),b.SessionId.ToString("N"),b.Id.ToString("N"));
                string recoveryRoot=Path.Combine(sourceParent,".photosift-reborn-recovery");
                string ownedRootMarker=Path.Combine(recoveryRoot,".photosift-move-owner-v1");
                string ownedOpMarker=Path.Combine(operation,".photosift-operation-v1");
                static bool Empty(string p)=>!File.Exists(p) && !Directory.Exists(p);
                if(!Directory.Exists(sourceParent) || !Directory.Exists(outputParent) ||
                   !Directory.Exists(operation) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.Source),sourceParent) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.Destination),outputParent) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.OriginalArchive),operation) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.OutputArchive),operation) ||
                   !SortingWorkspace.PathComparer.Equals(b.OriginalArchive,Path.Combine(operation,Path.GetFileName(b.Source))) ||
                   !SortingWorkspace.PathComparer.Equals(b.OutputArchive,Path.Combine(operation,
                       "installed-"+Path.GetFileName(b.Destination))) ||
                   !SortingWorkspace.PathComparer.Equals(b.Temp,Path.Combine(outputParent,
                       ".photosift-move-"+b.Id.ToString("N")+".part")) ||
                   !SortingWorkspace.PathComparer.Equals(c.Source,b.Destination) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(d.Archive),outputParent) ||
                   !File.Exists(ownedRootMarker) || !File.Exists(ownedOpMarker) ||
                   SortingWorkspace.HasSymlinkOrReparsePoint(ownedRootMarker) ||
                   SortingWorkspace.HasSymlinkOrReparsePoint(ownedOpMarker) ||
                   SortingWorkspace.HasLinkedExistingAncestor(operation) ||
                   SortingWorkspace.HasLinkedExistingAncestor(sourceParent) ||
                   SortingWorkspace.HasLinkedExistingAncestor(outputParent) ||
                   File.ReadAllText(ownedRootMarker)!="PhotoSift Reborn Move v1:"+install.ToString("N")+"\n" ||
                   File.ReadAllText(ownedOpMarker)!="PhotoSift Reborn Move operation "+b.Id.ToString("N")+"\n" ||
                   NativeFileCapability.DirectoryIdentity(outputParent)!=c.ReviewedDirectoryIdentity ||
                   NativeFileCapability.DeviceId(sourceParent)!=NativeFileCapability.DeviceId(operation) ||
                   NativeFileCapability.DeviceId(outputParent)!=NativeFileCapability.DeviceId(operation) ||
                   !Empty(c.Target) || !Empty(d.Archive) || !Empty(b.Temp) ||
                   !Empty(a.Temp) || !Empty(a.Recovery))
                    throw new IOException("P4-R18 frozen paths, markers, or recovery parent invalid.");
                string original=physicalState=="BeforeRestore"?b.OriginalArchive:b.Source;
                string result=physicalState=="AfterOutputArchive"?b.OutputArchive:b.Destination;
                string absentOriginal=physicalState=="BeforeRestore"?b.Source:b.OriginalArchive;
                string absentResult=physicalState=="AfterOutputArchive"?b.Destination:b.OutputArchive;
                if(!Empty(absentOriginal) || !Empty(absentResult) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(original) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(result) ||
                   NativeFileCapability.Identity(original)!=b.SourceIdentity ||
                   NativeFileCapability.Identity(result)!=b.TargetIdentity ||
                   new FileInfo(original).Length!=b.SourceLength ||
                   new FileInfo(original).LastWriteTimeUtc.Ticks!=b.SourceWriteTicks ||
                   new FileInfo(result).Length!=b.SourceLength ||
                   new FileInfo(result).LastWriteTimeUtc.Ticks!=c.WriteTicks ||
                   SortingWorkspace.ComputeSha256Async(original).GetAwaiter().GetResult()!=b.SourceHash ||
                   SortingWorkspace.ComputeSha256Async(result).GetAwaiter().GetResult()!=b.SourceHash)
                    throw new IOException("P4-R18 current Move native evidence not owned.");
                var pinned=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
                    new(a.Source,a.SourceIdentity,a.SourceHash,a.SourceLength,a.SourceWriteTicks),
                    new(a.Destination,a.TargetIdentity,a.SourceHash,a.SourceLength),
                    new(original,b.SourceIdentity,b.SourceHash,b.SourceLength,b.SourceWriteTicks),
                    new(result,b.TargetIdentity,b.SourceHash,b.SourceLength,
                        new FileInfo(result).LastWriteTimeUtc.Ticks));
                if(!pinned.IsFreshWitness || pinned.IsDurableProof ||
                   pinned.AuthorizesNativeOperation || pinned.AuthorizesSelectiveUndo ||
                   Raw(_connection,"copy_items")!=ar || Raw(_connection,"move_items")!=br ||
                   Raw(_connection,"rename_items")!=cr || Raw(_connection,"reject_items")!=dr ||
                   M6JReadReview(b.Id)!=review || M6JDataVersion()!=version)
                    throw new IOException("P4-R18 concurrent predecessor/native history drifted.");
                return new(true,"Time-local exact B four-family ownership witnessed; no authorization granted.");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or NotSupportedException or
                JsonException or OverflowException or NullReferenceException)
            {
                return new(false,"P4-R18 read-only stage refusal ("+ex.GetType().Name+").");
            }
        }
    }
}
