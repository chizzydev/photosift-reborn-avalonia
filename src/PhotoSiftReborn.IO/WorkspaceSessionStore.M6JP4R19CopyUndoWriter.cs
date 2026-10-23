using System;
using System.IO;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>Only the exact generated A journal row may transition; no new
/// journal, generic PutCopyItem bypass, history deletion or interrupted replay.</summary>
public sealed partial class WorkspaceSessionStore
{
    internal void PutM6JP4R19GeneratedAUndo(DurableCopyItem old,DurableCopyItem next,
        DurableMoveItem b,DurableRenameItem c,DurableRejectItem d)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(old is null || next is null || b is null || c is null || d is null ||
                old.State is not ("Committed" or "UndoIntent") ||
                next.State!=(old.State=="Committed"?"UndoIntent":"Undone") ||
                (old with {State=next.State})!=next ||
                old.Id==Guid.Empty || old.SessionId==Guid.Empty ||
                old.Error is not null || next.Error is not null ||
                b.State!="Undone" || c.State!="Undone" || d.State!="Undone" ||
                b.Error is not null || c.Error is not null || d.Error is not null ||
                b.InstallationId!=MoveInstallationId() ||
                b.InstallationId!=c.InstallationId || b.InstallationId!=d.InstallationId ||
                old.SessionId!=b.SessionId || old.SessionId!=c.SessionId || old.SessionId!=d.SessionId ||
                old.PhotoId==b.PhotoId || b.PhotoId!=c.PhotoId || c.PhotoId!=d.PhotoId ||
                b.Destination!=c.Source || c.Target!=d.Source ||
                b.TargetIdentity!=c.NativeIdentity || c.NativeIdentity!=d.SourceIdentity ||
                AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy)
                throw new IOException("P4-R19 illegal one-row generated Copy A Undo transition.");
            using var tx=_connection.BeginTransaction();
            static (string Id,string Session,string Json) Raw(
                Microsoft.Data.Sqlite.SqliteConnection connection,
                Microsoft.Data.Sqlite.SqliteTransaction transaction,string table)
            {
                if(table is not ("copy_items" or "move_items" or "rename_items" or "reject_items"))
                    throw new IOException("Unexpected journal table.");
                using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
                cmd.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                using var reader=cmd.ExecuteReader();
                if(!reader.Read())throw new IOException("Journal missing.");
                var row=(reader.GetString(0),reader.GetString(1),reader.GetString(2));
                if(reader.Read() || row.Item3.Length>32768)throw new IOException("Journal ambiguous.");
                return row;
            }
            var ar=Raw(_connection,tx,"copy_items");
            var br=Raw(_connection,tx,"move_items");
            var cr=Raw(_connection,tx,"rename_items");
            var dr=Raw(_connection,tx,"reject_items");
            bool Row((string Id,string Session,string Json) row,Guid id,Guid session)
                => row.Id==id.ToString("N") && row.Session==session.ToString("N");
            if(!Row(ar,old.Id,old.SessionId) || ar.Json!=JsonSerializer.Serialize(old) ||
                !Row(br,b.Id,b.SessionId) || br.Json!=JsonSerializer.Serialize(b) ||
                !Row(cr,c.Id,c.SessionId) || cr.Json!=JsonSerializer.Serialize(c) ||
                !Row(dr,d.Id,d.SessionId) || dr.Json!=JsonSerializer.Serialize(d))
                throw new IOException("P4-R19 immutable predecessor changed during CAS.");
            using var put=_connection.CreateCommand();put.Transaction=tx;
            put.CommandText="UPDATE copy_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            put.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            put.Parameters.AddWithValue("$id",ar.Id);
            put.Parameters.AddWithValue("$session",ar.Session);
            put.Parameters.AddWithValue("$old",ar.Json);
            if(put.ExecuteNonQuery()!=1)throw new IOException("P4-R19 A journal CAS failed.");
            tx.Commit();
        }
    }
}
