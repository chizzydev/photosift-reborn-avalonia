using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// Narrow third generated Move after precisely two independently committed
/// Copies. This is a durable SQL bridge, not a general operation epoch.
/// Only the marked disposable Windows native coordinator may call it.
/// </summary>
public sealed record M6JP4R26ThirdMoveIntentReceipt(Guid MoveId, bool ReviewConsumed,
    bool IntentDurable)
{
    public bool AuthorizesOrdinaryMixedFamily => false;
    public bool AuthorizesAutomaticReplay => false;
    public bool AuthorizesGenericMoveEpoch => false;
}

public sealed partial class WorkspaceSessionStore
{
    private static bool R26Empty(string path) => !File.Exists(path) && !Directory.Exists(path);

    private static void R26ExactTicket(PreparedCopy prepared,M6JProspectiveCandidate candidate,
        DurableMoveItem item,Guid install)
    {
        if(candidate.Family!="Move" || item is null || item.Id!=candidate.Id ||
           item.InstallationId!=install || item.SessionId!=prepared.SessionId ||
           item.PhotoId!=prepared.PhotoId || item.PlanId!=prepared.PlanId ||
           item.PlanRevision!=prepared.Revision || item.RouteKey!=prepared.Key ||
           item.Source!=prepared.Source || item.Destination!=prepared.Destination ||
           item.OutputRoot!=prepared.OutputRoot || item.SourceHash!=prepared.Hash ||
           item.SourceIdentity!=prepared.Identity || item.SourceLength!=prepared.Length ||
           item.SourceWriteTicks!=prepared.WriteTicks || item.State!="Intent" ||
           item.Error is not null || item.TempIdentity is not null ||
           item.TargetIdentity is not null || item.Sequence<=0 ||
           candidate.SessionId!=item.SessionId || candidate.PhotoId!=item.PhotoId ||
           candidate.Source!=item.Source || candidate.Destination!=item.Destination ||
           candidate.SourceHash!=item.SourceHash || candidate.SourceIdentity!=item.SourceIdentity ||
           candidate.SourceLength!=item.SourceLength ||
           candidate.SourceWriteTicks!=item.SourceWriteTicks)
            throw new IOException("R26 exact proposed Move/approved plan mismatch.");
        string operation=Path.Combine(Path.GetDirectoryName(item.Source)!,
            ".photosift-reborn-recovery",install.ToString("N"),
            item.SessionId.ToString("N"),item.Id.ToString("N"));
        if(!SortingWorkspace.PathComparer.Equals(item.OriginalArchive,
                Path.Combine(operation,Path.GetFileName(item.Source))) ||
           !SortingWorkspace.PathComparer.Equals(item.OutputArchive,
                Path.Combine(operation,"installed-"+Path.GetFileName(item.Destination))) ||
           !SortingWorkspace.PathComparer.Equals(item.Temp,
                Path.Combine(Path.GetDirectoryName(item.Destination)!,
                    ".photosift-move-"+item.Id.ToString("N")+".part")) ||
           !Path.IsPathFullyQualified(item.Source) ||
           !Path.IsPathFullyQualified(item.Destination) ||
           !R26Empty(item.Destination) || !R26Empty(item.Temp) ||
           !R26Empty(item.OriginalArchive) || !R26Empty(item.OutputArchive) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(item.Destination)!))
            throw new IOException("R26 exact recovery/temporary paths or target ownership refused.");
    }

    // Historical byte seal that R25 created when exactly TWO Copy rows and
    // zero other operation-family rows existed. NEVER omit an existing Move
    // to manufacture a new review; used only after verifying the one consumed
    // R26 Move row is precisely the reviewed operation.
    private string R26TwoCopyPredecessorRawDigest(SqliteTransaction? tx=null)
    {
        var builder=new StringBuilder("M6J-A2-RawHistory-v1\n");
        foreach(string table in new[]{"copy_items","move_items","rename_items","reject_items"})
        {
            builder.Append(table).Append('\n');
            if(table!="copy_items")continue;
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="SELECT id,session,json FROM copy_items ORDER BY id";
            using var reader=cmd.ExecuteReader();
            int count=0;
            while(reader.Read())
            {
                if(++count>2)throw new IOException("R26 unexpected extra Copy predecessor.");
                for(int index=0;index<3;index++)
                {
                    string value=reader.GetString(index);
                    if(value.Length>32768)throw new IOException("R26 oversized Copy predecessor.");
                    builder.Append(value.Length).Append(':').Append(value);
                }
                builder.Append('\n');
            }
            if(count!=2)throw new IOException("R26 missing committed Copy predecessor.");
        }
        return M6JHash(builder.ToString());
    }

    // This check runs ONLY while the R26 local transaction is active. Never
    // call M6JSavedCheckpoint here: it creates an unbound SQL command.
    // Compare the exact raw saved bytes that the earlier reviewed seal pinned.
    private string R26SavedCheckpointDigestInTransaction(SqliteTransaction tx)
    {
        using var cmd=_connection.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
        using var reader=cmd.ExecuteReader();
        if(!reader.Read() || reader.GetInt64(1)!=0)
            throw new IOException("R26 saved checkpoint became unsafe during atomic Intent.");
        string json=reader.GetString(0);
        if(json.Length>64*1024*1024 || reader.Read())
            throw new IOException("R26 malformed checkpoint during atomic Intent.");
        return M6JHash(json);
    }

    private void R26RequireTwoCopiesAndSeal(Guid id,Guid session,string expectedReviewState,
        string? expectedHistory=null)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(this);
        if(HasUnreconciledCopy || AllCopyHistoryCount()!=2 ||
           AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
           CountAuxiliaryHistoryRows()!=1)
            throw new IOException("R26 extra/unresolved mixed-family history.");
        var copies=CopyItems(session);
        if(copies.Count!=2 || copies[0].Id==copies[1].Id ||
           copies.Any(c=>c.State!="Committed" || c.Error is not null ||
               c.TargetIdentity is null || c.SourceIdentity is null ||
               !NativeFileCapability.IsSingleLinkRegularFile(c.Source) ||
               !NativeFileCapability.IsSingleLinkRegularFile(c.Destination) ||
               !CheckCopyOwnedFile(c.Source,c.SourceIdentity,c.SourceHash,c.SourceLength) ||
               !CheckCopyOwnedFile(c.Destination,c.TargetIdentity,c.SourceHash,c.SourceLength)))
            throw new IOException("R26 two committed native Copy predecessors not owned.");
        if(copies[0].PhotoId==copies[1].PhotoId ||
           copies[0].SourceIdentity==copies[1].SourceIdentity ||
           copies[0].TargetIdentity==copies[1].TargetIdentity)
            throw new IOException("R26 recorded Copy predecessors are not independent.");
        var stored=M6JReadReview(id);
        if(stored is null || stored.State!=expectedReviewState || stored.Family!="Move" ||
           stored.Installation!=MoveInstallationId().ToString("N") ||
           stored.Session!=session.ToString("N") ||
           stored.ReviewDigest!=M6JReviewDigest(stored.CandidateDigest,
               stored.HistoryDigest,stored.CheckpointDigest) ||
           stored.HistoryDigest!=R26TwoCopyPredecessorRawDigest() ||
           (expectedHistory is not null && stored.HistoryDigest!=expectedHistory) ||
           M6JSavedCheckpoint().Digest!=stored.CheckpointDigest)
            throw new IOException("R26 stored reviewed seal or historical Copy bytes changed.");
        // R25 bound the separately consumed Copy B successor grant. Preserve
        // the same one-use grant without attempting to mint another authority.
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT used_intent,state,session FROM copy_successor_grants";
        using var reader=cmd.ExecuteReader();
        if(!reader.Read() || reader.IsDBNull(0) || reader.GetInt64(1)!=1 ||
           reader.GetString(2)!=session.ToString("N") ||
           reader.GetString(0)!=copies[1].Id.ToString("N") || reader.Read())
            throw new IOException("R26 predecessor Copy B grant was not singly consumed.");
    }

    internal M6JP4R26ThirdMoveIntentReceipt RecordM6JP4R26ThirdMoveIntent(
        SortingWorkspace work,PreparedCopy prepared,M6JProspectiveCandidate candidate,
        M6JP4R25ThirdMoveReviewReceipt review,DurableMoveItem item,
        Action? testCutAfterSealConsumption=null)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(review is null || review.ReviewId!=candidate.Id ||
               !review.IsDurablyReviewedOnly || review.AuthorizesMoveIntent ||
               work.SessionId!=prepared.SessionId || !work.IsCurrentPlanApproved ||
               work.CurrentPlanId!=prepared.PlanId || work.Revision!=prepared.Revision ||
               work.GetApprovedPlan().Count!=1 ||
               JsonSerializer.Serialize(work.CaptureSession())!=JsonSerializer.Serialize(Load()))
                throw new IOException("R26 fresh explicitly reviewed third Move plan missing.");
            R26ExactTicket(prepared,candidate,item,MoveInstallationId());
            if(AllMoveHistoryCount()!=0 || !M6JNativeSourceMatches(candidate))
                throw new IOException("R26 native third source or operation history is stale.");
            R26RequireTwoCopiesAndSeal(candidate.Id,candidate.SessionId,"Reviewed",review.CopyHistoryDigest);
            var stored=M6JReadReview(candidate.Id)!;
            if(stored.ReviewDigest!=review.ReviewedDigest ||
               stored.CheckpointDigest!=review.SavedCheckpointDigest ||
               stored.CandidateJson!=JsonSerializer.Serialize(candidate) ||
               stored.CandidateDigest!=M6JHash(stored.CandidateJson) ||
               stored.HistoryDigest!=M6JRawHistoryDigest())
                throw new IOException("R26 reviewed candidate or saved predecessor proof changed.");
            var parent=Path.GetDirectoryName(item.Destination)!;
            string parentId=NativeFileCapability.DirectoryIdentity(parent);
            // Cache the installation identity BEFORE opening the SQLite transaction.
            // MoveInstallationId executes SQL without an attached transaction and
            // therefore cannot be called between BeginTransaction and Commit.
            string pinnedInstallation=MoveInstallationId().ToString("N");
            using var tx=_connection.BeginTransaction();
            using(var counts=_connection.CreateCommand())
            {
                counts.Transaction=tx;
                counts.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM reject_items),"+
                    "(SELECT COUNT(*) FROM m6j_review_proposals)";
                using var r=counts.ExecuteReader();
                if(!r.Read() || r.GetInt64(0)!=2 || r.GetInt64(1)!=0 ||
                   r.GetInt64(2)!=0 || r.GetInt64(3)!=0 || r.GetInt64(4)!=1)
                    throw new IOException("R26 additional journal rows inside atomic Intent.");
            }
            if(!M6JNativeSourceMatches(candidate) ||
               parentId!=NativeFileCapability.DirectoryIdentity(parent) ||
               stored.HistoryDigest!=R26TwoCopyPredecessorRawDigest(tx) ||
               R26SavedCheckpointDigestInTransaction(tx)!=stored.CheckpointDigest)
                throw new IOException("R26 predecessor or native source changed during atomic review.");
            using(var seal=_connection.CreateCommand())
            {
                seal.Transaction=tx;
                seal.CommandText="UPDATE m6j_review_proposals SET state='Consumed' "+
                    "WHERE id=$id AND installation=$install AND session=$session AND family='Move' "+
                    "AND state='Reviewed' AND candidate_json=$raw AND candidate_digest=$candidate "+
                    "AND history_digest=$history AND checkpoint_digest=$checkpoint AND review_digest=$seal";
                seal.Parameters.AddWithValue("$id",candidate.Id.ToString("N"));
                seal.Parameters.AddWithValue("$install",pinnedInstallation);
                seal.Parameters.AddWithValue("$session",candidate.SessionId.ToString("N"));
                seal.Parameters.AddWithValue("$raw",stored.CandidateJson);
                seal.Parameters.AddWithValue("$candidate",stored.CandidateDigest);
                seal.Parameters.AddWithValue("$history",stored.HistoryDigest);
                seal.Parameters.AddWithValue("$checkpoint",stored.CheckpointDigest);
                seal.Parameters.AddWithValue("$seal",stored.ReviewDigest!);
                if(seal.ExecuteNonQuery()!=1)
                    throw new IOException("R26 reviewed third Move was already consumed.");
            }
            testCutAfterSealConsumption?.Invoke(); // MUST roll back both writes
            using(var insert=_connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText="INSERT INTO move_items(id,session,sequence,json) VALUES($id,$session,$sequence,$json)";
                insert.Parameters.AddWithValue("$id",item.Id.ToString("N"));
                insert.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$sequence",item.Sequence);
                insert.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
                if(insert.ExecuteNonQuery()!=1)
                    throw new IOException("R26 exact third Move Intent was not inserted.");
            }
            // Transaction-bound rereads: both historical Copy bytes and the
            // raw saved checkpoint must remain exact after seal consumption.
            if(!M6JNativeSourceMatches(candidate) ||
               parentId!=NativeFileCapability.DirectoryIdentity(parent) ||
               stored.HistoryDigest!=R26TwoCopyPredecessorRawDigest(tx))
                throw new IOException("R26 native/SQLite Copy predecessor diverged before Intent commit.");
            using(var checkpoint=_connection.CreateCommand())
            {
                checkpoint.Transaction=tx;
                checkpoint.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
                using var reader=checkpoint.ExecuteReader();
                if(!reader.Read() || reader.GetInt64(1)!=0 ||
                   M6JHash(reader.GetString(0))!=stored.CheckpointDigest || reader.Read())
                    throw new IOException("R26 checkpoint changed before atomic Intent commit.");
            }
            tx.Commit();
            return new(item.Id,true,true);
        }
    }

    internal void PutM6JP4R26ThirdMoveState(DurableMoveItem next)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(next is null || next.Id==Guid.Empty || next.InstallationId!=MoveInstallationId() ||
               next.State is "Intent" or "UndoRestoreIntent" or "Undone" or "SourceRestored" or
                   "UndoOutputArchiveIntent" ||
               AllMoveHistoryCount()!=1)
                throw new IOException("R26 unsupported native third-Move journal state.");
            R26RequireTwoCopiesAndSeal(next.Id,next.SessionId,"Consumed");
            var stored=M6JReadReview(next.Id)!;
            var candidate=M6JDecodeStored(stored);
            if(candidate.Id!=next.Id || next.PhotoId!=candidate.PhotoId ||
               next.Source!=candidate.Source || next.Destination!=candidate.Destination ||
               next.SourceIdentity!=candidate.SourceIdentity ||
               next.SourceHash!=candidate.SourceHash ||
               next.SourceLength!=candidate.SourceLength ||
               next.SourceWriteTicks!=candidate.SourceWriteTicks)
                throw new IOException("R26 altered third-Move ticket identity.");
            using var tx=_connection.BeginTransaction();
            using var query=_connection.CreateCommand();
            query.Transaction=tx;
            query.CommandText="SELECT json FROM move_items WHERE id=$id AND session=$session";
            query.Parameters.AddWithValue("$id",next.Id.ToString("N"));
            query.Parameters.AddWithValue("$session",next.SessionId.ToString("N"));
            string previous=query.ExecuteScalar() as string
                ?? throw new IOException("R26 durable Move Intent disappeared.");
            if(previous.Length>32768)throw new IOException("R26 oversized native Move row.");
            var old=JsonSerializer.Deserialize<DurableMoveItem>(previous)
                ?? throw new IOException("R26 malformed persisted native Move row.");
            if(old with {TempIdentity=next.TempIdentity,TargetIdentity=next.TargetIdentity,
                State=next.State,Error=next.Error} != next)
                throw new IOException("R26 frozen native Move journal fields changed.");
            string? expected=old.State switch {
                "Intent"=>"TempReady","TempReady"=>"TargetPublished",
                "TargetPublished"=>"ArchiveIntent","ArchiveIntent"=>"SourceArchived",
                "SourceArchived"=>"Committed",_=>null
            };
            if(next.State!=expected && !(next.State=="NeedsAttention" &&
                   old.State!="Committed" && old.State!="NeedsAttention"))
                throw new IOException("R26 invalid durable third-Move journal transition.");
            query.Parameters.Clear();
            query.CommandText="UPDATE move_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            query.Parameters.AddWithValue("$id",next.Id.ToString("N"));
            query.Parameters.AddWithValue("$session",next.SessionId.ToString("N"));
            query.Parameters.AddWithValue("$old",previous);
            query.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            if(query.ExecuteNonQuery()!=1)
                throw new IOException("R26 native Move journal compare-and-swap conflict.");
            tx.Commit();
        }
    }
}
