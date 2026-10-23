using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// One atomic and immutable GUI checkpoint for the THIRD generated-photo Move.
/// This is NOT a Move ticket: v12's separate explicitly-approved, single-use
/// epoch grant must still be consumed with the native Move Intent.
/// </summary>
public sealed record ThirdGuiSessionEvidence(bool Present,bool Verified,
    Guid SessionId,string Reason);

public sealed partial class WorkspaceSessionStore
{
    private sealed record ThirdGuiRow(string Installation,string Prior,string Next,
        string PriorSnapshot,int Predecessors,string Head,string PhotoId,
        string Path,string Identity,string Hash,long Length,long Ticks);

    private ThirdGuiRow? ReadThirdGuiRow(SqliteTransaction? tx=null)
    {
        using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT installation,prior_session,next_session,prior_snapshot,"+
            "predecessor_rows,ledger_head,candidate_photo_id,candidate_path,"+
            "candidate_identity,candidate_hash,candidate_length,candidate_ticks " +
            "FROM move_gui_third_session WHERE id=1";
        using var rd=cmd.ExecuteReader();
        if(!rd.Read()) return null;
        return new ThirdGuiRow(rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.GetString(3),checked((int)rd.GetInt64(4)),rd.GetString(5),
            rd.GetString(6),rd.GetString(7),rd.GetString(8),rd.GetString(9),
            rd.GetInt64(10),rd.GetInt64(11));
    }

    private static bool MatchThirdFile(ThirdGuiRow row)
    {
        if(!File.Exists(row.Path) || SortingWorkspace.HasSymlinkOrReparsePoint(row.Path))
            return false;
        var info=new FileInfo(row.Path);
        return info.Length==row.Length && info.LastWriteTimeUtc.Ticks==row.Ticks &&
            NativeFileCapability.Identity(row.Path)==row.Identity &&
            string.Equals(HashFile(row.Path),row.Hash,StringComparison.Ordinal);
    }

    private string ReadOriginalThirdPhotoSnapshot()
    {
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT prior_snapshot FROM move_gui_successor WHERE id=1";
        var json=cmd.ExecuteScalar() as string;
        if(json is null || json.Length>64*1024*1024)
            throw new IOException("Original first-GUI-session ownership record missing.");
        return json;
    }

    /// <summary>Read-only inspection. Never fixes an uncertain file or silently
    /// backfills an old workspace. Immutable first and second terminal receipts
    /// remain natively verified even after the third Move has begun.</summary>
    public ThirdGuiSessionEvidence InspectThirdGuiSession(WorkspaceSessionSnapshot snapshot)
    {
        lock(_dbGate)
        {
            ThirdGuiRow? row=null;
            try
            {
                row=ReadThirdGuiRow();
                if(row is null) return new ThirdGuiSessionEvidence(false,false,Guid.Empty,
                    "No third generated-GUI transition exists.");
                // The OR-chain may short-circuit before parsing row.Next. Initialize
                // the session id so every fail-closed branch has a definite value.
                Guid next = Guid.Empty;
                if(!P5CGuiFixturePolicy.IsMarkedStore(this) || HasUnreconciledCopy ||
                    row.Installation!=MoveInstallationId().ToString("N") ||
                    !Guid.TryParseExact(row.Next,"N",out next) || next!=snapshot.SessionId ||
                    !Guid.TryParseExact(row.Prior,"N",out var prior) || prior==next ||
                    !Guid.TryParseExact(row.PhotoId,"N",out var photoId) ||
                    row.Predecessors!=2 || row.Head.Length!=64 || row.Hash.Length!=64 ||
                    snapshot.Photos.Length!=1 || snapshot.Photos[0].Id!=photoId ||
                    !Same(snapshot.Photos[0].Path,row.Path) ||
                    snapshot.Photos[0].NativeIdentity!=row.Identity ||
                    snapshot.Photos[0].Length!=row.Length ||
                    snapshot.Photos[0].LastWriteUtcTicks!=row.Ticks ||
                    snapshot.Assignments.Any(a=>a.PhotoId!=photoId) ||
                    snapshot.RemovedPhotos is {Length:>0} ||
                    snapshot.CollisionMode!=CollisionChoice.Refuse ||
                    AllRenameHistoryCount()!=0 || AssessFreshMoveEpoch().CopyRows!=0 ||
                    AssessFreshMoveEpoch().RenameCloseouts!=0 ||
                    AllMoveHistoryCount() is <2 or >3)
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Third GUI fixture/session/history differs from immutable binding.");
                var original=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(ReadOriginalThirdPhotoSnapshot());
                var predecessor=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(row.PriorSnapshot);
                if(original is null || predecessor is null || predecessor.SessionId!=prior ||
                    !Same(original.SourceRoot,snapshot.SourceRoot) ||
                    !Same(original.DestinationRoot,snapshot.DestinationRoot) ||
                    !Same(predecessor.SourceRoot,snapshot.SourceRoot) ||
                    !Same(predecessor.DestinationRoot,snapshot.DestinationRoot) ||
                    snapshot.SourceRoots is not {Length:1} ||
                    original.SourceRoots is not {Length:1} ||
                    !Same(original.SourceRoots[0],snapshot.SourceRoots[0]) ||
                    !Same(predecessor.SourceRoots?.SingleOrDefault(),snapshot.SourceRoots[0]) ||
                    !original.Routes.SequenceEqual(snapshot.Routes) ||
                    !predecessor.Routes.SequenceEqual(snapshot.Routes) ||
                    original.Photos.Count(p=>p.Id==photoId && Same(p.Path,row.Path))!=1 ||
                    predecessor.Photos.Length!=1 ||
                    Path.GetFileName(predecessor.Photos[0].Path)!="photo_02.png")
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "The first/second immutable source session no longer proves third photo.");
                string root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                if(!Same(row.Path,Path.Combine(root,"source","photo_03.png")) ||
                    !Same(snapshot.SourceRoot,Path.Combine(root,"source")) ||
                    !Same(snapshot.DestinationRoot,Path.Combine(root,"output")) ||
                    row.Hash!=OriginalGeneratedFixtureHash(root,"photo_03.png"))
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Only original generated photo_03 source/output are permitted.");
                using var tx=_connection.BeginTransaction();
                var historical=OrderedMoveBytes(tx);
                var receipts=ExistingEpochReceipts(tx);
                if(receipts.Count is <2 or >3 || historical.Count is <2 or >3 ||
                    (historical.Count==2 && receipts.Count!=2) ||
                    !string.Equals(row.Head,receipts[1].Digest,StringComparison.Ordinal))
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Two immutable predecessor receipts are required.");
                var nativeHead=VerifyEpochReceiptPrefix(receipts,historical,
                    row.Installation,true);
                if(receipts.Count==2 && nativeHead!=row.Head)
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Native historical receipt head changed.");
                var firstTwo=historical.Take(2).ToArray();
                if(firstTwo[1].Session!=row.Prior ||
                    historical.Skip(2).Any(x=>x.Session!=row.Next))
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Historical Move sessions differ from chained GUI transition.");
                // VerifyEpochReceiptPrefix above has already natively verified
                // BOTH immutable predecessor receipts inside this same SQL tx.
                // Do NOT call RequireNativePredecessorsInsideTransaction(tx,2):
                // that older whole-history guard iterates every Move row and
                // necessarily rejects the third COMMITTED Move (not terminal
                // until Undo). It is valid only before the third row exists.
                tx.Commit();
                var current=MoveItems(next);
                if(current.Count>1 || historical.Count!=2+current.Count ||
                    current.Any(m=>m.PhotoId!=photoId || !Same(m.Source,row.Path)))
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Foreign or duplicated third-Move journal entry.");
                if((current.Count==0 || current[0].State=="Undone") &&
                    (!MatchThirdFile(row) ||
                     (current.Count==1 && !DurableMoveCoordinator.ProveTerminalUndoneEvidence(current[0]))))
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Third native source or Undo archive ownership changed.");
                if(current.Count==1 && current[0].State is not ("Committed" or "Undone"))
                    return new ThirdGuiSessionEvidence(true,false,next,
                        "Uncertain third Move requires read-only inspection; no automatic retry.");
                return new ThirdGuiSessionEvidence(true,true,next,
                    "Third generated fixture session verified; separate v12 one-use grant required.");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or JsonException or
                ArgumentException or OverflowException)
            {
                return new ThirdGuiSessionEvidence(row is not null,false,Guid.Empty,
                    "Third GUI inspection refused: "+ex.Message);
            }
        }
    }

    /// <summary>CAS a fully undone second session to photo03 in one SQLite tx;
    /// persist immutable provenance. This transition creates NO Move Intent.</summary>
    public WorkspaceSessionSnapshot BeginThirdGuiSession(WorkspaceSessionSnapshot before)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(!P5CGuiFixturePolicy.IsMarkedStore(this) || HasUnreconciledCopy ||
                AllMoveHistoryCount()!=2 || AllRenameHistoryCount()!=0 ||
                AssessFreshMoveEpoch().CopyRows!=0 || AssessFreshMoveEpoch().RenameCloseouts!=0 ||
                !InspectGuiSuccessor(before).Verified)
                throw new IOException("Only the fully undone first GUI successor can begin third session.");
            if(ReadThirdGuiRow() is not null)
                throw new IOException("Third GUI session already recorded; never overwrite the transition.");
            var prior=MoveItems(before.SessionId);
            if(prior.Count!=1 || prior[0].State!="Undone" ||
                Path.GetFileName(prior[0].Source)!="photo_02.png" ||
                !DurableMoveCoordinator.ProveTerminalUndoneEvidence(prior[0]))
                throw new IOException("Second GUI Move Undo lacks native ownership proof.");
            var ledger=SealMoveEpochLedger();
            if(!ledger.Verified || ledger.SealedRows!=2 || ledger.MoveRows!=2)
                throw new IOException("Both terminal Move receipts must be natively sealed.");
            using var headQuery=_connection.CreateCommand();
            headQuery.CommandText="SELECT receipt_digest FROM move_epoch_ledger WHERE ordinal=2";
            string head=headQuery.ExecuteScalar() as string ?? throw new IOException("Second ledger head missing.");
            var initial=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(ReadOriginalThirdPhotoSnapshot())
                ?? throw new IOException("Original fixture snapshot missing.");
            var root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
            string path=Path.Combine(root,"source","photo_03.png");
            if(initial.Photos.Length!=3 || before.Photos.Length!=1 ||
                !Same(before.SourceRoot,Path.Combine(root,"source")) ||
                !Same(before.DestinationRoot,Path.Combine(root,"output")) ||
                !initial.Routes.SequenceEqual(before.Routes) ||
                initial.Photos.Count(p=>Same(p.Path,path))!=1)
                throw new IOException("Third generated photograph is absent from immutable initial import.");
            var photo=initial.Photos.Single(p=>Same(p.Path,path));
            if(photo.Id==prior[0].PhotoId || photo.NativeIdentity is null ||
                !File.Exists(path) || SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
                new FileInfo(path).Length!=photo.Length ||
                File.GetLastWriteTimeUtc(path).Ticks!=photo.LastWriteUtcTicks ||
                NativeFileCapability.Identity(path)!=photo.NativeIdentity ||
                HashFile(path)!=OriginalGeneratedFixtureHash(root,"photo_03.png"))
                throw new IOException("Generated photo03 native identity/hash no longer match initial snapshot.");
            var next=before with {
                SessionId=Guid.NewGuid(),Photos=new[]{photo},
                Assignments=Array.Empty<SavedAssignment>(),Decisions=Array.Empty<SavedDecision>(),
                RemovedPhotos=Array.Empty<RemovedSessionPhoto>(),Index=0,
                Revision=checked(before.Revision+1),CollisionMode=CollisionChoice.Refuse
            };
            var validator=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            validator.RestoreSession(next);
            P5CGuiFixturePolicy.VerifyExactWorkspace(this,validator);
            string frozen;
            using(var cursor=_connection.CreateCommand())
            {
                cursor.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
                using var rd=cursor.ExecuteReader();
                if(!rd.Read() || rd.GetInt64(1)!=0) throw new IOException("Saved checkpoint missing or at risk.");
                frozen=rd.GetString(0);
            }
            var persisted=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(frozen)
                ?? throw new IOException("Saved checkpoint unreadable.");
            if((before.Revision!=persisted.Revision &&
                !(persisted.Revision<long.MaxValue-1 && before.Revision==persisted.Revision+1)) ||
                JsonSerializer.Serialize(before with {Revision=persisted.Revision})!=frozen)
                throw new IOException("Saved workspace differs from restarted GUI, no transition recorded.");
            string nextJson=JsonSerializer.Serialize(next);
            string install=MoveInstallationId().ToString("N");
            using var tx=_connection.BeginTransaction();
            var ordered=OrderedMoveBytes(tx);var receipts=ExistingEpochReceipts(tx);
            if(ordered.Count!=2 || receipts.Count!=2 ||
                VerifyEpochReceiptPrefix(receipts,ordered,install,true)!=head ||
                ordered[1].Session!=before.SessionId.ToString("N"))
                throw new IOException("Terminal native predecessor chain changed during transition.");
            RequireNativePredecessorsInsideTransaction(tx,2,install);
            using(var insert=_connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText="INSERT INTO move_gui_third_session("+
                    "id,installation,prior_session,next_session,prior_snapshot,predecessor_rows,"+
                    "ledger_head,candidate_photo_id,candidate_path,candidate_identity,candidate_hash,"+
                    "candidate_length,candidate_ticks) VALUES(1,$i,$prior,$next,$snapshot,2,$head,"+
                    "$photo,$path,$identity,$hash,$length,$ticks)";
                insert.Parameters.AddWithValue("$i",install);
                insert.Parameters.AddWithValue("$prior",before.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$next",next.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$snapshot",frozen);
                insert.Parameters.AddWithValue("$head",head);
                insert.Parameters.AddWithValue("$photo",photo.Id.ToString("N"));
                insert.Parameters.AddWithValue("$path",path);
                insert.Parameters.AddWithValue("$identity",photo.NativeIdentity);
                insert.Parameters.AddWithValue("$hash",HashFile(path));
                insert.Parameters.AddWithValue("$length",photo.Length);
                insert.Parameters.AddWithValue("$ticks",photo.LastWriteUtcTicks);
                if(insert.ExecuteNonQuery()!=1) throw new IOException("Third transition binding not written.");
            }
            using(var cas=_connection.CreateCommand())
            {
                cas.Transaction=tx;
                cas.CommandText="UPDATE workspace_checkpoint SET json=$next WHERE id=1 AND json=$prior AND copy_risk=0";
                cas.Parameters.AddWithValue("$next",nextJson);
                cas.Parameters.AddWithValue("$prior",frozen);
                if(cas.ExecuteNonQuery()!=1)
                    throw new IOException("Third GUI checkpoint CAS failed; history unchanged.");
            }
            tx.Commit();
            if(!InspectThirdGuiSession(next).Verified)
                throw new IOException("Third GUI transition committed but inspection uncertain; restart read-only.");
            return next;
        }
    }

    public string ThirdGuiPredecessorSummary()
    {
        lock(_dbGate)
        {
            var row=ReadThirdGuiRow() ?? throw new IOException("Missing third GUI transition.");
            return "Historical terminal Moves="+row.Predecessors+", linked receipt head="+row.Head;
        }
    }
}
