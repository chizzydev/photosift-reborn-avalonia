using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// The all-Undone Rename record remains permanently in SQLite. An explicit
/// closeout, bound to its exact journal digest (not by a user checkbox),
/// permits subsequent Copy operations without erasing prior ownership evidence.
/// Closeout is irreversible: further Rename/Move cannot be appended to this
/// journal epoch. Failed proof refuses all mutations; there is no path fallback.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    private static string CloseoutDigest(IReadOnlyList<DurableRenameItem> rows)
    {
        var canonical=JsonSerializer.Serialize(rows.OrderBy(x=>x.Sequence).ThenBy(x=>x.Id).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void VerifyClosedHistory(IReadOnlyList<DurableRenameItem> rows, Guid session,
        SortingWorkspace? work)
    {
        if (rows.Count==0 || rows.Any(r=>r.SessionId!=session || r.State!="Undone" ||
            r.ReviewedDirectoryIdentity is null))
            throw new IOException("Only fully undone, anchored Rename history can be closed out.");
        foreach(var group in rows.GroupBy(x=>x.PhotoId))
        {
            var chain=group.OrderBy(x=>x.Sequence).ThenBy(x=>x.Id).ToArray();
            for(int i=1;i<chain.Length;i++)
                if(chain[i].SupersedesRenameId!=chain[i-1].Id ||
                   chain[i-1].State!="Undone" ||
                   chain[i].ReviewedDirectoryIdentity!=chain[i-1].ReviewedDirectoryIdentity ||
                   chain[i].NativeIdentity!=chain[i-1].NativeIdentity ||
                   chain[i].Hash!=chain[i-1].Hash ||
                   !SortingWorkspace.PathComparer.Equals(chain[i].Source,chain[i-1].Source))
                    throw new IOException("A historical Rename dependency is inconsistent.");
            var tip=chain[^1];
            if(!DurableRenameCoordinator.ProveState(tip))
                throw new IOException("Final restored Rename source or native identity cannot be proved.");
            if(work is not null)
            {
                var matches=work.Photos.Where(p=>p.Id==tip.PhotoId).ToArray();
                if(matches.Length!=1 ||
                   !SortingWorkspace.PathComparer.Equals(matches[0].Path,tip.Source) ||
                   matches[0].NativeIdentity!=tip.NativeIdentity ||
                   matches[0].Length!=tip.Length ||
                   matches[0].LastWriteUtcTicks!=tip.WriteTicks)
                    throw new IOException("The current workspace is not the verified restored Rename checkpoint.");
            }
        }
    }

    public bool HasRenameCloseout()
    {
        lock(_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM rename_closeout WHERE id=1";
            return Convert.ToInt64(cmd.ExecuteScalar())!=0;
        }
    }

    /// <summary>Checks durable closeout AND fresh native state on each executable Copy.</summary>
    public bool IsRenameCloseoutVerified(Guid session, SortingWorkspace? work=null)
    {
        lock(_dbGate)
        {
            if(HasUnreconciledCopy || AllMoveHistoryCount()!=0) return false;
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT session,digest,row_count FROM rename_closeout WHERE id=1";
            using var reader=cmd.ExecuteReader();
            if(!reader.Read()) return false;
            var recordedSession=reader.GetString(0);
            var digest=reader.GetString(1);
            var count=reader.GetInt32(2);
            reader.Close();
            if(!string.Equals(recordedSession,session.ToString("N"),StringComparison.Ordinal)) return false;
            var rows=RenameItems(session);
            if(rows.Count!=count || rows.Count!=AllRenameHistoryCount() ||
               !string.Equals(digest,CloseoutDigest(rows),StringComparison.Ordinal)) return false;
            try {VerifyClosedHistory(rows,session,work); return true;}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {return false;}
        }
    }

    /// <summary>
    /// Seal exactly once after FULL Rename Undo and workspace checkpoint save.
    /// A crash before this transaction leaves the old safe lock in place.
    /// No existing Copy/Move journal or divergent session can be grandfathered in.
    /// </summary>
    public void SealFullyUndoneRename(SortingWorkspace work)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(HasUnreconciledCopy || AllMoveHistoryCount()!=0 ||
               CopyItems(work.SessionId).Count!=0)
                throw new IOException("Rename closeout requires no Copy/Move operation history.");
            var rows=RenameItems(work.SessionId);
            if(rows.Count==0 || rows.Count!=AllRenameHistoryCount())
                throw new IOException("Rename closeout refuses foreign or empty history.");
            VerifyClosedHistory(rows,work.SessionId,work);
            // Closeout must refer to the SAME durable checkpoint, not an
            // unsaved in-memory collection that could vanish on process exit.
            var saved=Load();
            if(saved is null || saved.SessionId!=work.SessionId ||
               saved.Photos.Length!=work.Photos.Count ||
               saved.Photos.Any(p=>!work.Photos.Any(current=>
                   current.Id==p.Id &&
                   SortingWorkspace.PathComparer.Equals(current.Path,p.Path) &&
                   current.NativeIdentity==p.NativeIdentity)))
                throw new IOException("Persist the exactly restored Rename workspace checkpoint before closeout.");
            var digest=CloseoutDigest(rows);
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="SELECT session,digest,row_count FROM rename_closeout WHERE id=1";
            using(var reader=cmd.ExecuteReader())
            {
                if(reader.Read() && (reader.GetString(0)!=work.SessionId.ToString("N") ||
                    reader.GetString(1)!=digest || reader.GetInt32(2)!=rows.Count))
                    throw new IOException("Earlier closeout differs. Never rewrite closeout evidence.");
            }
            cmd.CommandText="INSERT OR IGNORE INTO rename_closeout(id,session,digest,row_count) VALUES(1,$session,$digest,$count)";
            cmd.Parameters.AddWithValue("$session",work.SessionId.ToString("N"));
            cmd.Parameters.AddWithValue("$digest",digest);
            cmd.Parameters.AddWithValue("$count",rows.Count);
            cmd.ExecuteNonQuery();
            tx.Commit();
            if(!IsRenameCloseoutVerified(work.SessionId,work))
                throw new IOException("Closeout was not verified; keep all operations locked.");
        }
    }
}
