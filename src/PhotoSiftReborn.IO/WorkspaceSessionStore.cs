using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// P3A durable *decisions* checkpoint. Copy recovery is NOT implemented here:
/// barrier records any possibly outstanding P2 copy and refuses unjournalled replay after restart.
/// P3B will replace this barrier with per-operation intent/identity reconciliation.
/// </summary>
public sealed partial class WorkspaceSessionStore : IDisposable
{
    private readonly object _dbGate = new();
    private readonly FileStream _lock;
    private readonly SqliteConnection _connection;
    private bool _disposed;
    public string DatabasePath { get; }
    public bool HasUnreconciledCopy { get; private set; }

    public static string DefaultDirectory()
    {
        string? @override = Environment.GetEnvironmentVariable("PHOTOSIFT_REBORN_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(@override)) return Path.GetFullPath(@override);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "PhotoSiftReborn");
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                : xdg, "PhotoSiftReborn");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoSiftReborn");
    }

    public WorkspaceSessionStore(string? directory = null)
    {
        var folder = Path.GetFullPath(directory ?? DefaultDirectory());
        Directory.CreateDirectory(folder);
        DatabasePath = Path.Combine(folder, "workspaces-v1.sqlite");
        _lock = new FileStream(Path.Combine(folder,"workspaces-v1.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            _connection.Open();
            // Detect damaged/downgraded enrollment before persistent pragmas,
            // backup or migration. Keep the connection available for read-only
            // HOLD inspection; no old schema writer may reinterpret this store.
            bool r30ReadOnly=R30EnrollmentDetected();
            using (var cmd = _connection.CreateCommand())
            {
                // Connection-local safety settings remain mandatory for reviewed
                // R30 writers after cold open. Only the persistent journal-mode
                // change and legacy migration are suppressed by enrollment.
                cmd.CommandText = (r30ReadOnly ? "" : "PRAGMA journal_mode=DELETE; ") +
                    "PRAGMA synchronous=EXTRA; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                int version = Convert.ToInt32(cmd.ExecuteScalar());
                if (version > 20) throw new InvalidDataException("Newer workspace database schema; refusing to overwrite.");
                // Refuse to migrate an already-damaged database. Even a
                // well-formed new table must not hide broken historical rows.
                cmd.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(Convert.ToString(cmd.ExecuteScalar()),"ok",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Existing SQLite integrity check failed; no migration attempted.");
                if(!r30ReadOnly)
                {
                if (version < 1)
                {
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS workspace_checkpoint (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL, copy_risk INTEGER NOT NULL DEFAULT 0); PRAGMA user_version=1;";
                    cmd.ExecuteNonQuery();
                }
                if (version < 2)
                {
                    // P3A version 1 may contain real saved work. Preserve a SQLite-consistent
                    // backup before changing the schema. Never reset the P3A copy-risk flag.
                    if (version == 1)
                    {
                        string backup = DatabasePath + ".before-v2-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sqlite";
                        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup, Mode=SqliteOpenMode.ReadWriteCreate, Pooling=false }.ToString());
                        destination.Open();
                        _connection.BackupDatabase(destination);
                    }
                    using var tx = _connection.BeginTransaction();
                    using var migrator = _connection.CreateCommand();
                    migrator.Transaction=tx;
                    migrator.CommandText = "CREATE TABLE IF NOT EXISTS copy_items (id TEXT PRIMARY KEY, session TEXT NOT NULL, sequence INTEGER NOT NULL, json TEXT NOT NULL);";
                    migrator.ExecuteNonQuery();
                    migrator.CommandText = "CREATE INDEX IF NOT EXISTS copy_items_session_sequence ON copy_items(session, sequence);";
                    migrator.ExecuteNonQuery();
                    migrator.CommandText = "PRAGMA user_version=2;";
                    migrator.ExecuteNonQuery();
                    tx.Commit();
                }
                // P4D: route records gain Kind/Location in the same single JSON
                // checkpoint authority. Backup v1/v2 DB before advancing the schema.
                if (version < 3)
                {
                    if (version >= 1)
                    {
                        string backup = DatabasePath + ".before-v3-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sqlite";
                        using var target = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup, Mode=SqliteOpenMode.ReadWriteCreate, Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx = _connection.BeginTransaction();
                    using var advance = _connection.CreateCommand();
                    advance.Transaction=tx;
                    advance.CommandText = "PRAGMA user_version=3;";
                    advance.ExecuteNonQuery();
                    tx.Commit();
                }
                // P5B: append-only Move journal authority, separate from Copy.
                // Every existing v1-v3 database is backed up before this upgrade.
                if (version < 4)
                {
                    if (version >= 1)
                    {
                        string backup = DatabasePath + ".before-v4-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sqlite";
                        using var target = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup, Mode=SqliteOpenMode.ReadWriteCreate, Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx = _connection.BeginTransaction();
                    using var migrator = _connection.CreateCommand();
                    migrator.Transaction=tx;
                    migrator.CommandText = "CREATE TABLE IF NOT EXISTS move_items (id TEXT PRIMARY KEY, session TEXT NOT NULL, sequence INTEGER NOT NULL, json TEXT NOT NULL);";
                    migrator.ExecuteNonQuery();
                    migrator.CommandText = "CREATE INDEX IF NOT EXISTS move_items_session_sequence ON move_items(session,sequence);";
                    migrator.ExecuteNonQuery();
                    migrator.CommandText = "CREATE TABLE IF NOT EXISTS installation_identity (id INTEGER PRIMARY KEY CHECK(id=1), token TEXT NOT NULL);";
                    migrator.ExecuteNonQuery();
                    migrator.CommandText = "INSERT OR IGNORE INTO installation_identity(id,token) VALUES(1,$identity);";
                    migrator.Parameters.AddWithValue("$identity",Guid.NewGuid().ToString("N"));
                    migrator.ExecuteNonQuery();
                    migrator.CommandText = "PRAGMA user_version=4;";
                    migrator.ExecuteNonQuery();
                    tx.Commit();
                }
                // P5D: separately journalled Rename. Do not change saved-photo snapshot schema.
                if (version < 5)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v5-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS rename_items (id TEXT PRIMARY KEY, session TEXT NOT NULL, sequence INTEGER NOT NULL, json TEXT NOT NULL);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="CREATE INDEX IF NOT EXISTS rename_items_session_sequence ON rename_items(session,sequence);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=5;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C1: immutable verified Rename closeout is the ONLY authority to
                // return to Copy after every Rename was undone. Existing v5 state
                // is SQLite-backed-up first; never drop any Rename/Copy record.
                if (version < 6)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v6-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS rename_closeout (id INTEGER PRIMARY KEY CHECK(id=1), session TEXT NOT NULL, digest TEXT NOT NULL, row_count INTEGER NOT NULL CHECK(row_count>0));";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=6;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C2-B1: optional, explicitly requested, independently checked
                // terminal Move-history closeout. This records a digest of the
                // exact past journal and DOES NOT grant new Move execution rights.
                // Never alter v1-v6 tables or delete old journal evidence.
                if (version < 7)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v7-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS move_closeout ("+
                        "id INTEGER PRIMARY KEY CHECK(id=1), installation TEXT NOT NULL, "+
                        "journal_digest TEXT NOT NULL, row_count INTEGER NOT NULL CHECK(row_count>0));";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=7;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C2-B2: append-only proposed epoch ticket; it is a read-only
                // preregistration, NOT a Move Intent grant or execution permit.
                if (version < 8)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v8-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS move_epoch_ticket ("+
                        "id INTEGER PRIMARY KEY CHECK(id=1),installation TEXT NOT NULL,"+
                        "predecessor_digest TEXT NOT NULL, predecessor_rows INTEGER NOT NULL CHECK(predecessor_rows>0),"+
                        "candidate_json TEXT NOT NULL, candidate_digest TEXT NOT NULL);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=8;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C2-B3: one immutable grant per staged successor, bound
                // to the exact B2 ticket. Consumed in the SAME transaction
                // that inserts the next Move Intent. The grant cannot roll
                // forward to third/later operations, nor authorize GUI Move.
                if (version < 9)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v9-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS move_epoch_grant ("+
                        "id INTEGER PRIMARY KEY CHECK(id=1), installation TEXT NOT NULL,"+
                        "candidate_digest TEXT NOT NULL, predecessor_digest TEXT NOT NULL,"+
                        "predecessor_rows INTEGER NOT NULL CHECK(predecessor_rows>0),"+
                        "state TEXT NOT NULL CHECK(state IN ('Approved','Consumed')));";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=9;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C2-B4A: immutable predecessor checkpoint + one exact fixture
                // successor session. This record is NOT Move Intent authority;
                // the independent B2/B3 ticket/grant are still mandatory.
                if (version < 10)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v10-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS move_gui_successor ("+
                        "id INTEGER PRIMARY KEY CHECK(id=1), installation TEXT NOT NULL,"+
                        "prior_session TEXT NOT NULL, next_session TEXT NOT NULL,"+
                        "prior_snapshot TEXT NOT NULL, prior_move_json TEXT NOT NULL,"+
                        "predecessor_digest TEXT NOT NULL, predecessor_rows INTEGER NOT NULL CHECK(predecessor_rows=1),"+
                        "candidate_photo_id TEXT NOT NULL, candidate_path TEXT NOT NULL,"+
                        "candidate_identity TEXT NOT NULL, candidate_hash TEXT NOT NULL,"+
                        "candidate_length INTEGER NOT NULL, candidate_ticks INTEGER NOT NULL);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=10;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C2-B4B0: append-only, per-Move immutable terminal receipts.
                // This is NOT an execution permit and cannot spend a grant.
                // Migration NEVER auto-seals historical operations: a later
                // explicit call must prove native ownership for every ancestor.
                if (version < 11)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v11-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE move_epoch_ledger ("+
                        "ordinal INTEGER PRIMARY KEY CHECK(ordinal>0),"+
                        "installation TEXT NOT NULL, move_id TEXT NOT NULL UNIQUE,"+
                        "move_session TEXT NOT NULL UNIQUE, move_json TEXT NOT NULL,"+
                        "previous_digest TEXT NOT NULL, receipt_digest TEXT NOT NULL UNIQUE);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=11;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6C2-B4B1A: independent immutable per-epoch authorization records.
                // Existing v8/v9 singleton ticket/grant and v10 GUI transition
                // remain untouched. No implicit approval or legacy backfill.
                if (version < 12)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v12-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE move_epoch_authorization ("+
                        "epoch INTEGER PRIMARY KEY CHECK(epoch>=3),installation TEXT NOT NULL,"+
                        "predecessor_rows INTEGER NOT NULL CHECK(predecessor_rows>=2),"+
                        "ledger_head TEXT NOT NULL,candidate_json TEXT NOT NULL,"+
                        "candidate_digest TEXT NOT NULL UNIQUE,"+
                        "state TEXT NOT NULL CHECK(state IN ('Staged','Approved','Consumed')),"+
                        "CHECK(epoch=predecessor_rows+1));";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=12;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // B4B1B: atomic, immutable third-GUI session transition.
                // This row is NOT Move authorization; v12 per-epoch tickets/grants
                // remain exclusively responsible for a later Move Intent.
                if (version < 13)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v13-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE move_gui_third_session ("+
                        "id INTEGER PRIMARY KEY CHECK(id=1),installation TEXT NOT NULL,"+
                        "prior_session TEXT NOT NULL,next_session TEXT NOT NULL UNIQUE,"+
                        "prior_snapshot TEXT NOT NULL,predecessor_rows INTEGER NOT NULL CHECK(predecessor_rows=2),"+
                        "ledger_head TEXT NOT NULL,candidate_photo_id TEXT NOT NULL,"+
                        "candidate_path TEXT NOT NULL,candidate_identity TEXT NOT NULL,"+
                        "candidate_hash TEXT NOT NULL,candidate_length INTEGER NOT NULL,"+
                        "candidate_ticks INTEGER NOT NULL);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=13;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // P6D2A: a separate, native, no-delete Reject recovery journal.
                // No old Reject history exists and migration does not authorize
                // filesystem operations. SQLite-consistent backup before change.
                if (version < 14)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v14-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        {DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
                        target.Open();_connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE reject_items ("+
                        "id TEXT PRIMARY KEY, session TEXT NOT NULL, json TEXT NOT NULL);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=14;";
                    migration.ExecuteNonQuery();tx.Commit();
                }
                // P6E2A: immutable, native-verified Reject successor receipt chain.
                // A receipt is evidence, NEVER approval. An additional separate
                // single-use grant is required before any successor Staged row.
                if (version < 15)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v15-"+
                            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        {DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
                        target.Open();_connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE reject_terminal_receipts ("+
                        "ordinal INTEGER PRIMARY KEY CHECK(ordinal>0),installation TEXT NOT NULL,"+
                        "reject_id TEXT NOT NULL UNIQUE,reject_session TEXT NOT NULL UNIQUE,"+
                        "reject_json TEXT NOT NULL,previous_digest TEXT NOT NULL,"+
                        "receipt_digest TEXT NOT NULL UNIQUE);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="CREATE TABLE reject_successor_grants ("+
                        "reject_id TEXT PRIMARY KEY,previous_digest TEXT NOT NULL UNIQUE,"+
                        "staged_json TEXT NOT NULL,used INTEGER NOT NULL DEFAULT 0 CHECK(used IN (0,1)));";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=15;";
                    migration.ExecuteNonQuery();tx.Commit();
                }
                // P6E2B: immutable GUI checkpoint binding for the SECOND generated Reject.
                // A terminal receipt alone MUST NOT enable a different live GUI workspace.
                if (version < 16)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v16-"+
                            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        {DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
                        target.Open();_connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE reject_gui_second_session ("+
                        "id INTEGER PRIMARY KEY CHECK(id=1),installation TEXT NOT NULL,"+
                        "prior_session TEXT NOT NULL,next_session TEXT NOT NULL UNIQUE,"+
                        "prior_snapshot TEXT NOT NULL,next_snapshot TEXT NOT NULL,"+
                        "receipt_head TEXT NOT NULL,candidate_photo_id TEXT NOT NULL,"+
                        "candidate_path TEXT NOT NULL,candidate_identity TEXT NOT NULL,"+
                        "candidate_hash TEXT NOT NULL,candidate_length INTEGER NOT NULL,"+
                        "candidate_ticks INTEGER NOT NULL);";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=16;";
                    migration.ExecuteNonQuery();tx.Commit();
                }
                // P6G3C-R0: one narrowly scoped, durable, single-use Copy-successor
                // grant. Preserve all older native journals and ledger evidence.
                if (version < 17)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v17-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false }.ToString());
                        target.Open();_connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE IF NOT EXISTS copy_successor_grants ("+
                        "id TEXT PRIMARY KEY, installation TEXT NOT NULL, session TEXT NOT NULL,"+
                        "predecessor_id TEXT NOT NULL, predecessor_digest TEXT NOT NULL,"+
                        "checkpoint_digest TEXT NOT NULL, candidate_json TEXT NOT NULL,"+
                        "used_intent TEXT UNIQUE, state INTEGER NOT NULL CHECK(state IN (0,1)))";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=17;";
                    migration.ExecuteNonQuery();tx.Commit();
                }
                // M6J-A2: durable, non-authorizing exact-review proposals.
                // No existing native Put*Intent branch consults this table.
                // A proposal is NEVER permission to mutate a photograph.
                if (version < 18)
                {
                    if (version >= 1)
                    {
                        string backup=DatabasePath+".before-v18-"+
                            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
                        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
                        {DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
                        target.Open();
                        _connection.BackupDatabase(target);
                    }
                    using var tx=_connection.BeginTransaction();
                    using var migration=_connection.CreateCommand();
                    migration.Transaction=tx;
                    migration.CommandText="CREATE TABLE m6j_review_proposals ("+
                        "id TEXT PRIMARY KEY, installation TEXT NOT NULL, session TEXT NOT NULL,"+
                        "family TEXT NOT NULL, candidate_json TEXT NOT NULL, candidate_digest TEXT NOT NULL,"+
                        "history_digest TEXT NOT NULL, checkpoint_digest TEXT NOT NULL,"+
                        "state TEXT NOT NULL CHECK(state IN ('Staged','Reviewed','Cancelled')),"+
                        "review_digest TEXT)";
                    migration.ExecuteNonQuery();
                    migration.CommandText="CREATE UNIQUE INDEX m6j_only_one_open_review " +
                        "ON m6j_review_proposals(installation) WHERE state IN ('Staged','Reviewed')";
                    migration.ExecuteNonQuery();
                    migration.CommandText="PRAGMA user_version=18;";
                    migration.ExecuteNonQuery();
                    tx.Commit();
                }
                // M6J-A3-P2A-R1: migrate v18 durable review rows to a
                // v19 table that admits a strictly one-way Consumed state.
                // The old v18 CHECK is never disabled. See audited migration.
                if (version < 19)
                    UpgradeM6JReviewSchemaToV19(version);
                }
                cmd.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(Convert.ToString(cmd.ExecuteScalar()),"ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SQLite integrity check failed; preserving original database.");
            }
            using var state = _connection.CreateCommand();
            state.CommandText = "SELECT copy_risk FROM workspace_checkpoint WHERE id=1";
            HasUnreconciledCopy = Convert.ToInt32(state.ExecuteScalar() ?? 0) != 0;
        }
        catch
        {
            _connection?.Dispose();
            _lock.Dispose();
            throw;
        }
    }

    public WorkspaceSessionSnapshot? Load()
    {
        lock (_dbGate)
        {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT json FROM workspace_checkpoint WHERE id=1";
        var json = command.ExecuteScalar() as string;
        if (json is null) return null;
        if (json.Length > 64*1024*1024) throw new InvalidDataException("Workspace checkpoint exceeds allowed size.");
        return JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(json)
               ?? throw new InvalidDataException("Saved sorting session is malformed.");
        }
    }

    public void Save(WorkspaceSessionSnapshot snapshot)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO workspace_checkpoint(id,json,copy_risk) VALUES(1,$json,$risk) " +
                          "ON CONFLICT(id) DO UPDATE SET json=excluded.json,copy_risk=excluded.copy_risk";
        cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(snapshot));
        cmd.Parameters.AddWithValue("$risk",HasUnreconciledCopy ? 1 : 0);
        cmd.ExecuteNonQuery();
        tx.Commit();
        }
    }

    public void SetCopyBarrier(bool possibleCopy, WorkspaceSessionSnapshot snapshot)
    {
        lock(_dbGate)
        {
        RequireNoR30Writer(); // Before changing even the in-memory risk flag.
        // Persist risk status and saved work in ONE transaction, before entering old copy code.
        bool previous = HasUnreconciledCopy;
        HasUnreconciledCopy = possibleCopy;
        try { Save(snapshot); }
        catch { HasUnreconciledCopy = previous; throw; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed=true;
        _connection.Dispose();
        _lock.Dispose();
    }
}
