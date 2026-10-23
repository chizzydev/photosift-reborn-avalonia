using System;
using System.IO;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R17 one-row C Undo CAS after terminal D Undone. Never bypass the
/// general PutRenameItem Reject-history veto, which remains unchanged.
/// No new family rows, replay, GUI grant, or general mixed-history write.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    internal void PutM6JP4R17GeneratedRenameUndoAfterReject(
        DurableRenameItem current, DurableRenameItem next, DurableRejectItem dUndone)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(current is null || next is null || dUndone is null ||
                current.Id==Guid.Empty || current.SessionId==Guid.Empty ||
                current.InstallationId!=MoveInstallationId() ||
                current.InstallationId!=next.InstallationId ||
                current.InstallationId!=dUndone.InstallationId ||
                current.SessionId!=next.SessionId || current.SessionId!=dUndone.SessionId ||
                dUndone.State!="Undone" || dUndone.Error is not null ||
                dUndone.Source!=current.Target ||
                dUndone.SourceIdentity!=current.NativeIdentity ||
                dUndone.SourceHash!=current.Hash ||
                dUndone.SourceLength!=current.Length ||
                dUndone.SourceWriteTicks!=current.WriteTicks ||
                current.State is not ("Committed" or "UndoIntent") ||
                next.State!=(current.State=="Committed" ? "UndoIntent" : "Undone") ||
                current.Error is not null || next.Error is not null ||
                (current with { State=next.State })!=next ||
                AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy)
                throw new IOException("P4-R17 exact C after D terminal Undo transition refused.");
            using var tx=_connection.BeginTransaction();
            static (string Id,string Session,string Json) Raw(
                Microsoft.Data.Sqlite.SqliteConnection db,
                Microsoft.Data.Sqlite.SqliteTransaction tx,string table)
            {
                using var get=db.CreateCommand(); get.Transaction=tx;
                get.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                using var rd=get.ExecuteReader();
                if(!rd.Read()) throw new IOException("P4-R17 journal row missing.");
                var row=(rd.GetString(0),rd.GetString(1),rd.GetString(2));
                if(rd.Read() || row.Item3.Length>32768)
                    throw new IOException("P4-R17 journal row not unique or oversized.");
                return row;
            }
            var c=Raw(_connection,tx,"rename_items");
            var d=Raw(_connection,tx,"reject_items");
            if(c.Id!=current.Id.ToString("N") || c.Session!=current.SessionId.ToString("N") ||
                c.Json!=JsonSerializer.Serialize(current) ||
                d.Id!=dUndone.Id.ToString("N") || d.Session!=dUndone.SessionId.ToString("N") ||
                d.Json!=JsonSerializer.Serialize(dUndone))
                throw new IOException("P4-R17 exact C/D historical persisted records changed.");
            using var update=_connection.CreateCommand();update.Transaction=tx;
            update.CommandText="UPDATE rename_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            update.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            update.Parameters.AddWithValue("$id",c.Id);
            update.Parameters.AddWithValue("$session",c.Session);
            update.Parameters.AddWithValue("$old",c.Json);
            if(update.ExecuteNonQuery()!=1)
                throw new IOException("P4-R17 durable C single-row CAS failed.");
            tx.Commit();
        }
    }
}
