using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// R29 generated-only complete reverse of the *existing* reviewed A/B/C history:
/// C and B must already be terminal Undone.  No new operation or grant is issued.
/// The exact original committed A/B review is reconstructed solely to compare
/// the older R25 digest, never to manufacture a new review or rewrite history.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    private string R29OriginalTwoCopyDigestInTransaction(SqliteTransaction tx,
        DurableCopyItem currentA, DurableCopyItem terminalB)
    {
        var raw=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
        using(var cmd=_connection.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="SELECT id,session,json FROM copy_items ORDER BY id";
            using var rows=cmd.ExecuteReader();
            int count=0;
            while(rows.Read())
            {
                if(++count>2)throw new IOException("R29 excess Copy journals.");
                string id=rows.GetString(0),session=rows.GetString(1),json=rows.GetString(2);
                if(json.Length>32768)throw new IOException("R29 oversized Copy journal.");
                if(id==currentA.Id.ToString("N"))
                {
                    if(session!=currentA.SessionId.ToString("N") ||
                       json!=JsonSerializer.Serialize(currentA))
                        throw new IOException("R29 Copy A changed during source history witness.");
                    json=JsonSerializer.Serialize(currentA with {State="Committed"});
                }
                else if(id==terminalB.Id.ToString("N"))
                {
                    if(session!=terminalB.SessionId.ToString("N") ||
                       json!=JsonSerializer.Serialize(terminalB))
                        throw new IOException("R29 Copy B changed during source history witness.");
                    json=JsonSerializer.Serialize(terminalB with {State="Committed"});
                }
                else throw new IOException("R29 unreviewed Copy row.");
                foreach(var part in new[]{id,session,json})
                    raw.Append(part.Length).Append(':').Append(part);
                raw.Append('\n');
            }
            if(count!=2)throw new IOException("R29 original review predecessor set absent.");
        }
        raw.Append("move_items\nrename_items\nreject_items\n");
        return M6JHash(raw.ToString());
    }

    internal void RequireM6JP4R29FirstCopyUndoStage(DurableCopyItem a,string stage)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(this);
        lock(_dbGate)
        {
            if(a is null || a.Id==Guid.Empty || a.SessionId==Guid.Empty ||
               a.SourceIdentity is null || a.TargetIdentity is null ||
               a.SourceIdentity==a.TargetIdentity || a.Error is not null ||
               a.State is not ("Committed" or "UndoIntent" or "Undone") ||
               AllCopyHistoryCount()!=2 || AllMoveHistoryCount()!=1 ||
               AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
               CountAuxiliaryHistoryRows()!=1 || HasUnreconciledCopy)
                throw new IOException("R29 exact terminal three-operation history missing.");
            var copies=CopyItems(a.SessionId);
            if(copies.Count!=2 || copies[0]!=a || copies[1].State!="Undone" ||
               copies[1].Error is not null || copies[1].Id==a.Id ||
               copies[1].PhotoId==a.PhotoId || copies[1].TargetIdentity==a.TargetIdentity ||
               copies[1].SourceIdentity==a.SourceIdentity)
                throw new IOException("R29 Copy A/B terminal lineage changed.");
            var b=copies[1];
            var moves=MoveItems(a.SessionId);
            if(moves.Count!=1 || moves[0].State!="Undone" ||
               moves[0].Error is not null || moves[0].PhotoId==a.PhotoId ||
               moves[0].PhotoId==b.PhotoId)
                throw new IOException("R29 reviewed Move C not fully Undone.");
            var c=moves[0];
            R27OwnedRecoveryMarkers(c);
            if(!R27Vacant(c.OriginalArchive) || !R27Vacant(c.Temp) ||
               !R27Vacant(c.Destination) ||
               !R27Owned(c.Source,c.SourceIdentity,c.SourceHash,c.SourceLength,c.SourceWriteTicks) ||
               !R27Owned(c.OutputArchive,c.TargetIdentity!,c.SourceHash,c.SourceLength))
                throw new IOException("R29 native terminal Move C changed.");
            if(!R27Owned(b.Source,b.SourceIdentity,b.SourceHash,b.SourceLength,b.SourceWriteTicks) ||
               !R27Vacant(b.Destination) || !R27Vacant(b.Temp) ||
               !R27Owned(b.Recovery,b.TargetIdentity!,b.SourceHash,b.SourceLength,b.TargetWriteTicks))
                throw new IOException("R29 native terminal Copy B changed.");
            if(!R27Owned(a.Source,a.SourceIdentity,a.SourceHash,a.SourceLength,a.SourceWriteTicks) ||
               !R27Vacant(a.Temp) ||
               SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(a.Destination)!) ||
               SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(a.Source)!))
                throw new IOException("R29 first Copy source/native path changed.");
            string root=R28ExactRecoveryRoot(a,DatabasePath);
            string operation=Path.Combine(root,a.SessionId.ToString("N"),a.Id.ToString("N"));
            if(!SortingWorkspace.PathComparer.Equals(a.Recovery,
                    Path.Combine(operation,Path.GetFileName(a.Destination))) ||
               !Directory.Exists(root) || SortingWorkspace.HasLinkedExistingAncestor(root) ||
               !File.Exists(Path.Combine(root,".photosift-owner-v1")) ||
               SortingWorkspace.HasSymlinkOrReparsePoint(Path.Combine(root,".photosift-owner-v1")) ||
               File.ReadAllText(Path.Combine(root,".photosift-owner-v1"))!=
                   "PhotoSift Reborn managed reversible output archives v1\n" ||
               (Directory.Exists(operation) && SortingWorkspace.HasLinkedExistingAncestor(operation)))
                throw new IOException("R29 Copy A recovery hierarchy/marker not owned.");
            bool output=R27Owned(a.Destination,a.TargetIdentity,a.SourceHash,
                a.SourceLength,a.TargetWriteTicks);
            bool archived=R27Owned(a.Recovery,a.TargetIdentity,a.SourceHash,
                a.SourceLength,a.TargetWriteTicks);
            bool valid=stage switch
            {
                "BeforeArchive"=>(a.State is "Committed" or "UndoIntent") &&
                    output && !archived && R27Vacant(a.Recovery),
                "AfterArchive"=>(a.State is "UndoIntent" or "Undone") &&
                    !output && R27Vacant(a.Destination) && archived,
                _=>false
            };
            if(!valid)throw new IOException("R29 stage-scoped native Copy A ownership refused: "+stage);
            string install=MoveInstallationId().ToString("N");
            var seal=M6JReadReview(c.Id);
            if(seal is null || seal.State!="Consumed" || seal.Family!="Move" ||
               seal.Session!=a.SessionId.ToString("N") || seal.Installation!=install ||
               seal.ReviewDigest!=M6JReviewDigest(seal.CandidateDigest,
                   seal.HistoryDigest,seal.CheckpointDigest))
                throw new IOException("R29 consumed exact Move C review unavailable.");
            using var tx=_connection.BeginTransaction();
            if(R29OriginalTwoCopyDigestInTransaction(tx,a,b)!=seal.HistoryDigest ||
               R26SavedCheckpointDigestInTransaction(tx)!=seal.CheckpointDigest)
                throw new IOException("R29 original R25 two-Copy seal no longer matches.");
            using(var proposal=_connection.CreateCommand())
            {
                proposal.Transaction=tx;
                proposal.CommandText="SELECT installation,session,family,state,candidate_digest,history_digest,checkpoint_digest,review_digest FROM m6j_review_proposals WHERE id=$id";
                proposal.Parameters.AddWithValue("$id",c.Id.ToString("N"));
                using var rows=proposal.ExecuteReader();
                if(!rows.Read() || rows.GetString(0)!=seal.Installation ||
                   rows.GetString(1)!=seal.Session || rows.GetString(2)!=seal.Family ||
                   rows.GetString(3)!=seal.State || rows.GetString(4)!=seal.CandidateDigest ||
                   rows.GetString(5)!=seal.HistoryDigest || rows.GetString(6)!=seal.CheckpointDigest ||
                   rows.GetString(7)!=seal.ReviewDigest || rows.Read())
                    throw new IOException("R29 existing consumed Move C seal changed.");
            }
            using(var grant=_connection.CreateCommand())
            {
                grant.Transaction=tx;
                grant.CommandText="SELECT installation,session,predecessor_id,predecessor_digest,used_intent,state FROM copy_successor_grants";
                using var rows=grant.ExecuteReader();
                var committedA=a with {State="Committed"};
                if(!rows.Read() || rows.GetString(0)!=install ||
                   rows.GetString(1)!=a.SessionId.ToString("N") ||
                   rows.GetString(2)!=a.Id.ToString("N") ||
                   rows.GetString(3)!=CopySuccessorHash(a.Id.ToString("N")+"\n"+
                       a.SessionId.ToString("N")+"\n"+JsonSerializer.Serialize(committedA)) ||
                   rows.IsDBNull(4) || rows.GetString(4)!=b.Id.ToString("N") ||
                   rows.GetInt64(5)!=1 || rows.Read())
                    throw new IOException("R29 original single-use B successor grant changed.");
            }
            using(var move=_connection.CreateCommand())
            {
                move.Transaction=tx;
                move.CommandText="SELECT id,session,json FROM move_items";
                using var rows=move.ExecuteReader();
                if(!rows.Read() || rows.GetString(0)!=c.Id.ToString("N") ||
                   rows.GetString(1)!=c.SessionId.ToString("N") ||
                   rows.GetString(2)!=JsonSerializer.Serialize(c) || rows.Read())
                    throw new IOException("R29 Move C terminal journal changed.");
            }
            tx.Commit();
        }
    }

    internal void PutM6JP4R29FirstCopyUndo(DurableCopyItem previous,DurableCopyItem next,
        string stage)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            string expected=previous.State switch {"Committed"=>"UndoIntent",
                "UndoIntent"=>"Undone",_=>"INVALID"};
            if(next.State!=expected || (previous with {State=expected})!=next ||
               (previous.State=="Committed" && stage!="BeforeArchive") ||
               (previous.State=="UndoIntent" && stage!="AfterArchive"))
                throw new IOException("R29 unauthorized terminal Copy A journal transition.");
            RequireM6JP4R29FirstCopyUndoStage(previous,stage);
            var b=CopyItems(previous.SessionId)[1];
            var c=MoveItems(previous.SessionId).Single();
            var seal=M6JReadReview(c.Id);
            if(seal is null || seal.State!="Consumed")
                throw new IOException("R29 consumed seal missing before journal transition.");
            using var tx=_connection.BeginTransaction();
            if(R29OriginalTwoCopyDigestInTransaction(tx,previous,b)!=seal.HistoryDigest ||
               R26SavedCheckpointDigestInTransaction(tx)!=seal.CheckpointDigest)
                throw new IOException("R29 original predecessor witness changed inside CAS.");
            using(var counts=_connection.CreateCommand())
            {
                counts.Transaction=tx;
                counts.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM reject_items),(SELECT COUNT(*) FROM m6j_review_proposals),"+
                    "(SELECT COUNT(*) FROM copy_successor_grants)";
                using var row=counts.ExecuteReader();
                if(!row.Read() || row.GetInt64(0)!=2 || row.GetInt64(1)!=1 ||
                   row.GetInt64(2)!=0 || row.GetInt64(3)!=0 ||
                   row.GetInt64(4)!=1 || row.GetInt64(5)!=1)
                    throw new IOException("R29 additional interleaved history refused.");
            }
            using(var copyB=_connection.CreateCommand())
            {
                copyB.Transaction=tx;
                copyB.CommandText="SELECT json FROM copy_items WHERE id=$id AND session=$session";
                copyB.Parameters.AddWithValue("$id",b.Id.ToString("N"));
                copyB.Parameters.AddWithValue("$session",b.SessionId.ToString("N"));
                if(copyB.ExecuteScalar() as string!=JsonSerializer.Serialize(b))
                    throw new IOException("R29 terminal Copy B journal changed inside CAS.");
            }
            using(var moveC=_connection.CreateCommand())
            {
                moveC.Transaction=tx;
                moveC.CommandText="SELECT json FROM move_items WHERE id=$id AND session=$session";
                moveC.Parameters.AddWithValue("$id",c.Id.ToString("N"));
                moveC.Parameters.AddWithValue("$session",c.SessionId.ToString("N"));
                if(moveC.ExecuteScalar() as string!=JsonSerializer.Serialize(c))
                    throw new IOException("R29 terminal Move C journal changed inside CAS.");
            }
            using(var update=_connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText="UPDATE copy_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
                update.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
                update.Parameters.AddWithValue("$id",previous.Id.ToString("N"));
                update.Parameters.AddWithValue("$session",previous.SessionId.ToString("N"));
                update.Parameters.AddWithValue("$old",JsonSerializer.Serialize(previous));
                if(update.ExecuteNonQuery()!=1)
                    throw new IOException("R29 exact Copy A compare-and-swap failed.");
            }
            tx.Commit();
        }
    }
}
