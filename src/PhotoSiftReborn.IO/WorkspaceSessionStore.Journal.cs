using System.Text.Json;

namespace PhotoSiftReborn;

public sealed record DurableCopyItem(
    Guid Id, Guid SessionId, Guid PhotoId, char Key, Guid PlanId, long PlanRevision, long Sequence,
    string Source, string Destination, string Temp, string Recovery,
    string SourceHash, string SourceIdentity, long SourceLength, long SourceWriteTicks,
    string? TempIdentity, string? TargetIdentity, long TargetWriteTicks,
    string State, string? Error);

public sealed partial class WorkspaceSessionStore
{
    public void PutCopyItem(DurableCopyItem item)
    {
        lock (_dbGate)
        {
        RequireNoR30Writer();
        if (item.Id == Guid.Empty || item.SessionId == Guid.Empty || item.PlanId == Guid.Empty ||
            item.PhotoId == Guid.Empty || !Path.IsPathFullyQualified(item.Source) ||
            !Path.IsPathFullyQualified(item.Destination) || !Path.IsPathFullyQualified(item.Temp) ||
            !Path.IsPathFullyQualified(item.Recovery))
            throw new InvalidDataException("Incomplete file-operation identity or path.");
        if (AllRejectHistoryCount()!=0)
            throw new IOException("Reject recovery history blocks Copy journal writes; preserve independent recovery.");
        // A successor grant permits a new row ONLY through the atomic
        // PutCopySuccessorIntent transaction. Protect ALL states here, not just
        // Intent: a failed pre-Intent Apply has a volatile item, and its catch
        // must never INSERT a false NeedsAttention row using this writer.
        // Updates to a genuinely existing journal row remain allowed so a
        // successfully recorded Intent can be marked/reconciled safely.
        if (HasAnyCopySuccessorGrant)
        {
            using var existing=_connection.CreateCommand();
            existing.CommandText="SELECT COUNT(*) FROM copy_items WHERE id=$id";
            existing.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            if(Convert.ToInt64(existing.ExecuteScalar())!=1)
                throw new IOException("New Copy journal rows require a consumed, durably recorded successor Intent.");
        }
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText = "INSERT INTO copy_items(id,session,sequence,json) VALUES($id,$session,$seq,$json) " +
            "ON CONFLICT(id) DO UPDATE SET json=excluded.json";
        cmd.Parameters.AddWithValue("$id",item.Id.ToString("N"));
        cmd.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
        cmd.Parameters.AddWithValue("$seq",item.Sequence);
        cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
        cmd.ExecuteNonQuery();
        tx.Commit();
        }
    }
    // This is a global veto: a per-session terminal proof cannot silently
    // ignore an operation that belongs to an earlier or foreign session.
    public int AllCopyHistoryCount()
    {
        lock (_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM copy_items";
            return checked((int)Convert.ToInt64(cmd.ExecuteScalar()));
        }
    }

    public IReadOnlyList<DurableCopyItem> CopyItems(Guid session)
    {
        lock (_dbGate)
        {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT json FROM copy_items WHERE session=$session ORDER BY sequence,id";
        cmd.Parameters.AddWithValue("$session",session.ToString("N"));
        using var reader=cmd.ExecuteReader();
        var items = new List<DurableCopyItem>();
        while (reader.Read())
        {
            string json=reader.GetString(0);
            if (json.Length>32768) throw new InvalidDataException("Oversized operation journal item.");
            var item=JsonSerializer.Deserialize<DurableCopyItem>(json)
                ?? throw new InvalidDataException("Malformed operation journal item.");
            if (item.SessionId != session) throw new InvalidDataException("Operation journal session mismatch.");
            items.Add(item);
        }
        return items;
        }
    }
}
