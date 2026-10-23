using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P2A. Isolated generated-fixture transaction exercise only.
/// This method commits one exact A2 Reviewed candidate AND its Move Intent
/// in the SAME SQLite transaction. There is NO coordinator entry point and
/// NO file-system mutation or Undo permission here. It must not be generalized
/// to ordinary user files without the separate native execution contract.
/// </summary>
public sealed record M6JAtomicIntentReceipt(Guid Id, string State, bool JournalCommitted)
{
    public bool AuthorizesNativeFileMutation => false;
    public bool AuthorizesCrossFamilyUndo => false;
}

public sealed partial class WorkspaceSessionStore
{
    // The current OS-native descriptor observation is deliberately transient. This
    // helper never records a token or returns permission; callers MUST still
    // hold their own journal transaction and coordinator-specific native rules.
    private static string RequireP4R3PreCommitWitness(DurableCopyItem predecessor,
        DurableMoveItem proposed)
    {
        if (predecessor.TargetIdentity is null ||
            predecessor.SourceIdentity is null || proposed.SourceIdentity is null ||
            File.Exists(predecessor.Temp) || Directory.Exists(predecessor.Temp) ||
            File.Exists(predecessor.Recovery) || Directory.Exists(predecessor.Recovery) ||
            File.Exists(proposed.Destination) || Directory.Exists(proposed.Destination) ||
            File.Exists(proposed.Temp) || Directory.Exists(proposed.Temp) ||
            File.Exists(proposed.OriginalArchive) || Directory.Exists(proposed.OriginalArchive) ||
            File.Exists(proposed.OutputArchive) || Directory.Exists(proposed.OutputArchive))
            throw new IOException("P4-R3 unowned destination, temporary or recovery path exists.");
        var observation=M6JP4FreshPinnedPredecessor.Inspect(
            new(predecessor.Source,predecessor.SourceIdentity,predecessor.SourceHash,
                predecessor.SourceLength,predecessor.SourceWriteTicks),
            new(predecessor.Destination,predecessor.TargetIdentity,
                predecessor.SourceHash,predecessor.SourceLength),
            new(proposed.Source,proposed.SourceIdentity,proposed.SourceHash,
                proposed.SourceLength,proposed.SourceWriteTicks));
        if (!observation.IsFreshWitness || observation.ObservationFingerprint is null)
            throw new IOException("P4-R3 pinned native predecessor has changed.");
        return observation.ObservationFingerprint;
    }

    internal M6JAtomicIntentReceipt RecordM6JGeneratedOnlyCopyMoveIntent(
        Guid reviewedId, DurableMoveItem item, Action? testCutAfterReviewConsume = null)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            // Non-negotiable fixture boundary. The production execution path
            // does NOT expose this function, and this method performs no FS write.
            var stateFolder=Path.GetDirectoryName(DatabasePath)
                ?? throw new IOException("M6J transaction has no state folder.");
            var root=Directory.GetParent(stateFolder)?.FullName
                ?? throw new IOException("M6J transaction has no disposable root.");
            if (Path.GetFileName(stateFolder)!="state" ||
                !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(root,"P3B2_TEST_ONLY.marker")) ||
                File.ReadAllText(Path.Combine(root,"P3B2_TEST_ONLY.marker"))!="synthetic-interruption-only" ||
                SortingWorkspace.HasLinkedExistingAncestor(root))
                throw new IOException("M6J-P2A rejects non-disposable or unmarked state.");

            if (reviewedId==Guid.Empty || item is null || item.Id!=reviewedId || item.State!="Intent" ||
                item.Error is not null || item.TempIdentity is not null || item.TargetIdentity is not null)
                throw new IOException("M6J exact native Intent did not match reviewed identity.");

            var proof=InspectM6JMoveAfterCopyIntent(reviewedId,item);
            if (!proof.IsExactPreparedShape || proof.PredecessorDigest is null ||
                proof.ReviewedCandidateDigest is null)
                throw new IOException("M6J reviewed native predecessor/plan proof refused.");
            var stored=M6JReadReview(reviewedId)
                ?? throw new IOException("M6J reviewed proposal not found.");
            if (stored.State!="Reviewed" || stored.ReviewDigest!=M6JReviewDigest(
                    stored.CandidateDigest,stored.HistoryDigest,stored.CheckpointDigest) ||
                stored.CandidateDigest!=proof.ReviewedCandidateDigest)
                throw new IOException("M6J stored reviewed seal is stale or malformed.");

            // P4-R4: bind the predecessor used by both fresh native witnesses
            // to the EXACT Copy journal bytes accepted by the review proof.
            // A previous P4-R3 candidate used an unbound identifier here and
            // therefore could not compile.  The row stays immutable within
            // the subsequent SQLite transaction and is reread before commit.
            DurableCopyItem predecessor;
            using (var predecessorQuery=_connection.CreateCommand())
            {
                predecessorQuery.CommandText="SELECT id,session,json FROM copy_items LIMIT 2";
                using var row=predecessorQuery.ExecuteReader();
                if (!row.Read())
                    throw new IOException("P4-R4 committed Copy predecessor missing.");
                string id=row.GetString(0), session=row.GetString(1), json=row.GetString(2);
                if (row.Read() || json.Length>32768 ||
                    M6JHash(id+"\n"+session+"\n"+json)!=proof.PredecessorDigest)
                    throw new IOException("P4-R4 committed Copy predecessor bytes differ from review.");
                predecessor=JsonSerializer.Deserialize<DurableCopyItem>(json)
                    ?? throw new IOException("P4-R4 committed Copy predecessor malformed.");
                if (predecessor.Id.ToString("N")!=id ||
                    predecessor.SessionId.ToString("N")!=session ||
                    predecessor.State!="Committed" || predecessor.Error is not null)
                    throw new IOException("P4-R4 committed Copy predecessor identity/state invalid.");
            }

            // P4-R3: pin a fresh three-file witness before starting the
            // generated-only SQLite commit; do not turn this into a native
            // coordinator permission, a long-lived capability or an epoch.
            string firstWitness=RequireP4R3PreCommitWitness(predecessor,item);
            string destinationParent=Path.GetDirectoryName(item.Destination)
                ?? throw new IOException("M6J destination parent is missing.");
            string originalDestinationParent=NativeFileCapability.DirectoryIdentity(destinationParent);

            // This is the ONLY commit boundary; a thrown test cut after the
            // review UPDATE rolls it back together with the pending Intent.
            using var tx=_connection.BeginTransaction();
            using (var current=_connection.CreateCommand())
            {
                current.Transaction=tx;
                current.CommandText="SELECT "+
                    "(SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM reject_items)";
                using var reader=current.ExecuteReader();
                if (!reader.Read() || reader.GetInt64(0)!=1 || reader.GetInt64(1)!=0 ||
                    reader.GetInt64(2)!=0 || reader.GetInt64(3)!=0)
                    throw new IOException("M6J exact predecessor cardinality changed before transaction.");
            }
            // Reconfirm the immutable predecessor JSON observed by A3-P1.
            using (var prior=_connection.CreateCommand())
            {
                prior.Transaction=tx;
                prior.CommandText="SELECT id,session,json FROM copy_items LIMIT 2";
                using var reader=prior.ExecuteReader();
                if (!reader.Read())throw new IOException("M6J original Copy disappeared.");
                string id=reader.GetString(0), session=reader.GetString(1), json=reader.GetString(2);
                if (reader.Read() || M6JHash(id+"\n"+session+"\n"+json)!=proof.PredecessorDigest)
                    throw new IOException("M6J original Copy predecessor changed before atomic Intent.");
            }
            // Check the raw original review seal with a conditional update.
            using (var seal=_connection.CreateCommand())
            {
                seal.Transaction=tx;
                seal.CommandText="UPDATE m6j_review_proposals SET state='Consumed' "+
                    "WHERE id=$id AND installation=$installation AND session=$session "+
                    "AND family='Move' AND state='Reviewed' AND review_digest=$seal "+
                    "AND candidate_digest=$candidate AND history_digest=$history "+
                    "AND checkpoint_digest=$checkpoint";
                seal.Parameters.AddWithValue("$id",reviewedId.ToString("N"));
                seal.Parameters.AddWithValue("$installation",stored.Installation);
                seal.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
                seal.Parameters.AddWithValue("$seal",stored.ReviewDigest!);
                seal.Parameters.AddWithValue("$candidate",stored.CandidateDigest);
                seal.Parameters.AddWithValue("$history",stored.HistoryDigest);
                seal.Parameters.AddWithValue("$checkpoint",stored.CheckpointDigest);
                if (seal.ExecuteNonQuery()!=1)
                    throw new IOException("M6J reviewed proposal already consumed or modified.");
            }
            testCutAfterReviewConsume?.Invoke();
            using (var intent=_connection.CreateCommand())
            {
                intent.Transaction=tx;
                intent.CommandText="INSERT INTO move_items(id,session,sequence,json) "+
                    "VALUES($id,$session,$sequence,$json)";
                intent.Parameters.AddWithValue("$id",item.Id.ToString("N"));
                intent.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
                intent.Parameters.AddWithValue("$sequence",item.Sequence);
                intent.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
                if (intent.ExecuteNonQuery()!=1)
                    throw new IOException("M6J native Move Intent insert did not occur.");
            }
            // Re-read the immutable predecessor bytes in the SAME transaction,
            // and then acquire a second fresh native descriptor witness AFTER the
            // conditional seal consumption and journal insertion. An injected
            // filesystem alteration causes rollback of BOTH SQLite changes.
            using (var verify=_connection.CreateCommand())
            {
                verify.Transaction=tx;
                verify.CommandText="SELECT id,session,json FROM copy_items LIMIT 2";
                using var reader=verify.ExecuteReader();
                if(!reader.Read() || M6JHash(reader.GetString(0)+"\n"+
                       reader.GetString(1)+"\n"+reader.GetString(2))!=proof.PredecessorDigest ||
                   reader.Read())
                    throw new IOException("P4-R3 predecessor changed during atomic Intent.");
            }
            var lastWitness=RequireP4R3PreCommitWitness(predecessor,item);
            if (!string.Equals(firstWitness,lastWitness,StringComparison.Ordinal) ||
                !string.Equals(originalDestinationParent,
                    NativeFileCapability.DirectoryIdentity(destinationParent),StringComparison.Ordinal))
                throw new IOException("P4-R3 native source, predecessor or destination parent changed before commit.");
            tx.Commit();
            // No generated photo/recovery/output file was created or moved.
            return new(item.Id,"ConsumedWithIntent",true);
        }
    }
}
