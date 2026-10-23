using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// A B3 grant is executable ONLY for its one exact disposable synthetic
/// successor. The previous B1 seal and B2 ticket remain immutable; each
/// committed Intent consumes this receipt in the same SQLite transaction.
/// It is NOT permission for arbitrary personal-photo Move or multiple epochs.
/// </summary>
public sealed record MoveEpochGrantEvidence(bool Present, bool Approved, bool Consumed,
    string Reason);

public sealed partial class WorkspaceSessionStore
{
    public MoveEpochGrantEvidence InspectMoveEpochGrant()
    {
        lock (_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT installation,candidate_digest,predecessor_digest,"+
                "predecessor_rows,state FROM move_epoch_grant WHERE id=1";
            using var rd=cmd.ExecuteReader();
            if (!rd.Read()) return new MoveEpochGrantEvidence(false,false,false,
                "No approved successor grant. The old history veto remains enforced.");
            var install=rd.GetString(0);var digest=rd.GetString(1);
            var prev=rd.GetString(2);var count=rd.GetInt64(3);var state=rd.GetString(4);
            rd.Close();
            using var ticket=_connection.CreateCommand();
            ticket.CommandText="SELECT installation,candidate_digest,predecessor_digest,"+
                "predecessor_rows FROM move_epoch_ticket WHERE id=1";
            using var tr=ticket.ExecuteReader();
            if (!tr.Read() || tr.GetString(0)!=install || tr.GetString(1)!=digest ||
                tr.GetString(2)!=prev || tr.GetInt64(3)!=count ||
                install!=MoveInstallationId().ToString("N"))
                return new MoveEpochGrantEvidence(true,false,false,
                    "Grant and staged ticket mismatch; preserve the records.");
            tr.Close();
            if (state=="Consumed")
                return new MoveEpochGrantEvidence(true,false,true,
                    "Single-use capability consumed. It cannot authorize another Intent.");
            if (state!="Approved" || !InspectStagedMoveEpoch().Verified)
                return new MoveEpochGrantEvidence(true,false,false,
                    "Approved grant stale: historical journal or native candidate failed fresh proof.");
            return new MoveEpochGrantEvidence(true,true,false,
                "Exact native-verified staged Move approved for one disposable successor Intent.");
        }
    }

    // Called ONLY after coordinator checks real reviewed workspace, prepared
    // source and explicit user acknowledgement. An unchanged B2 ticket is NOT
    // itself approval; approval creates a second, distinct durable SQL receipt.
    internal void ApproveMoveEpochGrant(DurableMoveItem candidate)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            if (!InspectStagedMoveEpoch().Verified || !ValidateProposedMove(candidate) ||
                candidate.InstallationId!=MoveInstallationId())
                throw new IOException("Staged successor needs fresh native and historical verification.");
            string exact=JsonSerializer.Serialize(candidate);
            string install=MoveInstallationId().ToString("N");
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="SELECT installation,predecessor_digest,predecessor_rows,"+
                "candidate_json,candidate_digest FROM move_epoch_ticket WHERE id=1";
            using (var rd=cmd.ExecuteReader())
            {
                if (!rd.Read() || rd.GetString(0)!=install || rd.GetString(3)!=exact ||
                    rd.GetString(4)!=CandidateDigest(candidate))
                    throw new IOException("B2 staged successor is missing or changed.");
                string pred=rd.GetString(1);long rows=rd.GetInt64(2);
                rd.Close();
                using var hist=_connection.CreateCommand();hist.Transaction=tx;
                hist.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM rename_closeout),"+
                    "(SELECT copy_risk FROM workspace_checkpoint WHERE id=1)";
                using(var rr=hist.ExecuteReader())
                    if(!rr.Read() || rr.GetInt64(0)!=0 || rr.GetInt64(1)!=rows ||
                        rr.GetInt64(2)!=0 || rr.GetInt64(3)!=0 ||
                        (!rr.IsDBNull(4) && rr.GetInt64(4)!=0))
                        throw new IOException("Dependency history changed before explicit Move grant.");
                var head=TerminalMoveJournalDigest(install,tx);
                if (head.Count!=rows || head.Digest!=pred || !ValidateProposedMove(candidate))
                    throw new IOException("Move history or candidate changed during grant transaction.");
                RequireNativePredecessorsInsideTransaction(tx,checked((int)rows),install);
                using var prior=_connection.CreateCommand();prior.Transaction=tx;
                prior.CommandText="SELECT installation,candidate_digest,predecessor_digest,"+
                    "predecessor_rows,state FROM move_epoch_grant WHERE id=1";
                using var existing=prior.ExecuteReader();
                if (existing.Read())
                {
                    if (existing.GetString(0)!=install || existing.GetString(1)!=CandidateDigest(candidate) ||
                        existing.GetString(2)!=pred || existing.GetInt64(3)!=rows ||
                        existing.GetString(4)!="Approved")
                        throw new IOException("Existing grant immutable or already consumed; no reauthorization.");
                }
                else
                {
                    existing.Close();
                    prior.CommandText="INSERT INTO move_epoch_grant(id,installation,candidate_digest,"+
                        "predecessor_digest,predecessor_rows,state) " +
                        "VALUES(1,$inst,$hash,$pred,$rows,'Approved')";
                    prior.Parameters.AddWithValue("$inst",install);
                    prior.Parameters.AddWithValue("$hash",CandidateDigest(candidate));
                    prior.Parameters.AddWithValue("$pred",pred);
                    prior.Parameters.AddWithValue("$rows",rows);
                    prior.ExecuteNonQuery();
                }
            }
            tx.Commit();
            if(!InspectMoveEpochGrant().Approved)
                throw new IOException("Grant could not be freshly reverified; do not execute.");
        }
    }

    // Reprove native ownership of EVERY predecessor Move during the SAME
    // SQLite transaction that consumes the grant. Digest alone would miss a
    // same-byte replacement of an old Undo output archive after approval.
    private void RequireNativePredecessorsInsideTransaction(SqliteTransaction tx,int count,
        string installation)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT id,session,json FROM move_items ORDER BY sequence,id";
        using var reader=cmd.ExecuteReader();
        var seenSource=new HashSet<string>(SortingWorkspace.PathComparer);
        var seenTarget=new HashSet<string>(SortingWorkspace.PathComparer);
        var seenArchive=new HashSet<string>(SortingWorkspace.PathComparer);
        int verified=0;
        while(reader.Read())
        {
            string id=reader.GetString(0),session=reader.GetString(1),json=reader.GetString(2);
            if(json.Length>32768) throw new IOException("Oversized historical Move row.");
            DurableMoveItem? old;
            try { old=JsonSerializer.Deserialize<DurableMoveItem>(json); }
            catch (JsonException) {old=null;}
            if (old is null || old.Id.ToString("N")!=id ||
                old.SessionId.ToString("N")!=session ||
                old.InstallationId.ToString("N")!=installation ||
                !seenSource.Add(old.Source) || !seenTarget.Add(old.Destination) ||
                !seenArchive.Add(old.OutputArchive) ||
                !DurableMoveCoordinator.ProveTerminalUndoneEvidence(old))
                throw new IOException("A terminal predecessor lost native source/archive ownership.");
            verified=checked(verified+1);
        }
        if (verified!=count)
            throw new IOException("Historical predecessor count changed before Move approval/Intent.");
    }

    // Used before the coordinator builds the durable Intent. The final SQL
    // boundary STILL performs an atomic independent proof and consumption.
    internal void RequireApprovedMoveEpochGrant(DurableMoveItem candidate)
    {
        lock (_dbGate)
        {
            if (!InspectMoveEpochGrant().Approved)
                throw new IOException("A signed-off, unconsumed successor grant is required.");
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT candidate_json FROM move_epoch_ticket WHERE id=1";
            if (!string.Equals(cmd.ExecuteScalar() as string,JsonSerializer.Serialize(candidate),
                    StringComparison.Ordinal) || !ValidateProposedMove(candidate))
                throw new IOException("Reviewed successor differs from the frozen staged ticket.");
        }
    }

    // Must be called ONLY from PutMoveItem's new-Intent SQLite transaction,
    // under _dbGate. All history reads and UPDATE use the same transaction.
    // Historical journal digest and the exact candidate MUST match the grant.
    private void ConsumeApprovedMoveEpochInsideIntent(DurableMoveItem item,SqliteTransaction tx)
    {
        // PutMoveItem already checked this installation identity BEFORE
        // opening its transaction. Do not issue a command without tx here.
        string install=item.InstallationId.ToString("N");
        using var ticket=_connection.CreateCommand();ticket.Transaction=tx;
        ticket.CommandText="SELECT installation,predecessor_digest,predecessor_rows,"+
            "candidate_json,candidate_digest FROM move_epoch_ticket WHERE id=1";
        using var t=ticket.ExecuteReader();
        if(!t.Read() || t.GetString(0)!=install ||
            t.GetString(3)!=JsonSerializer.Serialize(item) ||
            t.GetString(4)!=CandidateDigest(item))
            throw new IOException("Move Intent differs from staged ticket; preserving history.");
        string predecessor=t.GetString(1);long rows=t.GetInt64(2);
        t.Close();
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
            "(SELECT COUNT(*) FROM move_items),"+
            "(SELECT COUNT(*) FROM rename_items),"+
            "(SELECT COUNT(*) FROM rename_closeout),"+
            "(SELECT copy_risk FROM workspace_checkpoint WHERE id=1)";
        using(var r=cmd.ExecuteReader())
            if(!r.Read() || r.GetInt64(0)!=0 || r.GetInt64(1)!=rows ||
               r.GetInt64(2)!=0 || r.GetInt64(3)!=0 ||
               (!r.IsDBNull(4) && r.GetInt64(4)!=0))
                throw new IOException("Move Intent denied: dependent journal changed after approval.");
        var head=TerminalMoveJournalDigest(install,tx);
        if (head.Count!=rows || head.Digest!=predecessor || !ValidateProposedMove(item))
            throw new IOException("Move Intent denied: predecessor/candidate not freshly owned.");
        RequireNativePredecessorsInsideTransaction(tx,checked((int)rows),install);
        using var grant=_connection.CreateCommand();grant.Transaction=tx;
        grant.CommandText="UPDATE move_epoch_grant SET state='Consumed' WHERE id=1 AND "+
            "installation=$install AND candidate_digest=$candidate AND "+
            "predecessor_digest=$predecessor AND predecessor_rows=$rows AND state='Approved'";
        grant.Parameters.AddWithValue("$install",install);
        grant.Parameters.AddWithValue("$candidate",CandidateDigest(item));
        grant.Parameters.AddWithValue("$predecessor",predecessor);
        grant.Parameters.AddWithValue("$rows",rows);
        if(grant.ExecuteNonQuery()!=1)
            throw new IOException("Move Intent denied: grant missing, used or mismatched.");
        // Do not commit here; PutMoveItem must atomically write the Intent.
    }
}
