using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// R28: one generated-only, exact second Copy B Undo after the independently
/// reviewed third Move C has reached the durable R27 Undone state. This does
/// NOT authorize Copy A Undo, an arbitrary second epoch, or ordinary photos.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    private string R28OriginalTwoCopyReviewDigestInTransaction(SqliteTransaction tx,
        DurableCopyItem originalB)
    {
        var raw=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT id,session,json FROM copy_items ORDER BY id";
        using(var rows=cmd.ExecuteReader())
        {
            int count=0;
            while(rows.Read())
            {
                if(++count>2)throw new IOException("R28 excess Copy rows in historical seal.");
                string id=rows.GetString(0),session=rows.GetString(1),json=rows.GetString(2);
                if(json.Length>32768)throw new IOException("R28 oversized Copy journal.");
                if(id==originalB.Id.ToString("N"))
                {
                    if(session!=originalB.SessionId.ToString("N") ||
                       json!=JsonSerializer.Serialize(originalB))
                        throw new IOException("R28 second Copy row changed during seal verification.");
                    // Reconstruct the ORIGINAL Reviewed history, not a new
                    // approval after changing the Copy's terminal state.
                    json=JsonSerializer.Serialize(originalB with {State="Committed"});
                }
                foreach(var part in new[]{id,session,json})
                    raw.Append(part.Length).Append(':').Append(part);
                raw.Append('\n');
            }
            if(count!=2)throw new IOException("R28 exact Copy predecessor count missing.");
        }
        raw.Append("move_items\nrename_items\nreject_items\n");
        return M6JHash(raw.ToString());
    }

    private static string R28ExactRecoveryRoot(DurableCopyItem b,string database)
    {
        string baseRoot=Directory.GetParent(Path.GetDirectoryName(database)!)!.FullName;
        return Path.Combine(baseRoot,".photosift-reborn-recovery");
    }

    internal void RequireM6JP4R28SecondCopyUndoStage(DurableCopyItem b,string physical)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(this);
        lock(_dbGate)
        {
            if(b is null || b.Id==Guid.Empty || b.SessionId==Guid.Empty ||
               b.Error is not null || b.SourceIdentity is null || b.TargetIdentity is null ||
               b.SourceIdentity==b.TargetIdentity ||
               AllCopyHistoryCount()!=2 || AllMoveHistoryCount()!=1 ||
               AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
               CountAuxiliaryHistoryRows()!=1 || HasUnreconciledCopy ||
               (b.State is not ("Committed" or "UndoIntent" or "Undone")))
                throw new IOException("R28 exact reviewed multi-row history missing.");
            var copies=CopyItems(b.SessionId);
            if(copies.Count!=2 || copies[1]!=b || copies[0].State!="Committed" ||
               copies[0].Error is not null || copies[0].TargetIdentity is null ||
               copies[0].Id==b.Id || copies[0].PhotoId==b.PhotoId ||
               copies[0].SourceIdentity==b.SourceIdentity ||
               copies[0].TargetIdentity==b.TargetIdentity)
                throw new IOException("R28 Copy A/B identity or ordered journal changed.");
            var a=copies[0];
            var moves=MoveItems(b.SessionId);
            if(moves.Count!=1 || moves[0].State!="Undone" || moves[0].Error is not null ||
               moves[0].PhotoId==b.PhotoId || moves[0].PhotoId==a.PhotoId)
                throw new IOException("R28 third Move C must be fully Undone.");
            var c=moves[0];
            R27OwnedRecoveryMarkers(c);
            if(!R27Vacant(c.OriginalArchive) || !R27Vacant(c.Destination) ||
               !R27Vacant(c.Temp) ||
               !R27Owned(c.Source,c.SourceIdentity,c.SourceHash,c.SourceLength,c.SourceWriteTicks) ||
               !R27Owned(c.OutputArchive,c.TargetIdentity!,c.SourceHash,c.SourceLength))
                throw new IOException("R28 R27 terminal Move C native ownership stale.");
            if(!R27Owned(a.Source,a.SourceIdentity,a.SourceHash,a.SourceLength,a.SourceWriteTicks) ||
               !R27Owned(a.Destination,a.TargetIdentity!,a.SourceHash,a.SourceLength,a.TargetWriteTicks) ||
               !R27Owned(b.Source,b.SourceIdentity,b.SourceHash,b.SourceLength,b.SourceWriteTicks) ||
               !R27Vacant(a.Temp) || !R27Vacant(a.Recovery) || !R27Vacant(b.Temp))
                throw new IOException("R28 independently committed Copy ownership stale.");
            string root=R28ExactRecoveryRoot(b,DatabasePath);
            string operation=Path.Combine(root,b.SessionId.ToString("N"),b.Id.ToString("N"));
            if(!SortingWorkspace.PathComparer.Equals(b.Recovery,
                    Path.Combine(operation,Path.GetFileName(b.Destination))) ||
               SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(b.Destination)!) ||
               SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(b.Source)!) ||
               (Directory.Exists(root) && SortingWorkspace.HasLinkedExistingAncestor(root)))
                throw new IOException("R28 owned recovery path changed or linked.");
            if(Directory.Exists(root))
            {
                string marker=Path.Combine(root,".photosift-owner-v1");
                if(!File.Exists(marker) || SortingWorkspace.HasSymlinkOrReparsePoint(marker) ||
                   File.ReadAllText(marker)!="PhotoSift Reborn managed reversible output archives v1\n")
                    throw new IOException("R28 unmanaged output recovery root.");
            }
            if(Directory.Exists(operation) &&
               SortingWorkspace.HasLinkedExistingAncestor(operation))
                throw new IOException("R28 linked recovery operation hierarchy.");
            bool inDestination=R27Owned(b.Destination,b.TargetIdentity,b.SourceHash,
                b.SourceLength,b.TargetWriteTicks);
            bool inRecovery=R27Owned(b.Recovery,b.TargetIdentity,b.SourceHash,
                b.SourceLength,b.TargetWriteTicks);
            bool matches=physical switch
            {
                "BeforeArchive"=>(b.State is "Committed" or "UndoIntent") &&
                    inDestination && !inRecovery && R27Vacant(b.Recovery),
                "AfterArchive"=>(b.State is "UndoIntent" or "Undone") &&
                    !inDestination && R27Vacant(b.Destination) && inRecovery,
                _=>false
            };
            if(!matches)
                throw new IOException("R28 stage-specific Copy B native ownership refused: "+physical);
            // A durable consumed R25 review and one-use Copy grant must still
            // be bound to these very operation IDs and the originally committed
            // Copy B bytes. This is a READ-only SQLite transaction.
            string install=MoveInstallationId().ToString("N");
            var seal=M6JReadReview(c.Id);
            if(seal is null || seal.State!="Consumed" || seal.Family!="Move" ||
               seal.Installation!=install || seal.Session!=b.SessionId.ToString("N") ||
               seal.ReviewDigest!=M6JReviewDigest(seal.CandidateDigest,
                    seal.HistoryDigest,seal.CheckpointDigest))
                throw new IOException("R28 prior exact Move review seal changed.");
            using var tx=_connection.BeginTransaction();
            if(seal.HistoryDigest!=R28OriginalTwoCopyReviewDigestInTransaction(tx,b) ||
               seal.CheckpointDigest!=R26SavedCheckpointDigestInTransaction(tx))
                throw new IOException("R28 original reviewed committed Copy bytes/checkpoint changed.");
            using(var persisted=_connection.CreateCommand())
            {
                persisted.Transaction=tx;
                persisted.CommandText="SELECT installation,session,family,state,candidate_digest,history_digest,checkpoint_digest,review_digest FROM m6j_review_proposals WHERE id=$id";
                persisted.Parameters.AddWithValue("$id",c.Id.ToString("N"));
                using var row=persisted.ExecuteReader();
                if(!row.Read() || row.GetString(0)!=seal.Installation ||
                   row.GetString(1)!=seal.Session || row.GetString(2)!=seal.Family ||
                   row.GetString(3)!=seal.State || row.GetString(4)!=seal.CandidateDigest ||
                   row.GetString(5)!=seal.HistoryDigest || row.GetString(6)!=seal.CheckpointDigest ||
                   row.GetString(7)!=seal.ReviewDigest || row.Read())
                    throw new IOException("R28 consumed Move review changed inside witness transaction.");
            }
            using(var grant=_connection.CreateCommand())
            {
                grant.Transaction=tx;
                grant.CommandText="SELECT installation,session,predecessor_id,predecessor_digest,used_intent,state "+
                    "FROM copy_successor_grants";
                using var row=grant.ExecuteReader();
                if(!row.Read() || row.GetString(0)!=install ||
                   row.GetString(1)!=b.SessionId.ToString("N") ||
                   row.GetString(2)!=a.Id.ToString("N") ||
                   row.GetString(3)!=CopySuccessorHash(a.Id.ToString("N")+"\n"+
                       a.SessionId.ToString("N")+"\n"+JsonSerializer.Serialize(a)) ||
                   row.IsDBNull(4) || row.GetString(4)!=b.Id.ToString("N") ||
                   row.GetInt64(5)!=1 || row.Read())
                    throw new IOException("R28 exact Copy B one-use approval was not consumed.");
            }
            using(var move=_connection.CreateCommand())
            {
                move.Transaction=tx;
                move.CommandText="SELECT id,session,json FROM move_items";
                using var row=move.ExecuteReader();
                if(!row.Read() || row.GetString(0)!=c.Id.ToString("N") ||
                   row.GetString(1)!=c.SessionId.ToString("N") ||
                   row.GetString(2)!=JsonSerializer.Serialize(c) || row.Read())
                    throw new IOException("R28 Move C terminal journal changed during native witness.");
            }
            tx.Commit();
        }
    }

    internal void PutM6JP4R28SecondCopyUndo(DurableCopyItem previous,DurableCopyItem next,
        string nativeStage)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            string expected=previous.State switch{"Committed"=>"UndoIntent","UndoIntent"=>"Undone",_=>"INVALID"};
            if(next.State!=expected || (previous with {State=expected})!=next ||
               (previous.State=="Committed" && nativeStage!="BeforeArchive") ||
               (previous.State=="UndoIntent" && nativeStage!="AfterArchive"))
                throw new IOException("R28 unauthorized second Copy B CAS transition.");
            RequireM6JP4R28SecondCopyUndoStage(previous,nativeStage);
            var moves=MoveItems(previous.SessionId);
            if(moves.Count!=1)throw new IOException("R28 exact Move C missing before CAS.");
            var seal=M6JReadReview(moves[0].Id);
            if(seal is null || seal.State!="Consumed")
                throw new IOException("R28 prior Move seal missing before CAS.");
            using var tx=_connection.BeginTransaction();
            if(R28OriginalTwoCopyReviewDigestInTransaction(tx,previous)!=seal.HistoryDigest ||
               R26SavedCheckpointDigestInTransaction(tx)!=seal.CheckpointDigest)
                throw new IOException("R28 original Copy B review history changed inside CAS.");
            using(var prior=_connection.CreateCommand())
            {
                prior.Transaction=tx;
                prior.CommandText="SELECT json FROM move_items WHERE id=$id AND session=$session";
                prior.Parameters.AddWithValue("$id",moves[0].Id.ToString("N"));
                prior.Parameters.AddWithValue("$session",moves[0].SessionId.ToString("N"));
                if(prior.ExecuteScalar() as string!=JsonSerializer.Serialize(moves[0]))
                    throw new IOException("R28 terminal Move C changed inside CAS.");
            }
            using(var counts=_connection.CreateCommand())
            {
                counts.Transaction=tx;
                counts.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM reject_items),"+
                    "(SELECT COUNT(*) FROM m6j_review_proposals),"+
                    "(SELECT COUNT(*) FROM copy_successor_grants)";
                using var row=counts.ExecuteReader();
                if(!row.Read() || row.GetInt64(0)!=2 || row.GetInt64(1)!=1 ||
                   row.GetInt64(2)!=0 || row.GetInt64(3)!=0 ||
                   row.GetInt64(4)!=1 || row.GetInt64(5)!=1)
                    throw new IOException("R28 unexpected additional durable history.");
            }
            using(var update=_connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText="UPDATE copy_items SET json=$new WHERE id=$id AND "+
                    "session=$session AND json=$old";
                update.Parameters.AddWithValue("$new",JsonSerializer.Serialize(next));
                update.Parameters.AddWithValue("$id",previous.Id.ToString("N"));
                update.Parameters.AddWithValue("$session",previous.SessionId.ToString("N"));
                update.Parameters.AddWithValue("$old",JsonSerializer.Serialize(previous));
                if(update.ExecuteNonQuery()!=1)
                    throw new IOException("R28 exact second Copy B compare-and-swap failed.");
            }
            tx.Commit();
        }
    }
}
