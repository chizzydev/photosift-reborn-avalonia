using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

// P6F2D2: unmarked, *strictly generated* ordinary-folder second GUI checkpoint.
// Reuses the already migrated v16 reject_gui_second_session table, never the
// marked P5C fixture grant. This checkpoint itself NEVER authorizes Stage/Execute.
public sealed partial class WorkspaceSessionStore
{
    private string SecondOrdinaryRoot()
    {
        if(P5CGuiFixturePolicy.IsMarkedStore(this) || HasUnreconciledCopy ||
           AllMoveHistoryCount()!=0 || AllRenameHistoryCount()!=0)
            throw new IOException("Ordinary GUI cannot share marked fixture or other histories.");
        var state=Path.GetDirectoryName(DatabasePath)!;
        var root=Directory.GetParent(state)?.FullName??throw new IOException("No isolated root.");
        var envRoot=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_ORDINARY_GUI_PROBE_ROOT");
        var envHome=Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
        if(envRoot is null || envHome is null ||
           !SortingWorkspace.PathComparer.Equals(Path.GetFullPath(envRoot),root) ||
           !SortingWorkspace.PathComparer.Equals(Path.GetFullPath(envHome),state) ||
           !P6F2D2GeneratedScope.IsExactGeneratedRootName(Path.GetFileName(root)) ||
           SortingWorkspace.HasLinkedExistingAncestor(root) ||
           !Directory.Exists(Path.Combine(root,"source")) ||
           !Directory.Exists(Path.Combine(root,"output")) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.Combine(root,"source")) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.Combine(root,"output")) ||
           !File.Exists(Path.Combine(root,"P6F2D2_PROBE_SCOPE.json")) ||
           File.Exists(Path.Combine(root,"P5C_GUI_TEST_ONLY.marker")) ||
           File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")) ||
           Directory.EnumerateFileSystemEntries(Path.Combine(root,"output")).Count()!=1 ||
           Directory.EnumerateDirectories(Path.Combine(root,"source")).Any(dir=>
              !SortingWorkspace.PathComparer.Equals(dir,
                 Path.Combine(root,"source",".photosift-reborn-reject-recovery"))))
            throw new IOException("Only an isolated P6F2D2 generated two-photo root is allowed.");
        return root;
    }
    private (string First,string Second) SecondOrdinaryHashes(string root)
    {
        var manifest=Path.Combine(root,"P6F2D2_PROBE_SCOPE.json");
        var sentinel=Path.Combine(root,"output","PROTECTED_SENTINEL.txt");
        if(SortingWorkspace.HasSymlinkOrReparsePoint(manifest) ||
           SortingWorkspace.HasSymlinkOrReparsePoint(sentinel))
            throw new IOException("Second ordinary manifest linked.");
        using var json=JsonDocument.Parse(File.ReadAllText(manifest));
        var m=json.RootElement;
        if(m.GetProperty("schema").GetInt32()!=2 ||
           m.GetProperty("origin").GetString()!="P6F2D2_TWO_GENERATED_ONLY" ||
           m.GetProperty("sentinel").GetString()!=
               File.ReadAllText(Path.Combine(root,"output","PROTECTED_SENTINEL.txt")))
            throw new IOException("Second ordinary provenance/sentinel mismatch.");
        var h1=m.GetProperty("sha256_01").GetString()??"";
        var h2=m.GetProperty("sha256_02").GetString()??"";
        if(h1.Length!=64 || h2.Length!=64 || !h1.All(Uri.IsHexDigit) || !h2.All(Uri.IsHexDigit))
            throw new IOException("Second ordinary generated digests invalid.");
        P6F2D2GeneratedScope.RequireGeneratedDigests(h1,h2,
            m.GetProperty("sentinel").GetString()??"");
        // Native SHA-256 helpers return uppercase hex; generator writes lowercase.
        return (h1.ToUpperInvariant(),h2.ToUpperInvariant());
    }
    private static bool SecondOrdinarySnapshot(WorkspaceSessionSnapshot snap,string root,
        string firstPath,string secondPath,bool successor)
    {
        if(snap.Photos is null || snap.PendingRejectIds is not {Length:1} ||
           snap.Assignments is not {Length:0} || snap.Decisions is not {Length:0} ||
           snap.RemovedPhotos is {Length:>0} || snap.CollisionMode!=CollisionChoice.Refuse ||
           snap.SourceRoots is not {Length:1} ||
           !SortingWorkspace.PathComparer.Equals(snap.SourceRoot,Path.Combine(root,"source")) ||
           !SortingWorkspace.PathComparer.Equals(snap.SourceRoots[0],snap.SourceRoot) ||
           !SortingWorkspace.PathComparer.Equals(snap.DestinationRoot,Path.Combine(root,"output")) ||
           snap.Index!=(successor ? 0 : 1)) return false;
        if(successor)
            return snap.Photos.Length==1 &&
                SortingWorkspace.PathComparer.Equals(snap.Photos[0].Path,secondPath) &&
                snap.PendingRejectIds[0]==snap.Photos[0].Id;
        return snap.Photos.Length==2 &&
            SortingWorkspace.PathComparer.Equals(snap.Photos[0].Path,firstPath) &&
            SortingWorkspace.PathComparer.Equals(snap.Photos[1].Path,secondPath) &&
            snap.PendingRejectIds[0]==snap.Photos[0].Id;
    }
    public SecondRejectGuiEvidence InspectSecondOrdinaryRejectGuiSession(WorkspaceSessionSnapshot live)
    {
        lock(_dbGate)
        {
            SecondRejectBinding? binding=null;
            try
            {
                binding=ReadSecondRejectBinding();
                if(binding is null)return new(false,false,Guid.Empty,"No second ordinary GUI checkpoint.");
                var root=SecondOrdinaryRoot();
                var source=Path.Combine(root,"source");
                var firstPath=Path.Combine(source,"photo_01.png");
                var secondPath=Path.Combine(source,"photo_02.png");
                var hashes=SecondOrdinaryHashes(root);
                var first=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(binding.PriorJson)
                    ??throw new IOException("Missing prior GUI checkpoint.");
                var next=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(binding.NextJson)
                    ??throw new IOException("Missing successor GUI checkpoint.");
                var saved=Load()??throw new IOException("Missing saved GUI checkpoint.");
                var rows=RejectItems();
                if(!Guid.TryParseExact(binding.Next,"N",out var nextId) ||
                   !Guid.TryParseExact(binding.Prior,"N",out var priorId) ||
                   !Guid.TryParseExact(binding.Photo,"N",out var photoId) ||
                   priorId==nextId || first.SessionId!=priorId || next.SessionId!=nextId ||
                   live.SessionId!=nextId || saved.SessionId!=nextId ||
                   !EquivalentRestoredCheckpoint(live,saved) ||
                   JsonSerializer.Serialize(saved)!=binding.NextJson ||
                   JsonSerializer.Serialize(first)!=binding.PriorJson ||
                   JsonSerializer.Serialize(next)!=binding.NextJson ||
                   !SecondOrdinarySnapshot(first,root,firstPath,secondPath,false) ||
                   !SecondOrdinarySnapshot(next,root,firstPath,secondPath,true) ||
                   binding.Installation!=RejectInstallId().ToString("N") ||
                   !SortingWorkspace.PathComparer.Equals(binding.Path,secondPath) ||
                   next.Photos[0].Id!=photoId || next.Photos[0].NativeIdentity!=binding.Identity ||
                   next.Photos[0].Length!=binding.Length ||
                   next.Photos[0].LastWriteUtcTicks!=binding.Ticks ||
                   !string.Equals(binding.Hash,hashes.Second,StringComparison.OrdinalIgnoreCase) ||
                   first.Photos[1].Id!=photoId ||
                   first.Photos[1].NativeIdentity!=binding.Identity ||
                   rows.Count is <1 or >2 || rows[0].State!="Undone" ||
                   rows[0].SessionId!=priorId || rows[0].PhotoId!=first.Photos[0].Id ||
                   !SortingWorkspace.PathComparer.Equals(rows[0].Source,firstPath) ||
                   !string.Equals(rows[0].SourceHash,hashes.First,StringComparison.OrdinalIgnoreCase) ||
                   (rows.Count==2 && (rows[1].SessionId!=nextId ||
                       rows[1].PhotoId!=photoId ||
                       !SortingWorkspace.PathComparer.Equals(rows[1].Source,secondPath))) ||
                   ((rows.Count==1 || rows[1].State is "Staged" or "Approved" or "Undone") &&
                        !SecondSourceProven(binding)))
                    throw new IOException("Ordinary second GUI identity/snapshot or native source changed.");
                var receipt=InspectOrdinaryRejectTerminalReceipts(1);
                var all=ReadRejectReceipts();
                if(!receipt.Verified || receipt.Sealed<1 || all.Count<1 ||
                   all[0].Digest!=binding.Head ||
                   !File.Exists(firstPath) ||
                   SortingWorkspace.HasSymlinkOrReparsePoint(firstPath) ||
                   NativeFileCapability.Identity(firstPath)!=rows[0].SourceIdentity ||
                   HashFile(firstPath)!=hashes.First)
                    throw new IOException("Prior restored original or immutable receipt changed.");
                return new(true,true,nextId,"Second generated ordinary checkpoint and historical native receipt verified.");
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or OverflowException or JsonException or
                SqliteException or InvalidCastException or KeyNotFoundException)
            { return new(binding is not null,false,Guid.Empty,"Ordinary second GUI REFUSED: "+ex.Message); }
        }
    }
    public WorkspaceSessionSnapshot BeginSecondOrdinaryRejectGuiSession(
        WorkspaceSessionSnapshot before,bool explicitlyConfirmed)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(!explicitlyConfirmed || ReadSecondRejectBinding() is not null)
                throw new IOException("An explicit unused ordinary GUI transition is required.");
            var root=SecondOrdinaryRoot();
            var hashes=SecondOrdinaryHashes(root);
            var firstPath=Path.Combine(root,"source","photo_01.png");
            var secondPath=Path.Combine(root,"source","photo_02.png");
            var rows=RejectItems();
            var saved=Load()??throw new IOException("Ordinary GUI snapshot missing.");
            if(rows.Count!=1 || rows[0].State!="Undone" ||
               rows[0].SessionId!=saved.SessionId ||
               !SecondOrdinarySnapshot(saved,root,firstPath,secondPath,false) ||
               !EquivalentRestoredCheckpoint(before,saved) ||
               rows[0].PhotoId!=saved.Photos[0].Id ||
               !SortingWorkspace.PathComparer.Equals(rows[0].Source,firstPath) ||
               !string.Equals(rows[0].SourceHash,hashes.First,StringComparison.OrdinalIgnoreCase) ||
               !File.Exists(firstPath) ||
               NativeFileCapability.Identity(firstPath)!=rows[0].SourceIdentity ||
               HashFile(firstPath)!=hashes.First)
                throw new IOException("First generated original, checkpoint, or Undo changed.");
            var second=saved.Photos[1];
            if(second.Id==rows[0].PhotoId || second.NativeIdentity is null ||
               !File.Exists(secondPath) || SortingWorkspace.HasSymlinkOrReparsePoint(secondPath) ||
               SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(secondPath)!) ||
               NativeFileCapability.Identity(secondPath)!=second.NativeIdentity ||
               new FileInfo(secondPath).Length!=second.Length ||
               File.GetLastWriteTimeUtc(secondPath).Ticks!=second.LastWriteUtcTicks ||
               HashFile(secondPath)!=hashes.Second)
                throw new IOException("Second distinct generated original is unproven.");
            var sealedHistory=SealOrdinaryRejectTerminalHistory();
            if(!sealedHistory.Verified || sealedHistory.Sealed!=1 || sealedHistory.Historical!=1)
                throw new IOException("First native Reject terminal receipt cannot be verified.");
            var next=saved with {SessionId=Guid.NewGuid(),Photos=new[]{second},
                Assignments=Array.Empty<SavedAssignment>(),Decisions=Array.Empty<SavedDecision>(),
                RemovedPhotos=Array.Empty<RemovedSessionPhoto>(),PendingRejectIds=new[]{second.Id},
                Index=0,Revision=checked(saved.Revision+2),CollisionMode=CollisionChoice.Refuse};
            var check=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            check.RestoreSession(next);
            string beforeJson=JsonSerializer.Serialize(saved),nextJson=JsonSerializer.Serialize(next);
            using var tx=_connection.BeginTransaction();
            if(ReadSecondRejectBinding(tx) is not null || ReadRejectBytes(tx).Count!=1)
                throw new IOException("Reject history changed during ordinary GUI transition.");
            var receipts=VerifyRejectTerminalReceipts(tx,true,Path.Combine(root,"source"));
            if(receipts.Count!=1 || receipts.Head!=sealedHistory.Head)
                throw new IOException("Native receipt changed before ordinary GUI checkpoint.");
            using(var cmd=_connection.CreateCommand())
            {
                cmd.Transaction=tx;
                cmd.CommandText="UPDATE workspace_checkpoint SET json=$next WHERE id=1 AND json=$prior AND copy_risk=0";
                cmd.Parameters.AddWithValue("$next",nextJson);
                cmd.Parameters.AddWithValue("$prior",beforeJson);
                if(cmd.ExecuteNonQuery()!=1)
                    throw new IOException("Saved GUI revision changed; ordinary transition refused.");
            }
            using(var cmd=_connection.CreateCommand())
            {
                cmd.Transaction=tx;
                cmd.CommandText="INSERT INTO reject_gui_second_session(id,installation,prior_session,next_session,"+
                    "prior_snapshot,next_snapshot,receipt_head,candidate_photo_id,candidate_path,"+
                    "candidate_identity,candidate_hash,candidate_length,candidate_ticks) "+
                    "VALUES(1,$install,$prior,$next,$before,$after,$head,$photo,$path,$identity,$hash,$length,$ticks)";
                cmd.Parameters.AddWithValue("$install",RejectInstallId(tx).ToString("N"));
                cmd.Parameters.AddWithValue("$prior",saved.SessionId.ToString("N"));
                cmd.Parameters.AddWithValue("$next",next.SessionId.ToString("N"));
                cmd.Parameters.AddWithValue("$before",beforeJson);
                cmd.Parameters.AddWithValue("$after",nextJson);
                cmd.Parameters.AddWithValue("$head",receipts.Head);
                cmd.Parameters.AddWithValue("$photo",second.Id.ToString("N"));
                cmd.Parameters.AddWithValue("$path",second.Path);
                cmd.Parameters.AddWithValue("$identity",second.NativeIdentity);
                cmd.Parameters.AddWithValue("$hash",hashes.Second);
                cmd.Parameters.AddWithValue("$length",second.Length);
                cmd.Parameters.AddWithValue("$ticks",second.LastWriteUtcTicks);
                if(cmd.ExecuteNonQuery()!=1)throw new IOException("Ordinary GUI checkpoint not inserted.");
            }
            tx.Commit();
            return next;
        }
    }
}
