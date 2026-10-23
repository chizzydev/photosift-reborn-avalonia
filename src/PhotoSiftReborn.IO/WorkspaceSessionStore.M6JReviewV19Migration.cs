using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// M6J-A3-P2A-R1: fail-closed migration of immutable A2 reviewed proposals.
/// Only this migration changes schema. Existing Intent writers, filesystem
/// mutation authority, and native Undo remain byte-for-byte unchanged.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    private static readonly string M6JReviewV18TableSql =
        "CREATE TABLE m6j_review_proposals ("+
        "id TEXT PRIMARY KEY, installation TEXT NOT NULL, session TEXT NOT NULL,"+
        "family TEXT NOT NULL, candidate_json TEXT NOT NULL, candidate_digest TEXT NOT NULL,"+
        "history_digest TEXT NOT NULL, checkpoint_digest TEXT NOT NULL,"+
        "state TEXT NOT NULL CHECK(state IN ('Staged','Reviewed','Cancelled')),"+
        "review_digest TEXT)";
    private const string M6JReviewV18IndexSql =
        "CREATE UNIQUE INDEX m6j_only_one_open_review "+
        "ON m6j_review_proposals(installation) WHERE state IN ('Staged','Reviewed')";

    private void UpgradeM6JReviewSchemaToV19(int originalVersion)
    {
        // The v18 table and unique partial index were created by the exact
        // frozen A2 migration. Reject altered/unknown layouts, not just rows.
        using (var schema = _connection.CreateCommand())
        {
            schema.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='m6j_review_proposals'";
            if (!string.Equals(schema.ExecuteScalar() as string,M6JReviewV18TableSql,StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected v18 review table definition; refusing v19 migration.");
            schema.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name='m6j_only_one_open_review'";
            if (!string.Equals(schema.ExecuteScalar() as string,M6JReviewV18IndexSql,StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected v18 unique-review index; refusing migration.");
            schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE tbl_name='m6j_review_proposals' AND type IN ('trigger','index') AND name!='m6j_only_one_open_review' AND name NOT LIKE 'sqlite_autoindex_%'";
            if (Convert.ToInt64(schema.ExecuteScalar())!=0)
                throw new InvalidDataException("Unexpected review triggers/indexes; refusing migration.");
            // These review rows have no foreign keys by contract. Refuse an
            // unknown referencing table rather than dropping its target.
            schema.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name!='m6j_review_proposals' AND sql LIKE '%REFERENCES m6j_review_proposals%'";
            if (schema.ExecuteScalar() is not null)
                throw new InvalidDataException("Unknown foreign review reference; refusing migration.");
        }
        // Validate immutable digests prior to creating any backup or altering
        // the DB. A cancelled row may carry a former Reviewed seal; a staged
        // row may not. Do not require photographs still to be on disk.
        long expectedRows=0;
        using (var read=_connection.CreateCommand())
        {
            read.CommandText="SELECT id,installation,session,family,candidate_json,candidate_digest,"+
                "history_digest,checkpoint_digest,state,review_digest FROM m6j_review_proposals";
            using var rows=read.ExecuteReader();
            while(rows.Read())
            {
                if(++expectedRows>100000)
                    throw new InvalidDataException("Unbounded review rows; refusing v19 migration.");
                string id=rows.GetString(0),installation=rows.GetString(1),session=rows.GetString(2),
                    family=rows.GetString(3),candidate=rows.GetString(4),digest=rows.GetString(5),
                    history=rows.GetString(6),checkpoint=rows.GetString(7),state=rows.GetString(8);
                string? seal=rows.IsDBNull(9)?null:rows.GetString(9);
                if(id.Length!=32 || installation.Length!=32 || session.Length!=32 ||
                   family is not ("Copy" or "Move" or "Rename" or "Reject") ||
                   candidate.Length>32768 || digest!=M6JHash(candidate) ||
                   history.Length!=64 || checkpoint.Length!=64 ||
                   state is not ("Staged" or "Reviewed" or "Cancelled") ||
                   (state=="Staged" && seal is not null) ||
                   (state=="Reviewed" && seal!=M6JReviewDigest(digest,history,checkpoint)) ||
                   (state=="Cancelled" && seal is not null &&
                    seal!=M6JReviewDigest(digest,history,checkpoint)))
                    throw new InvalidDataException("Inconsistent v18 review row; refusing v19 migration.");
            }
        }
        if(originalVersion>=1)
        {
            string backup=DatabasePath+".before-v19-"+
                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".sqlite";
            if(File.Exists(backup))
                throw new IOException("v19 migration backup path already exists.");
            using var target=new SqliteConnection(new SqliteConnectionStringBuilder
            {DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
            target.Open();
            _connection.BackupDatabase(target);
            using var proof=target.CreateCommand();
            proof.CommandText="PRAGMA user_version;";
            if(Convert.ToInt32(proof.ExecuteScalar())!=18)
                throw new InvalidDataException("v19 backup is not a consistent v18 predecessor.");
            proof.CommandText="PRAGMA quick_check;";
            if(!string.Equals(Convert.ToString(proof.ExecuteScalar()),"ok",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("v19 backup integrity check failed.");
        }
        // SQLite cannot ALTER the existing CHECK; build the replacement
        // side-by-side, copy all columns, compare rows, and swap inside a
        // single atomic transaction. The uniqueness constraint is recreated.
        using var tx=_connection.BeginTransaction();
        using var cmd=_connection.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="CREATE TABLE m6j_review_proposals_v19 ("+
            "id TEXT PRIMARY KEY, installation TEXT NOT NULL, session TEXT NOT NULL,"+
            "family TEXT NOT NULL, candidate_json TEXT NOT NULL, candidate_digest TEXT NOT NULL,"+
            "history_digest TEXT NOT NULL, checkpoint_digest TEXT NOT NULL,"+
            "state TEXT NOT NULL CHECK(state IN ('Staged','Reviewed','Cancelled','Consumed')),"+
            "review_digest TEXT)";
        cmd.ExecuteNonQuery();
        const string cols="id,installation,session,family,candidate_json,candidate_digest,"+
            "history_digest,checkpoint_digest,state,review_digest";
        cmd.CommandText="INSERT INTO m6j_review_proposals_v19("+cols+") SELECT "+cols+
            " FROM m6j_review_proposals";
        if(cmd.ExecuteNonQuery()!=expectedRows)
            throw new InvalidDataException("Review migration copy count changed.");
        cmd.CommandText="SELECT COUNT(*) FROM (SELECT "+cols+
            " FROM m6j_review_proposals EXCEPT SELECT "+cols+
            " FROM m6j_review_proposals_v19)";
        if(Convert.ToInt64(cmd.ExecuteScalar())!=0)
            throw new InvalidDataException("Review migration did not preserve exact row contents.");
        cmd.CommandText="DROP TABLE m6j_review_proposals";
        cmd.ExecuteNonQuery();
        cmd.CommandText="ALTER TABLE m6j_review_proposals_v19 RENAME TO m6j_review_proposals";
        cmd.ExecuteNonQuery();
        cmd.CommandText=M6JReviewV18IndexSql;
        cmd.ExecuteNonQuery();
        cmd.CommandText="PRAGMA user_version=19";
        cmd.ExecuteNonQuery();
        cmd.CommandText="SELECT COUNT(*) FROM m6j_review_proposals";
        if(Convert.ToInt64(cmd.ExecuteScalar())!=expectedRows)
            throw new InvalidDataException("Review migration lost or duplicated rows.");
        cmd.CommandText="PRAGMA foreign_key_check";
        using(var foreign=cmd.ExecuteReader())
            if(foreign.Read())throw new InvalidDataException("Foreign key error during review migration.");
        tx.Commit();
    }
}
