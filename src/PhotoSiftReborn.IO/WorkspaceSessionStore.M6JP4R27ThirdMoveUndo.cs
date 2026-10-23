using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// R27: exact generated-only native Undo of Move C after two separately
/// reviewed committed Copies. Read-only stage observations do not grant
/// ordinary authority or permit automatic continuation of interrupted work.
/// </summary>
public sealed partial class WorkspaceSessionStore
{
    private static bool R27Vacant(string path) => !File.Exists(path) && !Directory.Exists(path);

    private static bool R27Owned(string path,string? identity,string hash,long length,
        long? ticks=null)
    {
        if(identity is null || !File.Exists(path) ||
           SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(path)!) ||
           SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
           !NativeFileCapability.IsSingleLinkRegularFile(path)) return false;
        var info=new FileInfo(path);
        return info.Length==length && (!ticks.HasValue || info.LastWriteTimeUtc.Ticks==ticks.Value) &&
            NativeFileCapability.Identity(path)==identity &&
            SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==hash;
    }

    private static void R27OwnedRecoveryMarkers(DurableMoveItem c)
    {
        string parent=Path.GetDirectoryName(c.Source)!;
        string root=Path.Combine(parent,".photosift-reborn-recovery");
        string operation=Path.Combine(root,c.InstallationId.ToString("N"),
            c.SessionId.ToString("N"),c.Id.ToString("N"));
        string ownedArchive=Path.Combine(operation,Path.GetFileName(c.Source));
        string outputArchive=Path.Combine(operation,"installed-"+Path.GetFileName(c.Destination));
        string temp=Path.Combine(Path.GetDirectoryName(c.Destination)!,
            ".photosift-move-"+c.Id.ToString("N")+".part");
        string rootMarker=Path.Combine(root,".photosift-move-owner-v1");
        string operationMarker=Path.Combine(operation,".photosift-operation-v1");
        if(!SortingWorkspace.PathComparer.Equals(c.OriginalArchive,ownedArchive) ||
           !SortingWorkspace.PathComparer.Equals(c.OutputArchive,outputArchive) ||
           !SortingWorkspace.PathComparer.Equals(c.Temp,temp) ||
           !Directory.Exists(operation) ||
           SortingWorkspace.HasLinkedExistingAncestor(root) ||
           SortingWorkspace.HasLinkedExistingAncestor(operation) ||
           !File.Exists(rootMarker) || !File.Exists(operationMarker) ||
           SortingWorkspace.HasSymlinkOrReparsePoint(rootMarker) ||
           SortingWorkspace.HasSymlinkOrReparsePoint(operationMarker) ||
           File.ReadAllText(rootMarker)!="PhotoSift Reborn Move v1:"+c.InstallationId.ToString("N")+"\n" ||
           File.ReadAllText(operationMarker)!="PhotoSift Reborn Move operation "+c.Id.ToString("N")+"\n" ||
           NativeFileCapability.DeviceId(root)!=NativeFileCapability.DeviceId(parent))
            throw new IOException("R27 third Move recovery ownership/marker/path refused.");
    }

    /// <summary>Fresh observation for one exact stage. Does not authorize an
    /// unrelated operation or the retry of any interrupted stage.</summary>
    internal void RequireM6JP4R27ThirdMoveUndoStage(DurableMoveItem c,string stage)
    {
        M6JP4R12GeneratedRenameReject.RequireFixture(this);
        lock(_dbGate)
        {
            if(c is null || c.Id==Guid.Empty || c.SessionId==Guid.Empty ||
               c.InstallationId!=MoveInstallationId() || c.Error is not null ||
               c.TargetIdentity is null || c.SourceIdentity is null ||
               c.TargetIdentity==c.SourceIdentity ||
               AllMoveHistoryCount()!=1 || AllCopyHistoryCount()!=2 ||
               AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
               HasUnreconciledCopy || CountAuxiliaryHistoryRows()!=1)
                throw new IOException("R27 non-exact third Move Undo history.");
            var found=MoveItems(c.SessionId);
            if(found.Count!=1 || found[0]!=c)
                throw new IOException("R27 exact current Move journal changed.");
            R26RequireTwoCopiesAndSeal(c.Id,c.SessionId,"Consumed");
            var proposal=M6JReadReview(c.Id)!;
            var candidate=M6JDecodeStored(proposal);
            if(candidate.Id!=c.Id || candidate.SessionId!=c.SessionId ||
               candidate.PhotoId!=c.PhotoId || candidate.Family!="Move" ||
               candidate.Source!=c.Source || candidate.Destination!=c.Destination ||
               candidate.SourceIdentity!=c.SourceIdentity ||
               candidate.SourceHash!=c.SourceHash ||
               candidate.SourceLength!=c.SourceLength ||
               candidate.SourceWriteTicks!=c.SourceWriteTicks)
                throw new IOException("R27 original reviewed Move ticket does not match committed row.");
            var copies=CopyItems(c.SessionId);
            if(copies.Count!=2 || copies.Any(a=>a.PhotoId==c.PhotoId ||
               a.SourceIdentity==c.SourceIdentity || a.SourceIdentity==c.TargetIdentity ||
               a.TargetIdentity==c.SourceIdentity || a.TargetIdentity==c.TargetIdentity ||
               SortingWorkspace.PathComparer.Equals(a.Source,c.Source) ||
               SortingWorkspace.PathComparer.Equals(a.Source,c.Destination) ||
               SortingWorkspace.PathComparer.Equals(a.Destination,c.Source) ||
               SortingWorkspace.PathComparer.Equals(a.Destination,c.Destination)))
                throw new IOException("R27 third Move aliases Copy predecessor ownership.");
            R27OwnedRecoveryMarkers(c);
            if(!R27Vacant(c.Temp))
                throw new IOException("R27 temporary path has a foreign occupant.");
            bool original=R27Owned(c.Source,c.SourceIdentity,c.SourceHash,c.SourceLength,
                c.SourceWriteTicks);
            bool archived=R27Owned(c.OriginalArchive,c.SourceIdentity,c.SourceHash,c.SourceLength,
                c.SourceWriteTicks);
            bool target=R27Owned(c.Destination,c.TargetIdentity,c.SourceHash,c.SourceLength);
            bool removedOutput=R27Owned(c.OutputArchive,c.TargetIdentity,c.SourceHash,c.SourceLength);
            bool valid=stage switch
            {
                "BeforeRestore"=>(c.State is "Committed" or "UndoRestoreIntent") &&
                    !original && R27Vacant(c.Source) && archived && target &&
                    !removedOutput && R27Vacant(c.OutputArchive),
                "AfterRestore"=>(c.State is "UndoRestoreIntent" or "SourceRestored" or "UndoOutputArchiveIntent") &&
                    original && !archived && R27Vacant(c.OriginalArchive) && target &&
                    !removedOutput && R27Vacant(c.OutputArchive),
                "AfterOutputArchive"=>(c.State is "UndoOutputArchiveIntent" or "Undone") &&
                    original && !archived && R27Vacant(c.OriginalArchive) &&
                    !target && R27Vacant(c.Destination) && removedOutput,
                _=>false
            };
            if(!valid)
                throw new IOException("R27 fresh native third Move Undo stage ownership refused: "+stage);
        }
    }

    /// <summary>Persist only the exact planned next state of the SAME Move C;
    /// both Copy rows and the consumed R25 review stay immutable.</summary>
    internal void PutM6JP4R27ThirdMoveUndo(DurableMoveItem previous,DurableMoveItem next,
        string physicalStage)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            string? expected=previous.State switch {
                "Committed"=>"UndoRestoreIntent",
                "UndoRestoreIntent"=>"SourceRestored",
                "SourceRestored"=>"UndoOutputArchiveIntent",
                "UndoOutputArchiveIntent"=>"Undone",_=>null
            };
            if(expected is null || next.State!=expected ||
               (previous with {State=expected})!=next ||
               previous.Error is not null || next.Error is not null ||
               previous.TargetIdentity is null ||
               physicalStage!=(previous.State switch {
                   "Committed"=>"BeforeRestore", "UndoRestoreIntent"=>"AfterRestore",
                   "SourceRestored"=>"AfterRestore", "UndoOutputArchiveIntent"=>"AfterOutputArchive",_=>"INVALID"}))
                throw new IOException("R27 unapproved state transition or journal-field substitution.");
            RequireM6JP4R27ThirdMoveUndoStage(previous,physicalStage);
            var seal=M6JReadReview(previous.Id)!;
            using var tx=_connection.BeginTransaction();
            using(var counts=_connection.CreateCommand())
            {
                counts.Transaction=tx;
                counts.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM reject_items),"+
                    "(SELECT COUNT(*) FROM m6j_review_proposals)";
                using var r=counts.ExecuteReader();
                if(!r.Read() || r.GetInt64(0)!=2 || r.GetInt64(1)!=1 ||
                    r.GetInt64(2)!=0 || r.GetInt64(3)!=0 || r.GetInt64(4)!=1)
                    throw new IOException("R27 unexpected extra history inside Undo CAS.");
            }
            if(R26TwoCopyPredecessorRawDigest(tx)!=seal.HistoryDigest ||
               R26SavedCheckpointDigestInTransaction(tx)!=seal.CheckpointDigest)
                throw new IOException("R27 Copy predecessor or saved checkpoint changed in Undo transaction.");
            using(var readSeal=_connection.CreateCommand())
            {
                readSeal.Transaction=tx;
                readSeal.CommandText="SELECT state,review_digest FROM m6j_review_proposals WHERE id=$id";
                readSeal.Parameters.AddWithValue("$id",previous.Id.ToString("N"));
                using var rows=readSeal.ExecuteReader();
                if(!rows.Read() || rows.GetString(0)!="Consumed" ||
                   rows.GetString(1)!=seal.ReviewDigest || rows.Read())
                    throw new IOException("R27 consumed review row changed inside Undo CAS.");
            }
            using var put=_connection.CreateCommand();
            put.Transaction=tx;
            put.CommandText="UPDATE move_items SET json=$next WHERE id=$id AND session=$session AND json=$old";
            put.Parameters.AddWithValue("$next",JsonSerializer.Serialize(next));
            put.Parameters.AddWithValue("$id",previous.Id.ToString("N"));
            put.Parameters.AddWithValue("$session",previous.SessionId.ToString("N"));
            put.Parameters.AddWithValue("$old",JsonSerializer.Serialize(previous));
            if(put.ExecuteNonQuery()!=1)
                throw new IOException("R27 exact Move C durable CAS failed.");
            tx.Commit();
        }
    }
}
