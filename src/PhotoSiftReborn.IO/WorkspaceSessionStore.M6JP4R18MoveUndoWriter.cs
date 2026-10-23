using System;
using System.IO;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>P4-R18: exact generated-only four-family B history CAS. No new rows,
/// no journal reset, no generic PutMoveItem bypass, no re-entry to intermediate
/// states and no automatic recovery after an interruption.</summary>
public sealed partial class WorkspaceSessionStore
{
    internal void PutM6JP4R18GeneratedBUndo(DurableMoveItem current,DurableMoveItem next,
        DurableCopyItem a,DurableRenameItem c,DurableRejectItem d)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(current is null || next is null || a is null || c is null || d is null)
                throw new IOException("P4-R18 missing one of the exact predecessor rows.");
            string? nextState=current.State switch {
                "Committed"=>"UndoRestoreIntent",
                "UndoRestoreIntent"=>"SourceRestored",
                "SourceRestored"=>"UndoOutputArchiveIntent",
                "UndoOutputArchiveIntent"=>"Undone",
                _=>null
            };
            if(nextState is null || next.State!=nextState ||
                (current with {State=next.State})!=next ||
                current.Error is not null || next.Error is not null ||
                a.State!="Committed" || c.State!="Undone" || d.State!="Undone" ||
                a.Error is not null || c.Error is not null || d.Error is not null ||
                current.Id==Guid.Empty || current.InstallationId!=MoveInstallationId() ||
                current.SessionId==Guid.Empty || current.SessionId!=a.SessionId ||
                current.SessionId!=c.SessionId || current.SessionId!=d.SessionId ||
                current.InstallationId!=c.InstallationId ||
                current.InstallationId!=d.InstallationId ||
                current.Destination!=c.Source || c.Target!=d.Source ||
                current.TargetIdentity!=c.NativeIdentity ||
                c.NativeIdentity!=d.SourceIdentity ||
                current.PhotoId!=c.PhotoId || c.PhotoId!=d.PhotoId ||
                current.PhotoId==a.PhotoId ||
                AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                CountAuxiliaryHistoryRows()!=0 || HasUnreconciledCopy)
                throw new IOException("P4-R18 illegal generated B Undo transition.");
            using var tx=_connection.BeginTransaction();
            static (string Id,string Session,string Json) Raw(
                Microsoft.Data.Sqlite.SqliteConnection db,
                Microsoft.Data.Sqlite.SqliteTransaction t,string family)
            {
                if(family is not ("copy_items" or "move_items" or "rename_items" or "reject_items"))
                    throw new IOException("Unknown journal family.");
                using var q=db.CreateCommand();q.Transaction=t;
                q.CommandText="SELECT id,session,json FROM "+family+" LIMIT 2";
                using var read=q.ExecuteReader();
                if(!read.Read())throw new IOException("Missing journal row.");
                var row=(read.GetString(0),read.GetString(1),read.GetString(2));
                if(read.Read() || row.Item3.Length>32768)throw new IOException("Journal non-unique or oversized.");
                return row;
            }
            var ar=Raw(_connection,tx,"copy_items");
            var br=Raw(_connection,tx,"move_items");
            var cr=Raw(_connection,tx,"rename_items");
            var dr=Raw(_connection,tx,"reject_items");
            bool Row((string Id,string Session,string Json) raw,Guid id,Guid session)
                => raw.Id==id.ToString("N") && raw.Session==session.ToString("N");
            if(!Row(ar,a.Id,a.SessionId) || ar.Json!=JsonSerializer.Serialize(a) ||
                !Row(br,current.Id,current.SessionId) || br.Json!=JsonSerializer.Serialize(current) ||
                !Row(cr,c.Id,c.SessionId) || cr.Json!=JsonSerializer.Serialize(c) ||
                !Row(dr,d.Id,d.SessionId) || dr.Json!=JsonSerializer.Serialize(d))
                throw new IOException("P4-R18 prior exact persisted row changed.");
            using var put=_connection.CreateCommand();put.Transaction=tx;
            put.CommandText="UPDATE move_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            put.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            put.Parameters.AddWithValue("$id",br.Id);
            put.Parameters.AddWithValue("$session",br.Session);
            put.Parameters.AddWithValue("$old",br.Json);
            if(put.ExecuteNonQuery()!=1)throw new IOException("P4-R18 durable one-row B CAS failed.");
            tx.Commit();
        }
    }
}
