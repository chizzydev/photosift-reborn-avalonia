using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>Inspection is NOT permission. The digest only detects changed
/// SQLite bytes; the independent native inode/archive ownership check is what
/// prevents a copied or re-created file being treated as restored evidence.
/// </summary>
public sealed record RejectTerminalReceiptEvidence(bool Verified,int Sealed,int Historical,
    string Head,string Reason);

public sealed partial class WorkspaceSessionStore
{
    private sealed record RejectBytes(string Id,string Session,string Json,DurableRejectItem Row);
    private sealed record RejectReceipt(int Ordinal,string Install,string Id,string Session,
        string Json,string Previous,string Digest);
    private sealed record VerifiedRejectPrefix(int Count,string Head);

    private Guid RejectInstallId(SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT token FROM installation_identity WHERE id=1";
        if(!Guid.TryParseExact(cmd.ExecuteScalar() as string,"N",out var id) || id==Guid.Empty)
            throw new InvalidDataException("Reject installation identity unavailable.");
        return id;
    }
    private List<RejectBytes> ReadRejectBytes(SqliteTransaction? tx=null)
    {
        var installation=RejectInstallId(tx);
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT id,session,json FROM reject_items ORDER BY rowid";
        using var rd=cmd.ExecuteReader();var rows=new List<RejectBytes>();
        while(rd.Read())
        {
            string id=rd.GetString(0), session=rd.GetString(1), json=rd.GetString(2);
            if(json.Length>32768) throw new IOException("Oversized Reject journal; no successor.");
            DurableRejectItem row;
            try{row=JsonSerializer.Deserialize<DurableRejectItem>(json) ??
                throw new IOException("Missing Reject journal item.");}
            catch(JsonException ex){throw new IOException("Malformed Reject journal item.",ex);}
            if(row.Id==Guid.Empty || row.Id.ToString("N")!=id ||
               row.SessionId.ToString("N")!=session ||
               row.InstallationId!=installation)
                throw new IOException("Reject journal primary identity changed.");
            rows.Add(new RejectBytes(id,session,json,row));
        }
        return rows;
    }
    private List<RejectReceipt> ReadRejectReceipts(SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT ordinal,installation,reject_id,reject_session,reject_json,"+
            "previous_digest,receipt_digest FROM reject_terminal_receipts ORDER BY ordinal";
        using var rd=cmd.ExecuteReader();var rows=new List<RejectReceipt>();
        while(rd.Read())
        {
            string json=rd.GetString(4);
            if(json.Length>32768) throw new IOException("Oversized Reject terminal receipt.");
            rows.Add(new RejectReceipt(checked((int)rd.GetInt64(0)),rd.GetString(1),rd.GetString(2),
                rd.GetString(3),json,rd.GetString(5),rd.GetString(6)));
        }
        return rows;
    }
    private static string Digest(int ordinal,string install,string id,string session,string json,string prior)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var field in new[]{"PhotoSiftReborn.RejectTerminalReceipt.v1",
            ordinal.ToString(CultureInfo.InvariantCulture),install,id,session,json,prior})
        {
            var bytes=Encoding.UTF8.GetBytes(field);
            var prefix=BitConverter.GetBytes(bytes.Length);
            if(!BitConverter.IsLittleEndian)Array.Reverse(prefix);
            hash.AppendData(prefix);hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private void AssertRejectOnly(SqliteTransaction? tx=null)
    {
        if(HasUnreconciledCopy) throw new IOException("Outstanding Copy barrier blocks Reject epochs.");
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT (SELECT COUNT(*) FROM copy_items)+"+
            "(SELECT COUNT(*) FROM move_items)+(SELECT COUNT(*) FROM rename_items)+"+
            "(SELECT COUNT(*) FROM rename_closeout)";
        if(Convert.ToInt64(cmd.ExecuteScalar())!=0)
            throw new IOException("Foreign Copy/Move/Rename history blocks Reject successor.");
    }
    private VerifiedRejectPrefix VerifyRejectTerminalReceipts(SqliteTransaction? tx,
        bool requireAll,string? ordinarySourceRoot=null)
    {
        AssertRejectOnly(tx);
        var history=ReadRejectBytes(tx);var receipts=ReadRejectReceipts(tx);
        if(receipts.Count>history.Count || (requireAll && receipts.Count!=history.Count))
            throw new IOException("Reject terminal receipt count is not the exact predecessor history.");
        string previous="GENESIS";
        string install=RejectInstallId(tx).ToString("N");
        var sessions=new HashSet<Guid>();var photos=new HashSet<Guid>();
        var sources=new HashSet<string>(SortingWorkspace.PathComparer);
        var archives=new HashSet<string>(SortingWorkspace.PathComparer);
        var native=ordinarySourceRoot is null
            ? new DurableRejectCoordinator(this)
            : DurableRejectCoordinator.ForOrdinaryHistoricalEvidence(this,ordinarySourceRoot);
        foreach(var (receipt,index) in receipts.Select((r,i)=>(r,i)))
        {
            var data=history[index];var row=data.Row;
            bool nativeTerminal=ordinarySourceRoot is null
                ? native.ProveTerminalUndone(row,Guid.ParseExact(install,"N"))
                : native.ProveTerminalUndone(row,Guid.ParseExact(install,"N"),historicalOnly:true);
            if(receipt.Ordinal!=index+1 || receipt.Install!=install ||
               receipt.Id!=data.Id || receipt.Session!=data.Session ||
               receipt.Json!=data.Json || receipt.Previous!=previous ||
               receipt.Digest!=Digest(index+1,install,data.Id,data.Session,data.Json,previous) ||
               row.State!="Undone" || !sessions.Add(row.SessionId) ||
               !photos.Add(row.PhotoId) || !sources.Add(row.Source) ||
               !archives.Add(row.Archive) ||
               (ordinarySourceRoot is not null &&
                !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(row.Source),ordinarySourceRoot)) ||
               !nativeTerminal)
                throw new IOException("Historical Reject receipt/native Undo evidence changed; no new epoch.");
            previous=receipt.Digest;
        }
        return new VerifiedRejectPrefix(receipts.Count,previous);
    }
    public RejectTerminalReceiptEvidence InspectRejectTerminalReceipts(int requiredPrefix=0)
    {
        lock(_dbGate)
        {
            int rows=0,sealedRows=0;
            try
            {
                rows=ReadRejectBytes().Count;sealedRows=ReadRejectReceipts().Count;
                var verified=VerifyRejectTerminalReceipts(null,false);
                if(sealedRows<requiredPrefix || sealedRows==0)
                    return new RejectTerminalReceiptEvidence(false,sealedRows,rows,verified.Head,
                        "No sufficient independent terminal Reject receipt; no new authorization.");
                return new RejectTerminalReceiptEvidence(true,sealedRows,rows,verified.Head,
                    "Verified historical Reject receipt prefix; not a grant.");
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or OverflowException)
            {return new RejectTerminalReceiptEvidence(false,sealedRows,rows,"",
                "Reject receipt inspection refused: "+ex.Message);}
        }
    }
    public RejectTerminalReceiptEvidence SealRejectTerminalHistory()
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            AssertRejectOnly();
            var before=ReadRejectBytes();
            if(before.Count==0 || before.Any(x=>x.Row.State!="Undone"))
                throw new IOException("Only fully Undone Reject history can receive terminal receipts.");
            using var tx=_connection.BeginTransaction();
            AssertRejectOnly(tx);
            var history=ReadRejectBytes(tx);
            if(history.Count!=before.Count || history.Where((x,i)=>x!=before[i]).Any())
                throw new IOException("Reject journal changed before receipt append.");
            var prefix=VerifyRejectTerminalReceipts(tx,false);
            var install=RejectInstallId(tx).ToString("N");
            // Enforce unique source/photo/session for the WHOLE prospective chain.
            var sessions=new HashSet<Guid>();var photos=new HashSet<Guid>();
            var sources=new HashSet<string>(SortingWorkspace.PathComparer);
            var archives=new HashSet<string>(SortingWorkspace.PathComparer);
            var engine=new DurableRejectCoordinator(this);
            foreach(var item in history)
                if(!sessions.Add(item.Row.SessionId) || !photos.Add(item.Row.PhotoId) ||
                   !sources.Add(item.Row.Source) || !archives.Add(item.Row.Archive) ||
                   !engine.ProveTerminalUndone(item.Row,Guid.ParseExact(install,"N")))
                    throw new IOException("Unverified or repeated native Reject predecessor; cannot seal.");
            string head=prefix.Head;
            for(int i=prefix.Count;i<history.Count;i++)
            {
                var item=history[i];var digest=Digest(i+1,install,item.Id,item.Session,item.Json,head);
                using var insert=_connection.CreateCommand();insert.Transaction=tx;
                insert.CommandText="INSERT INTO reject_terminal_receipts (ordinal,installation,"+
                    "reject_id,reject_session,reject_json,previous_digest,receipt_digest) "+
                    "VALUES($ord,$install,$id,$session,$json,$head,$digest)";
                insert.Parameters.AddWithValue("$ord",i+1);insert.Parameters.AddWithValue("$install",install);
                insert.Parameters.AddWithValue("$id",item.Id);insert.Parameters.AddWithValue("$session",item.Session);
                insert.Parameters.AddWithValue("$json",item.Json);insert.Parameters.AddWithValue("$head",head);
                insert.Parameters.AddWithValue("$digest",digest);
                if(insert.ExecuteNonQuery()!=1)throw new IOException("Reject terminal receipt append refused.");
                head=digest;
            }
            tx.Commit();
            var checkedReceipt=InspectRejectTerminalReceipts(before.Count);
            if(!checkedReceipt.Verified || checkedReceipt.Sealed!=before.Count)
                throw new IOException("Terminal Reject receipt needs inspection; no authorization inferred.");
            return checkedReceipt;
        }
    }

    // P6F2D: ordinary-folder terminal history CLOSEOUT, not successor consent.
    // This produces independently native-verified, append-only receipts using
    // the same chain digest/schema as the fixture lane. Personal-photo GUI
    // remains disabled. Never delete the restored source or the owned markers.
    private string RequireOrdinaryTerminalRoot()
    {
        if(P5CGuiFixturePolicy.IsMarkedStore(this))
            throw new IOException("Ordinary terminal seal cannot use the old marked fixture lane.");
        var snap=Load() ?? throw new IOException("No saved ordinary workspace for terminal closeout.");
        string? root=snap.SourceRoot;
        if(root is null || snap.SourceRoots is not {Length:1} ||
           !SortingWorkspace.PathComparer.Equals(snap.SourceRoots[0],root) ||
           snap.DestinationRoot is not { } output || !Directory.Exists(output) ||
           SortingWorkspace.HasLinkedExistingAncestor(output) ||
           snap.Photos is not {Length:>0} || snap.Photos.Any(p=>
               !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(p.Path),root)))
            throw new IOException("Ordinary terminal closeout requires a saved single flat source and separate output.");
        // Reuse the same canonical path/root and state-directory protections as
        // the ordinary live coordinator; no fixture marker or environment token.
        _=DurableRejectCoordinator.ForOrdinaryHistoricalEvidence(this,root);
        static bool InsideOrSame(string child,string parent)
        {
            var rel=Path.GetRelativePath(parent,child);
            return rel=="." || (!Path.IsPathRooted(rel) && rel!=".." &&
                !rel.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
        }
        if(InsideOrSame(root,output) || InsideOrSame(output,root))
            throw new IOException("Ordinary terminal source/output overlap.");
        return root;
    }

    public RejectTerminalReceiptEvidence InspectOrdinaryRejectTerminalReceipts(int requiredPrefix=0)
    {
        lock(_dbGate)
        {
            int rows=0,sealedRows=0;
            try
            {
                var root=RequireOrdinaryTerminalRoot();
                rows=ReadRejectBytes().Count;sealedRows=ReadRejectReceipts().Count;
                var verified=VerifyRejectTerminalReceipts(null,false,root);
                if(sealedRows<requiredPrefix || sealedRows==0)
                    return new RejectTerminalReceiptEvidence(false,sealedRows,rows,verified.Head,
                        "Ordinary history not terminally sealed; inspection is not authorization.");
                return new RejectTerminalReceiptEvidence(true,sealedRows,rows,verified.Head,
                    "Ordinary native restored originals and immutable receipts verified; no successor grant.");
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or OverflowException)
            {return new RejectTerminalReceiptEvidence(false,sealedRows,rows,"",
                "Ordinary receipt inspection refused: "+ex.Message);}
        }
    }

    public RejectTerminalReceiptEvidence SealOrdinaryRejectTerminalHistory()
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            var root=RequireOrdinaryTerminalRoot();
            AssertRejectOnly();
            var before=ReadRejectBytes();
            if(before.Count==0 || before.Any(x=>x.Row.State!="Undone"))
                throw new IOException("Only fully Undone ordinary Reject history may be sealed.");
            // Snapshot must be the same session as the latest journal record,
            // not an arbitrary newly imported folder with stale old data.
            if(Load()!.SessionId!=before[^1].Row.SessionId)
                throw new IOException("Current ordinary session is not the terminal Reject session.");
            using var tx=_connection.BeginTransaction();
            AssertRejectOnly(tx);
            var history=ReadRejectBytes(tx);
            if(history.Count!=before.Count || history.Where((x,i)=>x!=before[i]).Any())
                throw new IOException("Ordinary Reject history changed before sealing.");
            var prefix=VerifyRejectTerminalReceipts(tx,false,root);
            var install=RejectInstallId(tx).ToString("N");
            var sessions=new HashSet<Guid>();var photos=new HashSet<Guid>();
            var sources=new HashSet<string>(SortingWorkspace.PathComparer);
            var archives=new HashSet<string>(SortingWorkspace.PathComparer);
            var engine=DurableRejectCoordinator.ForOrdinaryHistoricalEvidence(this,root);
            foreach(var item in history)
            {
                var r=item.Row;
                if(!sessions.Add(r.SessionId) || !photos.Add(r.PhotoId) ||
                   !sources.Add(r.Source) || !archives.Add(r.Archive) ||
                   !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(r.Source),root) ||
                   !engine.ProveTerminalUndone(r,Guid.ParseExact(install,"N"),historicalOnly:true))
                    throw new IOException("Ordinary predecessor native ownership/identity/refused during closeout.");
            }
            string head=prefix.Head;
            for(int i=prefix.Count;i<history.Count;i++)
            {
                var item=history[i];var digest=Digest(i+1,install,item.Id,item.Session,item.Json,head);
                using var insert=_connection.CreateCommand();insert.Transaction=tx;
                insert.CommandText="INSERT INTO reject_terminal_receipts (ordinal,installation,"+
                    "reject_id,reject_session,reject_json,previous_digest,receipt_digest) "+
                    "VALUES($ord,$install,$id,$session,$json,$head,$digest)";
                insert.Parameters.AddWithValue("$ord",i+1);insert.Parameters.AddWithValue("$install",install);
                insert.Parameters.AddWithValue("$id",item.Id);insert.Parameters.AddWithValue("$session",item.Session);
                insert.Parameters.AddWithValue("$json",item.Json);insert.Parameters.AddWithValue("$head",head);
                insert.Parameters.AddWithValue("$digest",digest);
                if(insert.ExecuteNonQuery()!=1)throw new IOException("Ordinary terminal receipt append failed.");
                head=digest;
            }
            tx.Commit();
            var checkedReceipt=InspectOrdinaryRejectTerminalReceipts(before.Count);
            if(!checkedReceipt.Verified || checkedReceipt.Sealed!=before.Count)
                throw new IOException("Ordinary receipt needs manual inspection; no continuation permission.");
            return checkedReceipt;
        }
    }

    public void GrantNextRejectStage(DurableRejectItem staged,bool explicitAcknowledgement,
        string? ordinarySourceRoot=null)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            bool fixture=P5CGuiFixturePolicy.IsMarkedStore(this);
            if(!explicitAcknowledgement || staged.State!="Staged" || staged.Error is not null ||
               (fixture && ordinarySourceRoot is not null) ||
               (!fixture && (ordinarySourceRoot is null ||
                  !SortingWorkspace.PathComparer.Equals(RequireOrdinaryTerminalRoot(),ordinarySourceRoot) ||
                  !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(staged.Source),ordinarySourceRoot) ||
                  Load()?.SessionId!=staged.SessionId)))
                throw new IOException("Successor Reject requires exact ordinary scope or fixture and fresh explicit acknowledgement.");
            AssertRejectOnly();
            var history=ReadRejectBytes();
            if(history.Count<1 || history.Any(x=>x.Row.State!="Undone"))
                throw new IOException("Only terminal historical Reject allows successor consideration.");
            var chain=VerifyRejectTerminalReceipts(null,true,ordinarySourceRoot);
            if(history.Any(x=>x.Row.SessionId==staged.SessionId ||
                x.Row.PhotoId==staged.PhotoId ||
                SortingWorkspace.PathComparer.Equals(x.Row.Source,staged.Source) ||
                SortingWorkspace.PathComparer.Equals(x.Row.Archive,staged.Archive)))
                throw new IOException("Successor Reject must use a distinct photo, session and recovery archive.");
            using var tx=_connection.BeginTransaction();
            var recheck=ordinarySourceRoot is null
                ? VerifyRejectTerminalReceipts(tx,true)
                : VerifyRejectTerminalReceipts(tx,true,ordinarySourceRoot);
            if(recheck.Head!=chain.Head || recheck.Count!=chain.Count)
                throw new IOException("Reject predecessor receipts changed during grant.");
            using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO reject_successor_grants(reject_id,previous_digest,staged_json,used) "+
                "VALUES($id,$head,$json,0)";
            cmd.Parameters.AddWithValue("$id",staged.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$head",chain.Head);
            cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(staged));
            if(cmd.ExecuteNonQuery()!=1)throw new IOException("Reject successor grant not stored.");
            tx.Commit();
        }
    }
}
