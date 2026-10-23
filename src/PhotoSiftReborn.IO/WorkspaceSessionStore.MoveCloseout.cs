using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// A P6C2-B1 closeout is a durable, database-local *historical checkpoint*.
/// Even Verified=true is NOT a license to create Move intents or mutate photos.
/// The digest is not signed; database tampering and hostile concurrent writers
/// are outside this bounded native evidence model.
/// </summary>
public sealed record TerminalMoveCloseoutEvidence(bool Present, bool Verified,
    int RecordedRows, string Reason);

public sealed partial class WorkspaceSessionStore
{
    private static void DigestField(IncrementalHash hash, string field)
    {
        byte[] data=Encoding.UTF8.GetBytes(field);
        Span<byte> prefix=stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix,data.Length);
        hash.AppendData(prefix);
        hash.AppendData(data);
    }

    // Compute a domain-separated, length-prefixed digest over the exact SQLite
    // journal bytes and ordering. All rows (not only current-session rows) bind.
    // Caller holds _dbGate; tx is provided when the seal is inserted.
    private (int Count, string Digest) TerminalMoveJournalDigest(string install,
        SqliteTransaction? tx=null)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        DigestField(hash,"PhotoSiftReborn.TerminalMoveCloseout.v1");
        DigestField(hash,install);
        using var cmd=_connection.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT id,session,sequence,json FROM move_items ORDER BY sequence,id";
        using var reader=cmd.ExecuteReader();
        int count=0;
        while (reader.Read())
        {
            string id=reader.GetString(0), session=reader.GetString(1);
            long sequence=reader.GetInt64(2);
            string json=reader.GetString(3);
            if (json.Length>32768) throw new InvalidDataException("Move row too large to close out.");
            DigestField(hash,id);
            DigestField(hash,session);
            DigestField(hash,sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            DigestField(hash,json);
            count=checked(count+1);
        }
        return (count,Convert.ToHexString(hash.GetHashAndReset()));
    }

    /// <summary>
    /// Read-only revalidation of both the stored seal and current B0 native
    /// evidence. Missing/stale/tampered evidence never implies permission.
    /// </summary>
    public TerminalMoveCloseoutEvidence InspectTerminalMoveCloseout()
    {
        lock (_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT installation,journal_digest,row_count FROM move_closeout WHERE id=1";
            using var reader=cmd.ExecuteReader();
            if (!reader.Read())
                return new TerminalMoveCloseoutEvidence(false,false,0,
                    "No terminal Move closeout; no subsequent Move authorization.");
            string installed=reader.GetString(0), digest=reader.GetString(1);
            int count=checked((int)reader.GetInt64(2));
            reader.Close();
            var installation=MoveInstallationId().ToString("N");
            if (count<=0 || installed!=installation || digest.Length!=64)
                return new TerminalMoveCloseoutEvidence(true,false,count,
                    "Closeout identity/count/digest is invalid.");
            var proof=InspectTerminalMoveHistory();
            if (!proof.CanConsiderLaterAuthorization || proof.VerifiedUndoRows!=count ||
                proof.MoveRows!=count)
                return new TerminalMoveCloseoutEvidence(true,false,count,
                    "Historical native ownership or dependent operation journals no longer verify.");
            var current=TerminalMoveJournalDigest(installation);
            if (current.Count!=count || !string.Equals(current.Digest,digest,StringComparison.Ordinal))
                return new TerminalMoveCloseoutEvidence(true,false,count,
                    "Exact historical Move journal differs from the durable closeout.");
            return new TerminalMoveCloseoutEvidence(true,true,count,
                "Native history and journal seal currently agree. NOT executable Move authority.");
        }
    }

    /// <summary>
    /// Persist an idempotent seal only when ALL historic Moves are terminal
    /// with native archive/source ownership. No Move item, photo, plan or
    /// recovery path is written, and the SQLite Move Intent veto is unchanged.
    /// </summary>
    public TerminalMoveCloseoutEvidence SealTerminalMoveHistory()
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            var proof=InspectTerminalMoveHistory();
            if (!proof.CanConsiderLaterAuthorization || proof.MoveRows<=0 ||
                proof.VerifiedUndoRows!=proof.MoveRows)
                throw new IOException("Terminal Move closeout refused: incomplete, mixed or unproved history.");
            string installation=MoveInstallationId().ToString("N");
            var before=TerminalMoveJournalDigest(installation);
            if (before.Count!=proof.MoveRows)
                throw new IOException("Terminal Move closeout refused: journal changed during proof.");
            using var tx=_connection.BeginTransaction();
            using (var counts=_connection.CreateCommand())
            {
                counts.Transaction=tx;
                counts.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM rename_closeout),"+
                    "(SELECT copy_risk FROM workspace_checkpoint WHERE id=1)";
                using var r=counts.ExecuteReader();
                if (!r.Read() || r.GetInt64(0)!=0 || r.GetInt64(1)!=proof.MoveRows ||
                    r.GetInt64(2)!=0 || r.GetInt64(3)!=0 ||
                    (!r.IsDBNull(4) && r.GetInt64(4)!=0))
                    throw new IOException("Move closeout refused: installation history or recovery barrier changed.");
            }
            var inside=TerminalMoveJournalDigest(installation,tx);
            if (inside!=before)
                throw new IOException("Move closeout refused: history differs from proof.");
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="SELECT installation,journal_digest,row_count FROM move_closeout WHERE id=1";
            using (var reader=cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    if (reader.GetString(0)!=installation ||
                        reader.GetString(1)!=inside.Digest || reader.GetInt64(2)!=inside.Count)
                        throw new IOException("Existing Move closeout changed; never overwrite evidence.");
                    // Fully verified idempotent operation, no update.
                }
                else
                {
                    reader.Close();
                    cmd.CommandText="INSERT INTO move_closeout(id,installation,journal_digest,row_count) "+
                        "VALUES(1,$installation,$digest,$count)";
                    cmd.Parameters.AddWithValue("$installation",installation);
                    cmd.Parameters.AddWithValue("$digest",inside.Digest);
                    cmd.Parameters.AddWithValue("$count",inside.Count);
                    cmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
            var verified=InspectTerminalMoveCloseout();
            if (!verified.Verified)
                throw new IOException("Terminal Move closeout could not be reverified; no authority granted.");
            return verified;
        }
    }
}
