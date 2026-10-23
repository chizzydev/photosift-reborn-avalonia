using System.Text.Json;

namespace PhotoSiftReborn;

// P5B is a separately journalled operation. Copy journal entries and their
// historical SQLite schema must remain meaningful and unchanged.
public sealed record DurableMoveItem(
    Guid Id, Guid InstallationId, Guid SessionId, Guid PhotoId, Guid PlanId,
    long PlanRevision, long Sequence,
    string Source, string Destination, string Temp,
    string OriginalArchive, string OutputArchive, string OutputRoot,
    string SourceHash, string SourceIdentity, long SourceLength, long SourceWriteTicks,
    string? TempIdentity, string? TargetIdentity, string State, string? Error,
    char RouteKey='\0');

/// <summary>
/// P6C2-A read-only database-wide authority snapshot. Every historical row
/// remains binding even when another workspace/session is opened or the old
/// operation has been undone. This is a STOP guard, not Move authorization.
/// No epoch rollover exists yet: P6C2-B/C must design an explicit durable
/// transition before any history-bearing installation can execute new Moves.
/// </summary>
public sealed record MoveEpochAssessment(int CopyRows, int MoveRows,
    int RenameRows, int RenameCloseouts)
{
    public bool IsFresh => CopyRows==0 && MoveRows==0 &&
        RenameRows==0 && RenameCloseouts==0;
    public string Counts => $"Copy={CopyRows}, Move={MoveRows}, Rename={RenameRows}, RenameCloseout={RenameCloseouts}";
}

/// <summary>
/// P6C2-B read-only terminal history evidence. CanConsiderLaterAuthorization
/// means only that earlier Move Undo artefacts are individually proved NOW.
/// It never authorizes a new Move, changes SQLite or survives as approval.
/// </summary>
public sealed record TerminalMoveHistoryEvidence(int MoveRows, int VerifiedUndoRows,
    bool CanConsiderLaterAuthorization, string Reason);

public sealed partial class WorkspaceSessionStore
{
    // Database-wide check, deliberately NOT filtered by the active session.
    // This operation reads only SQLite; it does not inspect, create or modify
    // a source, output, recovery directory, journal row or approval.
    public MoveEpochAssessment AssessFreshMoveEpoch()
    {
        lock (_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                "(SELECT COUNT(*) FROM move_items),"+
                "(SELECT COUNT(*) FROM rename_items),"+
                "(SELECT COUNT(*) FROM rename_closeout)";
            using var reader=cmd.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("Missing Move epoch authority counters.");
            return new MoveEpochAssessment(checked((int)reader.GetInt64(0)),checked((int)reader.GetInt64(1)),
                checked((int)reader.GetInt64(2)),checked((int)reader.GetInt64(3)));
        }
    }

    public void RequireFreshMoveEpoch()
    {
        var snapshot=AssessFreshMoveEpoch();
        if (!snapshot.IsFresh)
            throw new IOException("A previous file-operation journal belongs to this installation ("+
                snapshot.Counts+"). A newly opened workspace or an Undone state is NOT a new Move epoch. "+
                "No new Move is authorized; preserve the old journal and recovery archives.");
    }

    /// <summary>
    /// Observe all historical Move records across all sessions and independently
    /// prove current restored source / archived output ownership. This does NOT
    /// create a new epoch or change the existing PutMoveItem veto.
    /// </summary>
    public TerminalMoveHistoryEvidence InspectTerminalMoveHistory()
    {
        lock (_dbGate)
        {
            // Snapshot and journal rows are obtained under the same DB gate.
            var counts=AssessFreshMoveEpoch();
            if (HasUnreconciledCopy || counts.CopyRows!=0 ||
                counts.RenameRows!=0 || counts.RenameCloseouts!=0)
                return new TerminalMoveHistoryEvidence(counts.MoveRows,0,false,
                    "Copy, Rename, closeout or legacy copy-risk history requires its own separate proof.");
            if (counts.MoveRows==0)
                return new TerminalMoveHistoryEvidence(0,0,false,
                    "No Move history; the fresh-epoch gate remains the sole current authority.");
            var install=MoveInstallationId();
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT id,session,json FROM move_items ORDER BY sequence,id";
            using var reader=cmd.ExecuteReader();
            int examined=0;
            var seenSources=new HashSet<string>(SortingWorkspace.PathComparer);
            var seenTargets=new HashSet<string>(SortingWorkspace.PathComparer);
            var seenArchives=new HashSet<string>(SortingWorkspace.PathComparer);
            while (reader.Read())
            {
                var id=reader.GetString(0);
                var session=reader.GetString(1);
                var json=reader.GetString(2);
                if (json.Length>32768)
                    return new TerminalMoveHistoryEvidence(counts.MoveRows,examined,false,
                        "Oversized historical Move journal row.");
                DurableMoveItem? row;
                try {row=JsonSerializer.Deserialize<DurableMoveItem>(json);}
                catch (JsonException) {row=null;}
                if (row is null || row.Id==Guid.Empty || row.SessionId==Guid.Empty ||
                    row.InstallationId!=install ||
                    row.Id.ToString("N")!=id || row.SessionId.ToString("N")!=session ||
                    !seenSources.Add(row.Source) || !seenTargets.Add(row.Destination) ||
                    !seenArchives.Add(row.OutputArchive) ||
                    !DurableMoveCoordinator.ProveTerminalUndoneEvidence(row))
                    return new TerminalMoveHistoryEvidence(counts.MoveRows,examined,false,
                        "An earlier Move row is unresolved, malformed, replaced or missing owned recovery evidence.");
                examined++;
            }
            if (examined!=counts.MoveRows)
                return new TerminalMoveHistoryEvidence(counts.MoveRows,examined,false,
                    "Move history changed during evidence inspection.");
            return new TerminalMoveHistoryEvidence(counts.MoveRows,examined,true,
                "All historical Move Undo records are currently owned; NOT approval for another Move.");
        }
    }

    // A workspace is single-checkpoint but historical operations can belong to
    // a retired session. Do not silently lose authority when the user rebinds.
    public int AllMoveHistoryCount()
    {
        lock (_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM move_items";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public Guid MoveInstallationId()
    {
        lock (_dbGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT token FROM installation_identity WHERE id=1";
            if (!Guid.TryParseExact(cmd.ExecuteScalar() as string, "N", out var id) || id == Guid.Empty)
                throw new InvalidDataException("Move installation identity is unavailable; fail closed.");
            return id;
        }
    }

    public IReadOnlyList<DurableMoveItem> MoveItems(Guid session)
    {
        lock (_dbGate)
        {
            var installationId=MoveInstallationId();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM move_items WHERE session=$session ORDER BY sequence,id";
            cmd.Parameters.AddWithValue("$session",session.ToString("N"));
            using var reader=cmd.ExecuteReader();
            var results=new List<DurableMoveItem>();
            while (reader.Read())
            {
                string json=reader.GetString(0);
                if (json.Length>32768) throw new InvalidDataException("Move journal row is oversized.");
                var item=JsonSerializer.Deserialize<DurableMoveItem>(json)
                    ?? throw new InvalidDataException("Malformed Move journal row.");
                if (item.SessionId!=session || item.Id==Guid.Empty || item.InstallationId!=installationId)
                    throw new InvalidDataException("Move journal identity mismatch.");
                results.Add(item);
            }
            return results;
        }
    }

    public void PutMoveItem(DurableMoveItem item)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            if (item.Id==Guid.Empty || item.SessionId==Guid.Empty || item.PhotoId==Guid.Empty ||
                item.PlanId==Guid.Empty || item.InstallationId!=MoveInstallationId() ||
                item.SourceHash.Length!=64 || string.IsNullOrWhiteSpace(item.SourceIdentity) ||
                item.SourceLength<0 || new[] {item.Source,item.Destination,item.Temp,item.OriginalArchive,
                    item.OutputArchive,item.OutputRoot}.Any(p=>!Path.IsPathFullyQualified(p)))
                throw new InvalidDataException("Incomplete Move identity, contents or frozen paths.");
            var legal=new HashSet<string>(StringComparer.Ordinal) {
                "Intent", "TempReady", "TargetPublished", "ArchiveIntent", "SourceArchived",
                "Committed", "UndoRestoreIntent", "SourceRestored", "UndoOutputArchiveIntent",
                "Undone", "NeedsAttention"
            };
            if (!legal.Contains(item.State)) throw new InvalidDataException("Unknown Move journal state.");
            if (AllRejectHistoryCount()!=0)
                throw new IOException("Reject recovery history blocks Move journal writes.");
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="SELECT json FROM move_items WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            var prior=cmd.ExecuteScalar() as string;
            if (prior is null)
            {
                if (item.State!="Intent") throw new InvalidDataException("Move must begin with a durable Intent.");
                // P6C2-A last line of defense. This check participates in the
                // same SQLite write transaction as the first durable Intent;
                // a second process cannot introduce a new history epoch by
                // swapping SessionId or bypassing the UI's preflight.
                using (var history=_connection.CreateCommand())
                {
                    history.Transaction=tx;
                    history.CommandText="SELECT (SELECT COUNT(*) FROM copy_items)+"+
                        "(SELECT COUNT(*) FROM move_items)+"+
                        "(SELECT COUNT(*) FROM rename_items)+"+
                        "(SELECT COUNT(*) FROM rename_closeout)";
                    if (Convert.ToInt64(history.ExecuteScalar())!=0)
                    {
                        // B3 successor exception is EXACTLY one separately
                        // reviewed and approved ticket. Only this path can
                        // consume the grant, inside this same Intent tx.
                        if (AllMoveHistoryCountInsideTransaction(tx)>=2)
                            ConsumeAdditionalEpochInsideIntent(item,tx);
                        else
                            ConsumeApprovedMoveEpochInsideIntent(item,tx);
                    }
                }
                cmd.CommandText="SELECT json FROM move_items WHERE session=$session";
                cmd.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
                using var reader=cmd.ExecuteReader();
                while (reader.Read())
                {
                    var old=JsonSerializer.Deserialize<DurableMoveItem>(reader.GetString(0))
                        ?? throw new InvalidDataException("Malformed earlier Move row.");
                    if (old.PhotoId==item.PhotoId || SortingWorkspace.PathComparer.Equals(old.Source,item.Source) ||
                        SortingWorkspace.PathComparer.Equals(old.Destination,item.Destination))
                        throw new IOException("Move source/photo/destination already has operation history. Refusing a second operation.");
                    if (old.State!="Undone") throw new IOException("A previous Move journal entry is still active.");
                }
            }
            else
            {
                var old=JsonSerializer.Deserialize<DurableMoveItem>(prior)
                    ?? throw new InvalidDataException("Malformed existing Move journal row.");
                if (old with { TempIdentity=item.TempIdentity, TargetIdentity=item.TargetIdentity,
                        State=item.State,Error=item.Error } != item)
                    throw new InvalidDataException("A Move operation's frozen identity, paths or hash changed.");
                string? next = old.State switch
                {
                    "Intent" => "TempReady",
                    "TempReady" => "TargetPublished",
                    "TargetPublished" => "ArchiveIntent",
                    "ArchiveIntent" => "SourceArchived",
                    "SourceArchived" => "Committed",
                    "Committed" => "UndoRestoreIntent",
                    "UndoRestoreIntent" => "SourceRestored",
                    "SourceRestored" => "UndoOutputArchiveIntent",
                    "UndoOutputArchiveIntent" => "Undone",
                    _ => null
                };
                if (item.State!=old.State && item.State!=next &&
                    !(item.State=="NeedsAttention" && old.State!="Undone"))
                    throw new InvalidDataException("Illegal Move journal state transition: "+old.State+" -> "+item.State);
            }
            cmd.Parameters.Clear();
            cmd.CommandText="INSERT INTO move_items(id,session,sequence,json) VALUES($id,$session,$sequence,$json) " +
                "ON CONFLICT(id) DO UPDATE SET json=excluded.json";
            cmd.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
            cmd.Parameters.AddWithValue("$sequence",item.Sequence);
            cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }
}
