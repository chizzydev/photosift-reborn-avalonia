using System.Text.Json;

namespace PhotoSiftReborn;

// A separate, non-deleting journal. This is NOT a Move row, Copy row,
// Rename row or a user-visible Trash operation.
public sealed record DurableRejectItem(
    Guid Id,Guid InstallationId,Guid SessionId,Guid PhotoId,long SessionRevision,
    string Source,string Archive,string SourceHash,string SourceIdentity,
    long SourceLength,long SourceWriteTicks,string State,string? Error=null);

public sealed partial class WorkspaceSessionStore
{
    public int AllRejectHistoryCount()
    {
        lock(_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM reject_items";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public IReadOnlyList<DurableRejectItem> RejectItems()
    {
        lock(_dbGate)
        {
            var installation=MoveInstallationId();
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT id,session,json FROM reject_items ORDER BY rowid";
            using var rd=cmd.ExecuteReader();
            var found=new List<DurableRejectItem>();
            while(rd.Read())
            {
                var json=rd.GetString(2);
                if(json.Length>32768) throw new InvalidDataException("Oversized Reject recovery row.");
                var row=JsonSerializer.Deserialize<DurableRejectItem>(json)
                    ?? throw new InvalidDataException("Malformed Reject recovery row.");
                if(row.Id.ToString("N")!=rd.GetString(0) ||
                   row.SessionId.ToString("N")!=rd.GetString(1) ||
                   row.InstallationId!=installation || row.Id==Guid.Empty ||
                   row.SessionId==Guid.Empty || row.PhotoId==Guid.Empty)
                    throw new InvalidDataException("Reject recovery identity mismatch.");
                found.Add(row);
            }
            return found;
        }
    }

    // Successors require a separate native-verified terminal receipt chain and
    // an exact, single-use consent grant. Neither history deletion nor an
    // Undone state alone can authorize another filesystem operation.
    public void PutRejectItem(DurableRejectItem item)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(item.Id==Guid.Empty || item.InstallationId!=MoveInstallationId() ||
                item.SessionId==Guid.Empty || item.PhotoId==Guid.Empty || item.SessionRevision<0 ||
                item.SourceLength<1 || item.SourceWriteTicks<=0 || item.SourceHash.Length!=64 ||
                string.IsNullOrWhiteSpace(item.SourceIdentity) ||
                !Path.IsPathFullyQualified(item.Source) || !Path.IsPathFullyQualified(item.Archive) ||
                !new[]{"Staged","Approved","Intent","RelocateIntent","Committed","UndoIntent","Undone"}.Contains(item.State) ||
                item.Error is not null)
                throw new InvalidDataException("Invalid Reject recovery row or state.");
            // The ordinary source root must be bound BEFORE opening the SQL
            // transaction. A later saved session may differ from the historical
            // predecessor's session, but never from its verified source root.
            string? ordinaryRoot=item.State=="Staged" &&
                !P5CGuiFixturePolicy.IsMarkedStore(this)
                ? RequireOrdinaryTerminalRoot() : null;
            using var tx=_connection.BeginTransaction();
            string? oldJson;
            using(var lookup=_connection.CreateCommand())
            {
                lookup.Transaction=tx;
                lookup.CommandText="SELECT json FROM reject_items WHERE id=$id";
                lookup.Parameters.AddWithValue("$id",item.Id.ToString("N"));
                oldJson=lookup.ExecuteScalar() as string;
            }
            if(oldJson is null)
            {
                if(item.State!="Staged") throw new IOException("A Reject recovery must first be staged.");
                using var count=_connection.CreateCommand();count.Transaction=tx;
                count.CommandText="SELECT (SELECT COUNT(*) FROM copy_items)+"+
                    "(SELECT COUNT(*) FROM move_items)+"+
                    "(SELECT COUNT(*) FROM rename_items)+(SELECT COUNT(*) FROM rename_closeout)";
                if(Convert.ToInt64(count.ExecuteScalar())!=0 || HasUnreconciledCopy)
                    throw new IOException("Another recovery journal exists; Reject cannot mix with Copy, Move or Rename.");
                var earlier=ReadRejectBytes(tx);
                if(earlier.Count>0)
                {
                    var receipt=VerifyRejectTerminalReceipts(tx,requireAll:true,ordinaryRoot);
                    if(receipt.Count!=earlier.Count ||
                        earlier.Any(x=>x.Row.PhotoId==item.PhotoId ||
                            SortingWorkspace.PathComparer.Equals(x.Row.Source,item.Source) ||
                            SortingWorkspace.PathComparer.Equals(x.Row.Archive,item.Archive) ||
                            x.Row.SessionId==item.SessionId))
                        throw new IOException("Distinct verified terminal Reject epochs and a new session are required.");
                    using var consume=_connection.CreateCommand();consume.Transaction=tx;
                    consume.CommandText="UPDATE reject_successor_grants SET used=1 "+
                        "WHERE reject_id=$id AND previous_digest=$head AND staged_json=$json AND used=0";
                    consume.Parameters.AddWithValue("$id",item.Id.ToString("N"));
                    consume.Parameters.AddWithValue("$head",receipt.Head);
                    consume.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
                    if(consume.ExecuteNonQuery()!=1)
                        throw new IOException("No unique, exact, unused Reject successor grant; staging refused.");
                }
            }
            else
            {
                if(oldJson.Length>32768) throw new InvalidDataException("Oversized prior Reject row.");
                var old=JsonSerializer.Deserialize<DurableRejectItem>(oldJson)
                    ?? throw new InvalidDataException("Malformed prior Reject row.");
                if(old.Id!=item.Id || old.InstallationId!=item.InstallationId ||
                    old.SessionId!=item.SessionId || old.PhotoId!=item.PhotoId ||
                    old.SessionRevision!=item.SessionRevision || old.Source!=item.Source ||
                    old.Archive!=item.Archive || old.SourceHash!=item.SourceHash ||
                    old.SourceIdentity!=item.SourceIdentity || old.SourceLength!=item.SourceLength ||
                    old.SourceWriteTicks!=item.SourceWriteTicks)
                    throw new IOException("Reject operation identity/paths are immutable.");
                var expected=old.State switch
                {
                    "Staged"=>"Approved", "Approved"=>"Intent",
                    "Intent"=>"RelocateIntent", "RelocateIntent"=>"Committed",
                    "Committed"=>"UndoIntent", "UndoIntent"=>"Undone",
                    _=>"NO_NEXT_TRANSITION"
                };
                if(item.State!=expected)
                    throw new IOException("Reject journal is terminal, unresolved or transition skipped; no replay.");
            }
            using var put=_connection.CreateCommand();put.Transaction=tx;
            put.CommandText=oldJson is null
                ? "INSERT INTO reject_items(id,session,json) VALUES($id,$session,$json)"
                : "UPDATE reject_items SET json=$json WHERE id=$id";
            put.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            put.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
            if(oldJson is null)put.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
            if(put.ExecuteNonQuery()!=1)
                throw new IOException("Reject journal write did not affect exactly one row.");
            tx.Commit();
        }
    }
}
