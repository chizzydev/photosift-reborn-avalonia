using System;
using System.IO;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R16: isolated, generated-only, one-row D journal transition.
/// Neither ordinary PutRejectItem nor the P4-R12 forward-only writer changes.
/// Strict SQL compare-and-swap; never changes Copy/Move/Rename history.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    internal void PutM6JP4R16GeneratedRejectUndo(DurableRejectItem current,
        DurableRejectItem next)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(current is null || next is null || current.Id==Guid.Empty ||
                current.SessionId==Guid.Empty || current.InstallationId!=MoveInstallationId() ||
                current.InstallationId!=next.InstallationId ||
                current.State is not ("Committed" or "UndoIntent") ||
                next.State!=(current.State=="Committed" ? "UndoIntent" : "Undone") ||
                current.Error is not null || next.Error is not null ||
                (current with { State=next.State }) != next ||
                AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy)
                throw new IOException("P4-R16 exact fourth-family D Undo transition refused.");
            using var tx=_connection.BeginTransaction();
            using var get=_connection.CreateCommand();get.Transaction=tx;
            get.CommandText="SELECT id,session,json FROM reject_items LIMIT 2";
            using var rd=get.ExecuteReader();
            if(!rd.Read()) throw new IOException("P4-R16 D row missing.");
            var id=rd.GetString(0);var session=rd.GetString(1);var oldJson=rd.GetString(2);
            if(rd.Read() || oldJson.Length>32768 ||
                id!=current.Id.ToString("N") || session!=current.SessionId.ToString("N") ||
                oldJson!=JsonSerializer.Serialize(current))
                throw new IOException("P4-R16 exact persisted D predecessor bytes changed.");
            rd.Close();
            using var update=_connection.CreateCommand();update.Transaction=tx;
            update.CommandText="UPDATE reject_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            update.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            update.Parameters.AddWithValue("$id",id);
            update.Parameters.AddWithValue("$session",session);
            update.Parameters.AddWithValue("$old",oldJson);
            if(update.ExecuteNonQuery()!=1)
                throw new IOException("P4-R16 durable single-row compare-and-swap failed.");
            tx.Commit();
        }
    }
}
