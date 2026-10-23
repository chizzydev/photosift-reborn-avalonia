using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// B4B1A: one immutable record per later Move epoch. A staged ticket is NOT a
/// Move permission; separately acknowledged approval is spent atomically with
/// the exact native-reviewed Move Intent. Still generated-fixture ONLY.
/// </summary>
public sealed record AdditionalEpochAuthorizationEvidence(bool Present,bool Verified,
    bool Approved,bool Consumed,int Epoch,string Reason);

public sealed partial class WorkspaceSessionStore
{
    private sealed record AdditionalEpochRow(int Epoch,string Installation,int Predecessors,
        string Head,string CandidateJson,string Digest,string State);

    private int AllMoveHistoryCountInsideTransaction(SqliteTransaction tx)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT COUNT(*) FROM move_items";
        return checked(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    private AdditionalEpochRow? FindAdditionalEpoch(int epoch,SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT epoch,installation,predecessor_rows,ledger_head,"+
            "candidate_json,candidate_digest,state FROM move_epoch_authorization WHERE epoch=$epoch";
        cmd.Parameters.AddWithValue("$epoch",epoch);
        using var rd=cmd.ExecuteReader();
        if(!rd.Read()) return null;
        return new AdditionalEpochRow(checked((int)rd.GetInt64(0)),rd.GetString(1),
            checked((int)rd.GetInt64(2)),rd.GetString(3),rd.GetString(4),
            rd.GetString(5),rd.GetString(6));
    }

    private static bool IsExactAdditionalCandidate(AdditionalEpochRow row,
        DurableMoveItem item,string install,int ancestors,string ledgerHead)
        => row.Epoch==ancestors+1 && row.Predecessors==ancestors &&
            row.Installation==install && row.Head==ledgerHead &&
            row.CandidateJson==JsonSerializer.Serialize(item) &&
            row.Digest==CandidateDigest(item);

    private bool NoHistoricalCollisionInsideTx(DurableMoveItem candidate,SqliteTransaction tx)
    {
        foreach(var entry in OrderedMoveBytes(tx))
        {
            DurableMoveItem? prior;
            try { prior=JsonSerializer.Deserialize<DurableMoveItem>(entry.Json); }
            catch(JsonException) { return false; }
            if(prior is null || prior.SessionId==candidate.SessionId ||
                prior.PhotoId==candidate.PhotoId ||
                SortingWorkspace.PathComparer.Equals(prior.Source,candidate.Source) ||
                SortingWorkspace.PathComparer.Equals(prior.Destination,candidate.Destination) ||
                SortingWorkspace.PathComparer.Equals(prior.OriginalArchive,candidate.OriginalArchive) ||
                SortingWorkspace.PathComparer.Equals(prior.OutputArchive,candidate.OutputArchive))
                return false;
        }
        return true;
    }

    // Called only inside a write transaction. A hash head without proof of
    // every predecessor's current native Undo ownership is NEVER enough.
    private (int Ancestors,string Head) RequireCompleteEpochPrefixInsideTx(SqliteTransaction tx)
    {
        string install;
        using(var identity=_connection.CreateCommand())
        {
            identity.Transaction=tx;
            identity.CommandText="SELECT token FROM installation_identity WHERE id=1";
            install=identity.ExecuteScalar() as string ?? throw new IOException("Installation identity missing.");
            if(!Guid.TryParseExact(install,"N",out var guid) || guid==Guid.Empty)
                throw new IOException("Installation identity malformed.");
        }
        using(var cmd=_connection.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                "(SELECT COUNT(*) FROM move_items),"+
                "(SELECT COUNT(*) FROM rename_items),"+
                "(SELECT COUNT(*) FROM rename_closeout),"+
                "(SELECT copy_risk FROM workspace_checkpoint WHERE id=1)";
            using var rd=cmd.ExecuteReader();
            if(!rd.Read() || rd.GetInt64(0)!=0 || rd.GetInt64(1)<2 ||
                rd.GetInt64(2)!=0 || rd.GetInt64(3)!=0 ||
                (!rd.IsDBNull(4) && rd.GetInt64(4)!=0))
                throw new IOException("Mixed or incomplete history; later Move denied.");
        }
        var historical=OrderedMoveBytes(tx);
        var receipts=ExistingEpochReceipts(tx);
        if(historical.Count<2 || receipts.Count!=historical.Count)
            throw new IOException("Every previous Move must have an immutable terminal receipt.");
        string head=VerifyEpochReceiptPrefix(receipts,historical,install,true);
        RequireNativePredecessorsInsideTransaction(tx,historical.Count,install);
        if(!string.Equals(head,receipts[^1].Digest,StringComparison.Ordinal))
            throw new IOException("Terminal epoch chain head mismatch.");
        return (historical.Count,head);
    }

    private void RequireBoundedThirdCandidate(DurableMoveItem candidate)
    {
        if(!P5CGuiFixturePolicy.IsMarkedStore(this) || HasUnreconciledCopy ||
            candidate.InstallationId!=MoveInstallationId() ||
            !ValidateProposedMove(candidate) || !NoPriorSourceOrDestination(candidate))
            throw new IOException("Additional Move proposed outside owned generated fixture or with conflicting history.");
        // B4B1A's new engine supports the first later epoch only. This is NOT
        // authorization for arbitrary later photos, folders, or production Move.
        string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
        string generated=Path.Combine(root,"source","photo_03.png");
        if(!SortingWorkspace.PathComparer.Equals(candidate.Source,generated) ||
            !string.Equals(candidate.SourceHash,OriginalGeneratedFixtureHash(root,"photo_03.png"),
                StringComparison.Ordinal) ||
            Load()?.SessionId!=candidate.SessionId)
            throw new IOException("Third Move requires the exact saved generated-photo_03 session.");
    }

    public AdditionalEpochAuthorizationEvidence InspectAdditionalEpochAuthorization(DurableMoveItem candidate)
    {
        lock(_dbGate)
        {
            int ordinal=0;
            try
            {
                var counts=AssessFreshMoveEpoch();ordinal=counts.MoveRows+1;
                // An already consumed grant refers to the PREVIOUS row count;
                // inspect its own immutable epoch after Move Intent exists.
                using var cmd=_connection.CreateCommand();
                cmd.CommandText="SELECT epoch FROM move_epoch_authorization WHERE candidate_digest=$digest";
                cmd.Parameters.AddWithValue("$digest",CandidateDigest(candidate));
                object? stored=cmd.ExecuteScalar();
                if(stored is null)
                    return new AdditionalEpochAuthorizationEvidence(false,false,false,false,ordinal,
                        "No later-epoch ticket; no Move authority.");
                ordinal=checked(Convert.ToInt32(stored));
                var row=FindAdditionalEpoch(ordinal)!;
                if(row.State=="Consumed")
                    return new AdditionalEpochAuthorizationEvidence(true,false,false,true,ordinal,
                        "Consumed authorization cannot be reused.");
                using var tx=_connection.BeginTransaction();
                var (ancestors,head)=RequireCompleteEpochPrefixInsideTx(tx);
                if(!IsExactAdditionalCandidate(row,candidate,candidate.InstallationId.ToString("N"),
                       ancestors,head) || !NoHistoricalCollisionInsideTx(candidate,tx) ||
                   !ValidateProposedMove(candidate) || !P5CGuiFixturePolicy.IsMarkedStore(this))
                    return new AdditionalEpochAuthorizationEvidence(true,false,false,false,ordinal,
                        "Historical ledger, exact source or immutable proposal changed.");
                tx.Commit();
                return new AdditionalEpochAuthorizationEvidence(true,true,row.State=="Approved",
                    false,ordinal,"Bounded exact native Move proposal; explicit approval required.");
            }
            catch(Exception ex) when(ex is IOException or InvalidOperationException or InvalidDataException or
                UnauthorizedAccessException or JsonException or ArgumentException or OverflowException)
            {return new AdditionalEpochAuthorizationEvidence(ordinal>0,false,false,false,ordinal,
                "Later-epoch verification refused: "+ex.Message);}
        }
    }

    public AdditionalEpochAuthorizationEvidence StageAdditionalEpochAuthorization(DurableMoveItem candidate)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            RequireBoundedThirdCandidate(candidate);
            using var tx=_connection.BeginTransaction();
            var (ancestors,head)=RequireCompleteEpochPrefixInsideTx(tx);
            if(ancestors!=2 || !NoHistoricalCollisionInsideTx(candidate,tx))
                throw new IOException("B4B1A permits only the third distinct synthetic Move.");
            var json=JsonSerializer.Serialize(candidate);
            if(json.Length>32768) throw new IOException("Epoch ticket JSON too large.");
            var digest=CandidateDigest(candidate);
            var existing=FindAdditionalEpoch(ancestors+1,tx);
            if(existing is not null)
            {
                if(!IsExactAdditionalCandidate(existing,candidate,candidate.InstallationId.ToString("N"),
                     ancestors,head) || existing.State!="Staged")
                    throw new IOException("An immutable ticket already exists or has been approved/consumed.");
            }
            else
            {
                using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
                cmd.CommandText="INSERT INTO move_epoch_authorization("+
                    "epoch,installation,predecessor_rows,ledger_head,candidate_json,candidate_digest,state)"+
                    " VALUES($e,$install,$count,$head,$json,$digest,'Staged')";
                cmd.Parameters.AddWithValue("$e",ancestors+1);
                cmd.Parameters.AddWithValue("$install",candidate.InstallationId.ToString("N"));
                cmd.Parameters.AddWithValue("$count",ancestors);
                cmd.Parameters.AddWithValue("$head",head);
                cmd.Parameters.AddWithValue("$json",json);
                cmd.Parameters.AddWithValue("$digest",digest);
                if(cmd.ExecuteNonQuery()!=1) throw new IOException("Failed to stage later-epoch ticket.");
            }
            tx.Commit();
            var result=InspectAdditionalEpochAuthorization(candidate);
            if(!result.Verified) throw new IOException("Committed later-epoch ticket needs new inspection.");
            return result;
        }
    }

    public AdditionalEpochAuthorizationEvidence ApproveAdditionalEpochAuthorization(
        DurableMoveItem candidate,bool explicitlyAcknowledged)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(!explicitlyAcknowledged) throw new IOException("Explicitly acknowledged exact Move review is required.");
            RequireBoundedThirdCandidate(candidate);
            using var tx=_connection.BeginTransaction();
            var (ancestors,head)=RequireCompleteEpochPrefixInsideTx(tx);
            var row=FindAdditionalEpoch(ancestors+1,tx);
            if(row is null || row.State!="Staged" ||
                !IsExactAdditionalCandidate(row,candidate,candidate.InstallationId.ToString("N"),ancestors,head) ||
                !NoHistoricalCollisionInsideTx(candidate,tx))
                throw new IOException("Missing, stale or already used later-epoch ticket.");
            using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="UPDATE move_epoch_authorization SET state='Approved' WHERE epoch=$epoch "+
                "AND candidate_digest=$digest AND state='Staged'";
            cmd.Parameters.AddWithValue("$epoch",ancestors+1);
            cmd.Parameters.AddWithValue("$digest",CandidateDigest(candidate));
            if(cmd.ExecuteNonQuery()!=1) throw new IOException("Later-epoch ticket approval raced or changed.");
            tx.Commit();
            var approved=InspectAdditionalEpochAuthorization(candidate);
            if(!approved.Verified || !approved.Approved)
                throw new IOException("Approved later-epoch ticket lost evidence; do not move.");
            return approved;
        }
    }

    internal void RequireApprovedAdditionalEpochAuthorization(DurableMoveItem candidate)
    {
        lock(_dbGate)
        {
            var found=InspectAdditionalEpochAuthorization(candidate);
            if(!found.Verified || !found.Approved || !ValidateProposedMove(candidate))
                throw new IOException("Later Move requires unconsumed exact explicitly reviewed epoch grant.");
        }
    }

    // Invoked solely by PutMoveItem's Intent branch; the same SQLite
    // transaction updates Approved -> Consumed AND inserts the Move Intent.
    private void ConsumeAdditionalEpochInsideIntent(DurableMoveItem item,SqliteTransaction tx)
    {
        var (ancestors,head)=RequireCompleteEpochPrefixInsideTx(tx);
        if(ancestors!=2 || !NoHistoricalCollisionInsideTx(item,tx) ||
            !ValidateProposedMove(item) || !P5CGuiFixturePolicy.IsMarkedStore(this) ||
            Path.GetFileName(item.Source)!="photo_03.png")
            throw new IOException("Later Move Intent lacks exact verified generated source.");
        using(var checkpoint=_connection.CreateCommand())
        {
            checkpoint.Transaction=tx;
            checkpoint.CommandText="SELECT json FROM workspace_checkpoint WHERE id=1";
            var json=checkpoint.ExecuteScalar() as string;
            var saved=json is null ? null : JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(json);
            if(saved?.SessionId!=item.SessionId)
                throw new IOException("Later Move Intent differs from saved reviewed workspace session.");
        }
        var row=FindAdditionalEpoch(ancestors+1,tx);
        if(row is null || row.State!="Approved" ||
            !IsExactAdditionalCandidate(row,item,item.InstallationId.ToString("N"),ancestors,head))
            throw new IOException("Later Move Intent lacks exact fresh immutable grant.");
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="UPDATE move_epoch_authorization SET state='Consumed' WHERE epoch=$epoch "+
            "AND candidate_digest=$digest AND ledger_head=$head AND state='Approved'";
        cmd.Parameters.AddWithValue("$epoch",ancestors+1);
        cmd.Parameters.AddWithValue("$digest",CandidateDigest(item));
        cmd.Parameters.AddWithValue("$head",head);
        if(cmd.ExecuteNonQuery()!=1)
            throw new IOException("Later Move Intent grant consumption failed; no journal entry.");
    }
}
