using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

// P6E2B: an exact, immutable GUI successor checkpoint. It is NOT a native
// Reject grant. StageSuccessor still requires a distinct separate click and
// its own single-use receipt-bound grant; Approve/Execute/Undo stay separate.
public sealed record SecondRejectGuiEvidence(bool Present,bool Verified,
    Guid SessionId,string Reason);

public sealed partial class WorkspaceSessionStore
{
    private sealed record SecondRejectBinding(string Installation,string Prior,string Next,
        string PriorJson,string NextJson,string Head,string Photo,string Path,
        string Identity,string Hash,long Length,long Ticks);

    private SecondRejectBinding? ReadSecondRejectBinding(SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT installation,prior_session,next_session,prior_snapshot,next_snapshot,"+
            "receipt_head,candidate_photo_id,candidate_path,candidate_identity,candidate_hash,"+
            "candidate_length,candidate_ticks FROM reject_gui_second_session WHERE id=1";
        using var read=cmd.ExecuteReader();
        if(!read.Read())return null;
        return new SecondRejectBinding(read.GetString(0),read.GetString(1),read.GetString(2),
            read.GetString(3),read.GetString(4),read.GetString(5),read.GetString(6),
            read.GetString(7),read.GetString(8),read.GetString(9),read.GetInt64(10),read.GetInt64(11));
    }

    private static bool EquivalentRestoredCheckpoint(WorkspaceSessionSnapshot live,
        WorkspaceSessionSnapshot persisted)
    {
        if(live.Revision!=persisted.Revision &&
           !(persisted.Revision<long.MaxValue-1 && live.Revision==persisted.Revision+1))
            return false;
        return JsonSerializer.Serialize(live with {Revision=persisted.Revision})==
            JsonSerializer.Serialize(persisted);
    }

    private static bool SecondSourceProven(SecondRejectBinding binding)
    {
        if(!File.Exists(binding.Path) || SortingWorkspace.HasSymlinkOrReparsePoint(binding.Path) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(binding.Path)!))return false;
        var info=new FileInfo(binding.Path);
        return info.Length==binding.Length && info.LastWriteTimeUtc.Ticks==binding.Ticks &&
            NativeFileCapability.Identity(binding.Path)==binding.Identity &&
            HashFile(binding.Path)==binding.Hash;
    }

    /// <summary>Independent read-only check at startup and before every successor
    /// action. Does NOT repair a modified checkpoint or automatically replay files.</summary>
    public SecondRejectGuiEvidence InspectSecondRejectGuiSession(WorkspaceSessionSnapshot live)
    {
        lock(_dbGate)
        {
            SecondRejectBinding? binding=null;
            try
            {
                binding=ReadSecondRejectBinding();
                if(binding is null)return new SecondRejectGuiEvidence(false,false,Guid.Empty,
                    "No second generated Reject session has been authorized.");
                if(!Guid.TryParseExact(binding.Next,"N",out var next) || next==Guid.Empty)
                    throw new IOException("Invalid second GUI Reject session identity.");
                var saved=Load()??throw new IOException("Missing saved second Reject session.");
                var first=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(binding.PriorJson);
                var expected=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(binding.NextJson);
                var history=RejectItems();
                string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                string source=Path.Combine(root,"source");
                Guid prior=Guid.Empty,photoId=Guid.Empty;
                if(!P5CGuiFixturePolicy.IsMarkedStore(this) || HasUnreconciledCopy ||
                   binding.Installation!=RejectInstallId().ToString("N") ||
                   !Guid.TryParseExact(binding.Prior,"N",out prior) || prior==next ||
                   !Guid.TryParseExact(binding.Photo,"N",out photoId) ||
                   first is null || expected is null || first.Photos is null ||
                   first.PendingRejectIds is null || first.Assignments is null ||
                   expected.Photos is null || expected.PendingRejectIds is null ||
                   expected.Assignments is null || expected.Decisions is null ||
                   first.SessionId!=prior ||
                   expected.SessionId!=next || live.SessionId!=next ||
                   !EquivalentRestoredCheckpoint(live,saved) ||
                   JsonSerializer.Serialize(saved)!=binding.NextJson ||
                   first.Photos.Length!=3 || first.PendingRejectIds is not {Length:1} ||
                   first.Photos[0].Id!=first.PendingRejectIds[0] ||
                   Path.GetFileName(first.Photos[0].Path)!="photo_01.png" ||
                   expected.Photos.Length!=1 || expected.Photos[0].Id!=photoId ||
                   expected.PendingRejectIds is not {Length:1} ||
                   expected.PendingRejectIds[0]!=photoId ||
                   expected.Assignments.Length!=0 || expected.Decisions.Length!=0 ||
                   expected.RemovedPhotos is {Length:>0} || expected.CollisionMode!=CollisionChoice.Refuse ||
                   !SortingWorkspace.PathComparer.Equals(expected.SourceRoot,source) ||
                   !SortingWorkspace.PathComparer.Equals(expected.DestinationRoot,Path.Combine(root,"output")) ||
                   !SortingWorkspace.PathComparer.Equals(binding.Path,Path.Combine(source,"photo_02.png")) ||
                   first.Photos.Count(p=>p.Id==photoId &&
                       SortingWorkspace.PathComparer.Equals(p.Path,binding.Path))!=1 ||
                   expected.Photos[0].NativeIdentity!=binding.Identity ||
                   expected.Photos[0].Length!=binding.Length ||
                   expected.Photos[0].LastWriteUtcTicks!=binding.Ticks ||
                   ((history.Count==1 || history[1].State is "Staged" or "Approved" or "Undone") &&
                       !SecondSourceProven(binding)) ||
                   binding.Hash!=OriginalGeneratedFixtureHash(root,"photo_02.png") ||
                   history.Count is <1 or >2 || history[0].State!="Undone" ||
                   history[0].SessionId!=prior || history[0].PhotoId!=first.Photos[0].Id ||
                   (history.Count==2 && (history[1].SessionId!=next ||
                       history[1].PhotoId!=photoId ||
                       !SortingWorkspace.PathComparer.Equals(history[1].Source,binding.Path))))
                    throw new IOException("Second Reject session, source, or terminal history changed.");
                var receipts=InspectRejectTerminalReceipts(1);
                var receiptsAll=ReadRejectReceipts();
                if(!receipts.Verified || receipts.Sealed<1 ||
                   receiptsAll.Count<1 || receiptsAll[0].Digest!=binding.Head)
                    throw new IOException("Native predecessor Reject receipt changed.");
                return new SecondRejectGuiEvidence(true,true,next,
                    "Immutable second GUI session, native source and first terminal receipt verified.");
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or OverflowException or JsonException or
                SqliteException or InvalidCastException)
            {
                return new SecondRejectGuiEvidence(binding is not null,false,Guid.Empty,
                    "Second Reject session REFUSED: "+ex.Message);
            }
        }
    }

    /// <summary>Atomically moves the saved GUI checkpoint to a distinct
    /// one-photo REVIEW-ONLY successor after a verified native terminal seal.
    /// Never creates a Reject Intent or a new staged Reject journal row.</summary>
    public WorkspaceSessionSnapshot BeginSecondRejectGuiSession(WorkspaceSessionSnapshot before,
        bool explicitlyConfirmed)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(!explicitlyConfirmed || !P5CGuiFixturePolicy.IsMarkedStore(this) ||
               HasUnreconciledCopy || ReadSecondRejectBinding() is not null)
                throw new IOException("Second Reject session requires one explicit unused fixture transition.");
            var history=RejectItems();
            if(history.Count!=1 || history[0].State!="Undone" ||
               Path.GetFileName(history[0].Source)!="photo_01.png")
                throw new IOException("First native Reject has not completed independent Undo.");
            var saved=Load()??throw new IOException("Prior Reject checkpoint missing.");
            if(!EquivalentRestoredCheckpoint(before,saved) ||
               saved.SessionId!=history[0].SessionId ||
               saved.Photos is null || saved.Assignments is null || saved.Decisions is null ||
               saved.Photos.Length!=3 || saved.PendingRejectIds is not {Length:1} ||
               saved.PendingRejectIds[0]!=history[0].PhotoId ||
               saved.Photos[0].Id!=history[0].PhotoId ||
               saved.Assignments.Length!=0 || saved.Decisions.Length!=0 ||
               saved.RemovedPhotos is {Length:>0} || saved.CollisionMode!=CollisionChoice.Refuse)
                throw new IOException("First Undo GUI checkpoint is not an exact saved fixture state.");
            string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
            string source=Path.Combine(root,"source");
            var photo=saved.Photos.SingleOrDefault(p=>
                SortingWorkspace.PathComparer.Equals(p.Path,Path.Combine(source,"photo_02.png")))
                ??throw new IOException("Second generated photo missing from first saved import.");
            if(photo.Id==history[0].PhotoId || photo.NativeIdentity is null ||
               !File.Exists(photo.Path) || SortingWorkspace.HasSymlinkOrReparsePoint(photo.Path) ||
               SortingWorkspace.HasLinkedExistingAncestor(source) ||
               NativeFileCapability.Identity(photo.Path)!=photo.NativeIdentity ||
               new FileInfo(photo.Path).Length!=photo.Length ||
               File.GetLastWriteTimeUtc(photo.Path).Ticks!=photo.LastWriteUtcTicks ||
               HashFile(photo.Path)!=OriginalGeneratedFixtureHash(root,"photo_02.png"))
                throw new IOException("Second generated photo identity/hash is no longer original.");
            var sealedHistory=SealRejectTerminalHistory();
            if(!sealedHistory.Verified || sealedHistory.Sealed!=1 ||
               sealedHistory.Historical!=1)
                throw new IOException("First Reject native terminal receipt cannot be verified.");
            var next=saved with {SessionId=Guid.NewGuid(), Photos=new[]{photo},
                Assignments=Array.Empty<SavedAssignment>(),Decisions=Array.Empty<SavedDecision>(),
                RemovedPhotos=Array.Empty<RemovedSessionPhoto>(), PendingRejectIds=new[]{photo.Id},
                Index=0,Revision=checked(saved.Revision+2),CollisionMode=CollisionChoice.Refuse};
            var check=new SortingWorkspace {IdentityResolver=NativeFileCapability.Identity};
            check.RestoreSession(next); P5CGuiFixturePolicy.VerifyExactWorkspace(this,check);
            string beforeJson=JsonSerializer.Serialize(saved),nextJson=JsonSerializer.Serialize(next);
            using var tx=_connection.BeginTransaction();
            if(ReadSecondRejectBinding(tx) is not null)
                throw new IOException("Second Reject GUI binding already committed.");
            var receipts=VerifyRejectTerminalReceipts(tx,true);
            if(receipts.Count!=1 || receipts.Head!=sealedHistory.Head ||
               ReadRejectBytes(tx).Count!=1)
                throw new IOException("Native predecessor changed during GUI checkpoint transition.");
            using(var update=_connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText="UPDATE workspace_checkpoint SET json=$after WHERE id=1 "+
                    "AND json=$before AND copy_risk=0";
                update.Parameters.AddWithValue("$after",nextJson);
                update.Parameters.AddWithValue("$before",beforeJson);
                if(update.ExecuteNonQuery()!=1)
                    throw new IOException("Workspace changed before second Reject transition; no overwrite.");
            }
            using(var insert=_connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText="INSERT INTO reject_gui_second_session(id,installation,prior_session,"+
                    "next_session,prior_snapshot,next_snapshot,receipt_head,candidate_photo_id,"+
                    "candidate_path,candidate_identity,candidate_hash,candidate_length,candidate_ticks) "+
                    "VALUES(1,$install,$prior,$next,$before,$after,$head,$photo,$path,$identity,$hash,$length,$ticks)";
                insert.Parameters.AddWithValue("$install",RejectInstallId(tx).ToString("N"));
                insert.Parameters.AddWithValue("$prior",saved.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$next",next.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$before",beforeJson);
                insert.Parameters.AddWithValue("$after",nextJson);
                insert.Parameters.AddWithValue("$head",receipts.Head);
                insert.Parameters.AddWithValue("$photo",photo.Id.ToString("N"));
                insert.Parameters.AddWithValue("$path",photo.Path);
                insert.Parameters.AddWithValue("$identity",photo.NativeIdentity);
                insert.Parameters.AddWithValue("$hash",HashFile(photo.Path));
                insert.Parameters.AddWithValue("$length",photo.Length);
                insert.Parameters.AddWithValue("$ticks",photo.LastWriteUtcTicks);
                if(insert.ExecuteNonQuery()!=1)
                    throw new IOException("Second Reject GUI binding was not inserted.");
            }
            tx.Commit();return next;
        }
    }
}
