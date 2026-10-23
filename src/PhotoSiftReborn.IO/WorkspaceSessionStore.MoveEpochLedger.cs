using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// B4B0: read-only/append-only terminal Move receipts, one per chronological
/// operation, native-proved before each append. A VERIFIED ledger is NOT an
/// authorization to stage, approve, or execute another Move. Existing v8/v9/v10
/// singleton ticket/grant/GUI transition remain restrictive and untouched.
/// </summary>
public sealed record MoveEpochLedgerEvidence(bool Present, bool Verified,
    int SealedRows, int MoveRows, string Reason);

public sealed partial class WorkspaceSessionStore
{
    private sealed record EpochEntry(int Ordinal, string Installation,
        string MoveId, string Session, string Json, string Previous, string Digest);
    private sealed record MoveBytes(string Id, string Session, string Json);

    private static string EpochReceiptDigest(int ordinal, string install,
        string id, string session, string json, string parent)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        DigestField(hash,"PhotoSiftReborn.MoveEpochLedger.TerminalReceipt.v1");
        DigestField(hash,ordinal.ToString(CultureInfo.InvariantCulture));
        DigestField(hash,install);
        DigestField(hash,id);
        DigestField(hash,session);
        DigestField(hash,json);
        DigestField(hash,parent);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private List<MoveBytes> OrderedMoveBytes(SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT id,session,json FROM move_items ORDER BY sequence,id";
        using var reader=cmd.ExecuteReader();
        var entries=new List<MoveBytes>();
        while(reader.Read())
        {
            string json=reader.GetString(2);
            if(json.Length>32768) throw new IOException("Move history row oversized; no epoch receipt.");
            entries.Add(new MoveBytes(reader.GetString(0),reader.GetString(1),json));
        }
        return entries;
    }

    private List<EpochEntry> ExistingEpochReceipts(SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT ordinal,installation,move_id,move_session,move_json,"+
            "previous_digest,receipt_digest FROM move_epoch_ledger ORDER BY ordinal";
        using var reader=cmd.ExecuteReader();
        var entries=new List<EpochEntry>();
        while(reader.Read())
        {
            string json=reader.GetString(4);
            if(json.Length>32768) throw new IOException("Epoch ledger row oversized; no new receipt.");
            entries.Add(new EpochEntry(checked((int)reader.GetInt64(0)),
                reader.GetString(1),reader.GetString(2),reader.GetString(3),
                json,reader.GetString(5),reader.GetString(6)));
        }
        return entries;
    }

    private static string VerifyEpochReceiptPrefix(
        IReadOnlyList<EpochEntry> existing,IReadOnlyList<MoveBytes> historical,
        string install,bool verifyNative)
    {
        if(existing.Count>historical.Count)
            throw new IOException("Ledger contains more receipts than Move operations.");
        string parent="GENESIS";
        var sessions=new HashSet<string>(StringComparer.Ordinal);
        var photos=new HashSet<Guid>();
        var sources=new HashSet<string>(SortingWorkspace.PathComparer);
        var destinations=new HashSet<string>(SortingWorkspace.PathComparer);
        var archives=new HashSet<string>(SortingWorkspace.PathComparer);
        for(int i=0;i<existing.Count;i++)
        {
            var row=existing[i];var operation=historical[i];
            if(row.Ordinal!=i+1 || row.Installation!=install ||
                row.MoveId!=operation.Id || row.Session!=operation.Session ||
                row.Json!=operation.Json || row.Previous!=parent ||
                row.Digest!=EpochReceiptDigest(i+1,install,row.MoveId,row.Session,row.Json,parent) ||
                !sessions.Add(row.Session))
                throw new IOException("Immutable epoch chain or exact historical Move bytes differ.");
            DurableMoveItem? native;
            try { native=JsonSerializer.Deserialize<DurableMoveItem>(row.Json); }
            catch(JsonException) { native=null; }
            if(native is null || native.State!="Undone" ||
                native.InstallationId.ToString("N")!=install ||
                native.SessionId.ToString("N")!=row.Session ||
                native.Id.ToString("N")!=row.MoveId ||
                !photos.Add(native.PhotoId) || !sources.Add(native.Source) ||
                !destinations.Add(native.Destination) ||
                !archives.Add(native.OriginalArchive) || !archives.Add(native.OutputArchive) ||
                (verifyNative && !DurableMoveCoordinator.ProveTerminalUndoneEvidence(native)))
                throw new IOException("An epoch receipt lost native evidence or unique path ownership.");
            parent=row.Digest;
        }
        return parent;
    }

    public MoveEpochLedgerEvidence InspectMoveEpochLedger()
    {
        lock(_dbGate)
        {
            int moves=0,receipts=0;
            try
            {
                var historical=OrderedMoveBytes();moves=historical.Count;
                var existing=ExistingEpochReceipts();receipts=existing.Count;
                if(receipts==0)
                    return new MoveEpochLedgerEvidence(false,false,0,moves,
                        "No terminal epoch receipts; no new Move authorization.");
                var counts=AssessFreshMoveEpoch();
                if(HasUnreconciledCopy || counts.CopyRows!=0 || counts.RenameRows!=0 ||
                    counts.RenameCloseouts!=0)
                    return new MoveEpochLedgerEvidence(true,false,receipts,moves,
                        "Foreign journal history blocks epoch evidence.");
                VerifyEpochReceiptPrefix(existing,historical,MoveInstallationId().ToString("N"),true);
                return new MoveEpochLedgerEvidence(true,true,receipts,moves,
                    receipts==moves ? "Entire native terminal history sealed; NO Move authority." :
                        "Verified terminal receipt prefix; later operation not yet sealed. NO Move authority.");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or ArgumentException or
                JsonException or OverflowException)
            { return new MoveEpochLedgerEvidence(receipts>0,false,receipts,moves,
                "Epoch receipt inspection refused: "+ex.Message); }
        }
    }

    /// <summary>
    /// Append terminal receipts only after all existing Move operations are
    /// natively verified undone; old receipts are never updated. SQLite locks,
    /// a journal-digest race barrier, and in-transaction native rechecks bind
    /// the receipt append. This never creates a ticket, grant, or Move Intent.
    /// </summary>
    public MoveEpochLedgerEvidence SealMoveEpochLedger()
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            var proof=InspectTerminalMoveHistory();
            if(!proof.CanConsiderLaterAuthorization || proof.MoveRows<1 ||
                proof.VerifiedUndoRows!=proof.MoveRows)
                throw new IOException("Terminal native Move history required before ledger seal.");
            string install=MoveInstallationId().ToString("N");
            var before=TerminalMoveJournalDigest(install);
            if(before.Count!=proof.MoveRows)
                throw new IOException("Move journal changed before ledger seal.");
            using var tx=_connection.BeginTransaction();
            using(var check=_connection.CreateCommand())
            {
                check.Transaction=tx;
                check.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM rename_closeout),"+
                    "(SELECT copy_risk FROM workspace_checkpoint WHERE id=1)";
                using var rd=check.ExecuteReader();
                if(!rd.Read() || rd.GetInt64(0)!=0 || rd.GetInt64(1)!=before.Count ||
                    rd.GetInt64(2)!=0 || rd.GetInt64(3)!=0 ||
                    (!rd.IsDBNull(4) && rd.GetInt64(4)!=0))
                    throw new IOException("Dependent journals differ during epoch seal.");
            }
            if(TerminalMoveJournalDigest(install,tx)!=before)
                throw new IOException("Move journal changed inside receipt transaction.");
            RequireNativePredecessorsInsideTransaction(tx,before.Count,install);
            var historical=OrderedMoveBytes(tx);
            var existing=ExistingEpochReceipts(tx);
            string parent=VerifyEpochReceiptPrefix(existing,historical,install,true);
            if(historical.Count!=before.Count)
                throw new IOException("History count changed during epoch append.");
            for(int i=existing.Count;i<historical.Count;i++)
            {
                var entry=historical[i];
                var receipt=EpochReceiptDigest(i+1,install,entry.Id,entry.Session,entry.Json,parent);
                using var insert=_connection.CreateCommand();insert.Transaction=tx;
                insert.CommandText="INSERT INTO move_epoch_ledger(ordinal,installation,move_id,"+
                    "move_session,move_json,previous_digest,receipt_digest) " +
                    "VALUES($ordinal,$install,$id,$session,$json,$parent,$digest)";
                insert.Parameters.AddWithValue("$ordinal",i+1);
                insert.Parameters.AddWithValue("$install",install);
                insert.Parameters.AddWithValue("$id",entry.Id);
                insert.Parameters.AddWithValue("$session",entry.Session);
                insert.Parameters.AddWithValue("$json",entry.Json);
                insert.Parameters.AddWithValue("$parent",parent);
                insert.Parameters.AddWithValue("$digest",receipt);
                if(insert.ExecuteNonQuery()!=1)
                    throw new IOException("Failed to append immutable epoch receipt.");
                parent=receipt;
            }
            tx.Commit();
            var verified=InspectMoveEpochLedger();
            if(!verified.Verified || verified.SealedRows!=before.Count ||
                verified.MoveRows!=before.Count)
                throw new IOException("Committed ledger receipt needs inspection; no authorization granted.");
            return verified;
        }
    }
}
