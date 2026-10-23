using System;
using System.IO;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>Exact generated Copy A row CAS after a separately reviewed Move B
/// has become durably Undone. Generic Copy history vetoes are unchanged.</summary>
public sealed partial class WorkspaceSessionStore
{
    internal void PutM6JP4R23GeneratedAUndo(DurableCopyItem old,DurableCopyItem next,DurableMoveItem b)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(old is null || next is null || b is null ||
               old.State is not ("Committed" or "UndoIntent") ||
               next.State!=(old.State=="Committed"?"UndoIntent":"Undone") ||
               (old with {State=next.State})!=next ||
               old.Id==Guid.Empty || old.SessionId==Guid.Empty || old.PhotoId==b.PhotoId ||
               old.Error is not null || next.Error is not null ||
               b.State!="Undone" || b.Error is not null ||
               b.InstallationId!=MoveInstallationId() || old.SessionId!=b.SessionId ||
               AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
               AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
               CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy || HasAnyCopySuccessorGrant)
                throw new IOException("R23 illegal generated two-family Copy Undo transition.");
            using var tx=_connection.BeginTransaction();
            static (string Id,string Session,string Json) Raw(
                Microsoft.Data.Sqlite.SqliteConnection cn,
                Microsoft.Data.Sqlite.SqliteTransaction transaction,string table)
            {
                if(table is not ("copy_items" or "move_items"))
                    throw new IOException("R23 unexpected CAS table.");
                using var cmd=cn.CreateCommand();cmd.Transaction=transaction;
                cmd.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                using var rd=cmd.ExecuteReader();
                if(!rd.Read())throw new IOException("R23 CAS history missing.");
                var row=(rd.GetString(0),rd.GetString(1),rd.GetString(2));
                if(rd.Read() || row.Item3.Length>32768)throw new IOException("R23 CAS history ambiguous.");
                return row;
            }
            var ar=Raw(_connection,tx,"copy_items");
            var br=Raw(_connection,tx,"move_items");
            if(ar.Id!=old.Id.ToString("N") || ar.Session!=old.SessionId.ToString("N") ||
               ar.Json!=JsonSerializer.Serialize(old) || br.Id!=b.Id.ToString("N") ||
               br.Session!=b.SessionId.ToString("N") || br.Json!=JsonSerializer.Serialize(b))
                throw new IOException("R23 CAS original review predecessor changed.");
            using var update=_connection.CreateCommand();update.Transaction=tx;
            update.CommandText="UPDATE copy_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            update.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            update.Parameters.AddWithValue("$id",ar.Id);
            update.Parameters.AddWithValue("$session",ar.Session);
            update.Parameters.AddWithValue("$old",ar.Json);
            if(update.ExecuteNonQuery()!=1)throw new IOException("R23 exact Copy A row CAS failed.");
            tx.Commit();
        }
    }
}
