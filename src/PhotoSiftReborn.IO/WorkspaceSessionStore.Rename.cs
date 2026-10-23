using System.Text.Json;

namespace PhotoSiftReborn;

public sealed record DurableRenameItem(Guid Id,Guid InstallationId,Guid SessionId,Guid PhotoId,
    long Revision,long Sequence,string Source,string Target,string Hash,string NativeIdentity,
    long Length,long WriteTicks,string State,string? Error,string? ReviewedDirectoryIdentity=null,
    Guid? SupersedesRenameId=null);

public sealed partial class WorkspaceSessionStore
{
    public int AllRenameHistoryCount()
    {
        lock (_dbGate)
        {
            using var c=_connection.CreateCommand();
            c.CommandText="SELECT COUNT(*) FROM rename_items";
            return Convert.ToInt32(c.ExecuteScalar());
        }
    }

    public IReadOnlyList<DurableRenameItem> RenameItems(Guid session)
    {
        lock (_dbGate)
        {
            using var c=_connection.CreateCommand();
            c.CommandText="SELECT json FROM rename_items WHERE session=$session ORDER BY sequence,id";
            c.Parameters.AddWithValue("$session",session.ToString("N"));
            using var r=c.ExecuteReader();
            var result=new List<DurableRenameItem>();
            while (r.Read())
            {
                var json=r.GetString(0);
                if (json.Length>16384) throw new InvalidDataException("Oversized Rename journal row.");
                var item=JsonSerializer.Deserialize<DurableRenameItem>(json)
                    ?? throw new InvalidDataException("Malformed Rename journal row.");
                if (item.SessionId!=session || item.Id==Guid.Empty || item.InstallationId!=MoveInstallationId())
                    throw new InvalidDataException("Rename journal identity mismatch.");
                result.Add(item);
            }
            return result;
        }
    }

    private static bool RenameJournalPathOverlap(string a,string b)
        => string.Equals(a.Normalize(System.Text.NormalizationForm.FormC),
            b.Normalize(System.Text.NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase);

    public void PutRenameItem(DurableRenameItem item)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if (item.Id==Guid.Empty || item.PhotoId==Guid.Empty || item.SessionId==Guid.Empty ||
                item.InstallationId!=MoveInstallationId() || item.Hash.Length!=64 ||
                string.IsNullOrWhiteSpace(item.NativeIdentity) || item.Length<1 || item.WriteTicks<1 ||
                !Path.IsPathFullyQualified(item.Source) || !Path.IsPathFullyQualified(item.Target) ||
                (item.SupersedesRenameId==Guid.Empty) ||
                (item.ReviewedDirectoryIdentity is not null &&
                 (item.ReviewedDirectoryIdentity.Length<3 || item.ReviewedDirectoryIdentity.Length>160)) ||
                !new[]{"Intent","Committed","UndoIntent","Undone","NeedsAttention"}.Contains(item.State))
                throw new InvalidDataException("Invalid Rename journal identity or state.");
            if (AllRejectHistoryCount()!=0)
                throw new IOException("Reject recovery history blocks Rename journal writes.");
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="SELECT json FROM rename_items WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            var previous=cmd.ExecuteScalar() as string;
            if(previous is null)
            {
                using(var closeoutCmd=_connection.CreateCommand())
                {
                    closeoutCmd.Transaction=tx;
                    closeoutCmd.CommandText="SELECT COUNT(*) FROM rename_closeout WHERE id=1";
                    if(Convert.ToInt64(closeoutCmd.ExecuteScalar())!=0)
                        throw new IOException("Rename journal epoch is closed; begin a separately authorized session for more Rename operations.");
                }
                if(item.State!="Intent") throw new InvalidDataException("Rename requires durable Intent first.");
                // Keep every completed row forever, but permit a distinct PHOTO
                // after previous operations are fully committed or undone.
                // The database serializes writers and rejects unresolved history.
                using var historyCmd=_connection.CreateCommand();
                historyCmd.Transaction=tx;
                historyCmd.CommandText="SELECT json FROM rename_items ORDER BY sequence,id";
                long lastSequence=0;
                var oldRows=new List<DurableRenameItem>();
                using(var reader=historyCmd.ExecuteReader())
                {
                    while(reader.Read())
                    {
                        var json=reader.GetString(0);
                        if(json.Length>16384) throw new InvalidDataException("Oversized prior Rename row.");
                        var oldRow=JsonSerializer.Deserialize<DurableRenameItem>(json)
                            ?? throw new InvalidDataException("Malformed prior Rename row.");
                        if(oldRow.SessionId!=item.SessionId || oldRow.InstallationId!=item.InstallationId ||
                           oldRow.State is not ("Committed" or "Undone"))
                            throw new IOException("Rename journal has another session or unresolved history.");
                        oldRows.Add(oldRow);
                        if(oldRow.Sequence>lastSequence) lastSequence=oldRow.Sequence;
                    }
                }
                var samePhoto=oldRows.Where(r=>r.PhotoId==item.PhotoId).ToArray();
                if(samePhoto.Length==0)
                {
                    if(item.SupersedesRenameId is not null)
                        throw new IOException("Cannot supersede an absent historical Rename.");
                }
                else
                {
                    var last=samePhoto[^1];
                    if(item.SupersedesRenameId!=last.Id || last.State!="Undone" ||
                       item.ReviewedDirectoryIdentity is null ||
                       last.ReviewedDirectoryIdentity!=item.ReviewedDirectoryIdentity ||
                       item.NativeIdentity!=last.NativeIdentity || item.Hash!=last.Hash ||
                       item.Length!=last.Length || item.WriteTicks!=last.WriteTicks ||
                       !RenameJournalPathOverlap(last.Source,item.Source))
                        throw new IOException("Repeated Rename must directly supersede a verified undone generation.");
                }
                foreach(var oldRow in oldRows)
                {
                    if(oldRow.PhotoId==item.PhotoId) continue;
                    if(oldRow.NativeIdentity==item.NativeIdentity ||
                       RenameJournalPathOverlap(oldRow.Source,item.Source) ||
                       RenameJournalPathOverlap(oldRow.Source,item.Target) ||
                       RenameJournalPathOverlap(oldRow.Target,item.Source) ||
                       RenameJournalPathOverlap(oldRow.Target,item.Target))
                        throw new IOException("Rename path/photo belongs to another historical operation.");
                }
                if(item.Sequence<=lastSequence)
                    throw new IOException("Rename journal sequence must increase monotonically.");
            }
            else
            {
                var old=JsonSerializer.Deserialize<DurableRenameItem>(previous)
                    ?? throw new InvalidDataException("Malformed existing Rename row.");
                if(old with {State=item.State,Error=item.Error} != item)
                    throw new InvalidDataException("Rename frozen paths, identity or hash changed.");
                string? next=old.State switch {
                    "Intent"=>"Committed", "Committed"=>"UndoIntent", "UndoIntent"=>"Undone", _=>null
                };
                if(item.State!=old.State && item.State!=next &&
                   !(item.State=="NeedsAttention" && old.State!="Undone"))
                    throw new InvalidDataException("Illegal Rename state transition: "+old.State+" -> "+item.State);
            }
            cmd.Parameters.Clear();
            cmd.CommandText="INSERT INTO rename_items(id,session,sequence,json) VALUES($id,$session,$sequence,$json) "+
                "ON CONFLICT(id) DO UPDATE SET json=excluded.json";
            cmd.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
            cmd.Parameters.AddWithValue("$sequence",item.Sequence);
            cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
            cmd.ExecuteNonQuery();tx.Commit();
        }
    }
}
