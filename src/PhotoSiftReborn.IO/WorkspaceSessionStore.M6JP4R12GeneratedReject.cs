using System;
using System.IO;
using System.Text.Json;

namespace PhotoSiftReborn;

public sealed partial class WorkspaceSessionStore
{
    // P4-R12: an isolated generated-only FOUR-family journal writer. The
    // existing public PutRejectItem mixed-history veto remains unchanged.
    // Exactly one Copy/Move/Rename history, exactly one fresh Reject operation;
    // no epoch rollover, no generic grant, no replay after an uncertain state.
    internal void PutM6JP4R12GeneratedReject(DurableRejectItem row,string? priorState)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(row is null || row.Id==Guid.Empty || row.SessionId==Guid.Empty ||
                row.PhotoId==Guid.Empty || row.InstallationId!=MoveInstallationId() ||
                row.SourceLength<1 || row.SourceWriteTicks<=0 ||
                row.SourceHash.Length!=64 || string.IsNullOrWhiteSpace(row.SourceIdentity) ||
                !Path.IsPathFullyQualified(row.Source) || !Path.IsPathFullyQualified(row.Archive) ||
                AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                AllRenameHistoryCount()!=1 || CountAuxiliaryHistoryRows()!=0 ||
                HasUnreconciledCopy)
                throw new IOException("P4-R12 generated four-family journal scope refused.");
            bool initial=priorState is null;
            bool valid=initial ? row.State=="Intent" && row.Error is null :
                (priorState=="Intent" && row.State=="RelocateIntent" && row.Error is null) ||
                (priorState=="RelocateIntent" && row.State=="Committed" && row.Error is null) ||
                ((priorState=="Intent" || priorState=="RelocateIntent") &&
                    row.State=="NeedsAttention" && !string.IsNullOrWhiteSpace(row.Error));
            if(!valid || row.Source==row.Archive ||
                !string.Equals(Path.GetDirectoryName(row.Source),Path.GetDirectoryName(row.Archive),
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(row.Archive).StartsWith(
                    ".photosift-reborn-generated-reject-"+row.Id.ToString("N"),StringComparison.Ordinal))
                throw new IOException("P4-R12 generated Reject state transition is not authorized.");
            using var tx=_connection.BeginTransaction();
            using var count=_connection.CreateCommand();count.Transaction=tx;
            count.CommandText="SELECT COUNT(*) FROM reject_items";
            if(Convert.ToInt64(count.ExecuteScalar())!=(initial?0L:1L))
                throw new IOException("P4-R12 another Reject journal already exists.");
            string? priorJson=null;
            if(!initial)
            {
                using var get=_connection.CreateCommand();get.Transaction=tx;
                get.CommandText="SELECT json FROM reject_items WHERE id=$id";
                get.Parameters.AddWithValue("$id",row.Id.ToString("N"));
                priorJson=get.ExecuteScalar() as string;
                if(priorJson is null || priorJson.Length>32768)
                    throw new IOException("P4-R12 exact previous Reject row missing.");
                var old=JsonSerializer.Deserialize<DurableRejectItem>(priorJson)
                    ?? throw new IOException("P4-R12 invalid previous Reject row.");
                if(old.State!=priorState || old.Error is not null ||
                    old.Id!=row.Id || old.InstallationId!=row.InstallationId ||
                    old.SessionId!=row.SessionId || old.PhotoId!=row.PhotoId ||
                    old.SessionRevision!=row.SessionRevision ||
                    old.Source!=row.Source || old.Archive!=row.Archive ||
                    old.SourceHash!=row.SourceHash || old.SourceIdentity!=row.SourceIdentity ||
                    old.SourceLength!=row.SourceLength || old.SourceWriteTicks!=row.SourceWriteTicks)
                    throw new IOException("P4-R12 previous Reject row was modified or superseded.");
            }
            using var put=_connection.CreateCommand();put.Transaction=tx;
            put.CommandText=initial
                ? "INSERT INTO reject_items(id,session,json) VALUES($id,$session,$json)"
                : "UPDATE reject_items SET json=$json WHERE id=$id";
            put.Parameters.AddWithValue("$id",row.Id.ToString("N"));
            put.Parameters.AddWithValue("$json",JsonSerializer.Serialize(row));
            if(initial)put.Parameters.AddWithValue("$session",row.SessionId.ToString("N"));
            if(put.ExecuteNonQuery()!=1)
                throw new IOException("P4-R12 Reject journal write did not affect one row.");
            tx.Commit();
        }
    }
}
