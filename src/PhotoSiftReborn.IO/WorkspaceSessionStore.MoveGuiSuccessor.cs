using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>One guarded generated-fixture-only GUI successor. The prior
/// checkpoint stays immutable in SQLite; beginning another session is never
/// an authorization by itself, and B2/B3 still control the new Move Intent.
/// This v10 singleton deliberately does NOT implement third/later epochs.</summary>
public sealed record FixtureSuccessorEvidence(bool Present, bool Verified,
    Guid SessionId, string Reason);

public sealed partial class WorkspaceSessionStore
{
    private static bool Same(string? a,string? b) =>
        a is not null && b is not null && SortingWorkspace.PathComparer.Equals(a,b);

    private static string HashFile(string file) =>
        SortingWorkspace.ComputeSha256Async(file).GetAwaiter().GetResult();

    private static string OriginalGeneratedFixtureHash(string root,string filename)
    {
        string manifest=Path.Combine(root,"P5C_OWNER_MANIFEST.json");
        if (!File.Exists(manifest) || SortingWorkspace.HasSymlinkOrReparsePoint(manifest) ||
            new FileInfo(manifest).Length>65536)
            throw new IOException("Generated fixture manifest missing or unsafe.");
        using var doc=JsonDocument.Parse(File.ReadAllText(manifest));
        var entry=doc.RootElement.GetProperty("source_sha256").GetProperty(filename).GetString();
        if (entry is null || entry.Length!=64)
            throw new IOException("Missing exact generated-photo hash.");
        return entry.ToUpperInvariant();
    }

    public FixtureSuccessorEvidence InspectGuiSuccessor(WorkspaceSessionSnapshot snapshot)
    {
        lock (_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT installation,prior_session,next_session,prior_snapshot,"+
                "prior_move_json,predecessor_digest,predecessor_rows,candidate_photo_id,"+
                "candidate_path,candidate_identity,candidate_hash,candidate_length,candidate_ticks " +
                "FROM move_gui_successor WHERE id=1";
            using var rd=cmd.ExecuteReader();
            if (!rd.Read()) return new FixtureSuccessorEvidence(false,false,Guid.Empty,
                "No bounded GUI successor transition exists.");
            var fields=new string[11];
            for(int i=0;i<11;i++)
                fields[i]=i==6?rd.GetInt64(6).ToString(System.Globalization.CultureInfo.InvariantCulture):rd.GetString(i);
            long length=rd.GetInt64(11),ticks=rd.GetInt64(12);
            rd.Close();
            try
            {
                // A short-circuit earlier in the OR guard may leave this
                // identifier unassigned; failure must return an empty GUID.
                Guid nextId = Guid.Empty;
                var installation=MoveInstallationId().ToString("N");
                if(!P5CGuiFixturePolicy.IsMarkedStore(this) || fields[0]!=installation ||
                    !Guid.TryParseExact(fields[1],"N",out var priorId) ||
                    !Guid.TryParseExact(fields[2],"N",out nextId) ||
                    !Guid.TryParseExact(fields[7],"N",out var photoId) ||
                    nextId!=snapshot.SessionId || priorId==nextId ||
                    !Same(fields[8],snapshot.Photos.Single().Path) ||
                    snapshot.Photos.Single().Id!=photoId ||
                    Path.GetFileName(fields[8])!="photo_02.png" ||
                    !Same(Path.GetDirectoryName(fields[8]),Path.Combine(
                        Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName,"source")) ||
                    snapshot.Assignments.Any(a=>a.PhotoId!=photoId) ||
                    snapshot.RemovedPhotos is {Length:>0} || snapshot.CollisionMode!=CollisionChoice.Refuse ||
                    fields[5].Length!=64 || fields[6]!="1" || length<1 || ticks<0 ||
                    HasUnreconciledCopy || AllMoveHistoryCount() is <1 or >2 ||
                    AssessFreshMoveEpoch().CopyRows!=0 || AllRenameHistoryCount()!=0 || AssessFreshMoveEpoch().RenameCloseouts!=0)
                    return new FixtureSuccessorEvidence(true,false,nextId,"Successor fixture/session/history changed.");
                var prior=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(fields[3]);
                var originalCandidate=prior?.Photos.SingleOrDefault(p=>p.Id==photoId);
                if (prior is null || originalCandidate is null ||
                    snapshot.Photos.Single().Length!=originalCandidate.Length ||
                    snapshot.Photos.Single().LastWriteUtcTicks!=originalCandidate.LastWriteUtcTicks ||
                    snapshot.Photos.Single().NativeIdentity!=originalCandidate.NativeIdentity ||
                    originalCandidate.NativeIdentity!=fields[9] ||
                    prior.SessionId!=priorId ||
                    !Same(prior.SourceRoot,snapshot.SourceRoot) ||
                    !Same(prior.DestinationRoot,snapshot.DestinationRoot) ||
                    prior.SourceRoots is null || snapshot.SourceRoots is null ||
                    prior.SourceRoots.Length!=1 || snapshot.SourceRoots.Length!=1 ||
                    !Same(prior.SourceRoots[0],snapshot.SourceRoots[0]) ||
                    prior.Photos.Length!=3 || !prior.Photos.Any(p=>p.Id==photoId && Same(p.Path,fields[8])) ||
                    snapshot.Routes.Length!=prior.Routes.Length ||
                    !snapshot.Routes.SequenceEqual(prior.Routes))
                    return new FixtureSuccessorEvidence(true,false,nextId,"Immutable predecessor workspace binding differs.");
                var historical=MoveItems(priorId);
                if (historical.Count!=1 || historical[0].State!="Undone" ||
                    JsonSerializer.Serialize(historical[0])!=fields[4] ||
                    !DurableMoveCoordinator.ProveTerminalUndoneEvidence(historical[0]))
                    return new FixtureSuccessorEvidence(true,false,nextId,"Previous Move Undo native ownership lost.");
                var current=MoveItems(nextId);
                if (current.Count>1 || AllMoveHistoryCount()!=historical.Count+current.Count ||
                    current.Any(m=>m.PhotoId!=photoId || !Same(m.Source,fields[8])))
                    return new FixtureSuccessorEvidence(true,false,nextId,"Successor Move journal has foreign or duplicate rows.");
                if (current.Count==0 || current[0].State=="Undone")
                {
                    if(!File.Exists(fields[8]) || SortingWorkspace.HasSymlinkOrReparsePoint(fields[8]) ||
                        new FileInfo(fields[8]).Length!=length ||
                        File.GetLastWriteTimeUtc(fields[8]).Ticks!=ticks ||
                        NativeFileCapability.Identity(fields[8])!=fields[9] ||
                        HashFile(fields[8])!=fields[10] ||
                        (current.Count==1 && !DurableMoveCoordinator.ProveTerminalUndoneEvidence(current[0])))
                        return new FixtureSuccessorEvidence(true,false,nextId,"Successor source/archive ownership changed.");
                }
                return new FixtureSuccessorEvidence(true,true,nextId,
                    "Bounded photo_02 successor session bound to verified native predecessor. B3 grant still required.");
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or ArgumentException or JsonException)
            { return new FixtureSuccessorEvidence(true,false,Guid.Empty,"Successor inspection failed: "+ex.Message); }
        }
    }

    /// <summary>Atomic replacement of the current singleton checkpoint with a
    /// *restricted* photo_02-only session. Old checkpoint is saved byte-for-byte
    /// in immutable v10 row. A later Move still needs B2+B3 authority.</summary>
    public WorkspaceSessionSnapshot BeginFirstGuiSuccessor(WorkspaceSessionSnapshot before)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            if(!P5CGuiFixturePolicy.IsMarkedStore(this) || HasUnreconciledCopy ||
                AllMoveHistoryCount()!=1 || AssessFreshMoveEpoch().CopyRows!=0 || AllRenameHistoryCount()!=0 || AssessFreshMoveEpoch().RenameCloseouts!=0 ||
                !InspectTerminalMoveCloseout().Verified ||
                InspectStagedMoveEpoch().Present || InspectMoveEpochGrant().Present)
                throw new IOException("The first fixture Move history is not fully terminal and uncommitted to a successor.");
            using(var existing=_connection.CreateCommand())
            {
                existing.CommandText="SELECT COUNT(*) FROM move_gui_successor";
                if(Convert.ToInt32(existing.ExecuteScalar())!=0)
                    throw new IOException("GUI successor already begun; cannot rewrite its binding.");
            }
            var old=MoveItems(before.SessionId);
            if(old.Count!=1 || old[0].State!="Undone" ||
                !DurableMoveCoordinator.ProveTerminalUndoneEvidence(old[0]) ||
                Path.GetFileName(old[0].Source)!="photo_01.png" ||
                before.Photos.Length!=3 || before.SourceRoots is not { Length:1 } ||
                before.DestinationRoot is null ||
                before.Assignments.Any(a=>a.PhotoId!=old[0].PhotoId))
                throw new IOException("Original first-Move session is not the expected fully undone GUI fixture.");
            var root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
            var source=Path.Combine(root,"source"); var output=Path.Combine(root,"output");
            if(!Same(before.SourceRoot,source) || !Same(before.DestinationRoot,output) ||
                !Same(before.SourceRoots[0],source) ||
                before.Photos.Count(p=>Path.GetFileName(p.Path)=="photo_02.png")!=1)
                throw new IOException("Only the original generated three-photo fixture may begin successor review.");
            var photo=before.Photos.Single(p=>Path.GetFileName(p.Path)=="photo_02.png");
            if(photo.Id==old[0].PhotoId || !Same(Path.GetDirectoryName(photo.Path),source) ||
                photo.NativeIdentity is null || !File.Exists(photo.Path) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(photo.Path) ||
                new FileInfo(photo.Path).Length!=photo.Length ||
                File.GetLastWriteTimeUtc(photo.Path).Ticks!=photo.LastWriteUtcTicks ||
                NativeFileCapability.Identity(photo.Path)!=photo.NativeIdentity)
                throw new IOException("Second generated photograph lost its reviewed native identity.");
            string fileHash=HashFile(photo.Path);
            if(!string.Equals(fileHash,OriginalGeneratedFixtureHash(root,"photo_02.png"),
                    StringComparison.Ordinal))
                throw new IOException("Generated second photograph differs from its original fixture hash.");
            var next=before with {
                SessionId=Guid.NewGuid(), Photos=new[]{photo}, Assignments=Array.Empty<SavedAssignment>(),
                Decisions=Array.Empty<SavedDecision>(), RemovedPhotos=Array.Empty<RemovedSessionPhoto>(),
                Index=0,Revision=checked(before.Revision+1),CollisionMode=CollisionChoice.Refuse
            };
            var validator=new SortingWorkspace{IdentityResolver=NativeFileCapability.Identity};
            validator.RestoreSession(next);
            P5CGuiFixturePolicy.VerifyExactWorkspace(this,validator);
            string installation=MoveInstallationId().ToString("N");
            var head=TerminalMoveJournalDigest(installation);
            if(head.Count!=1) throw new IOException("Predecessor journal count changed.");
            // Owner GUI restarts are not byte-for-byte checkpoint replays:
            // SortingWorkspace.RestoreSession intentionally increments Revision
            // to revoke old plan approvals. The persisted checkpoint is still
            // the authority. Accept ONLY that one restoration-only increment,
            // while requiring every other saved field to match exactly.
            string originalJson;
            using(var frozen=_connection.CreateCommand())
            {
                frozen.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
                using var rd=frozen.ExecuteReader();
                if(!rd.Read() || rd.GetInt64(1)!=0)
                    throw new IOException("Saved fixture checkpoint is missing or at risk; no transition recorded.");
                originalJson=rd.GetString(0);
            }
            var persisted=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(originalJson)
                ?? throw new IOException("Saved fixture checkpoint cannot be read.");
            bool exactRevision=before.Revision==persisted.Revision;
            bool restartRevision=persisted.Revision<long.MaxValue-1 &&
                before.Revision==persisted.Revision+1;
            if((!exactRevision && !restartRevision) ||
                JsonSerializer.Serialize(before with {Revision=persisted.Revision})!=originalJson)
                throw new IOException("Saved checkpoint differs from the exact restarted GUI workspace; no transition recorded.");
            string nextJson=JsonSerializer.Serialize(next);
            using var tx=_connection.BeginTransaction();
            using(var cursor=_connection.CreateCommand())
            {
                cursor.Transaction=tx;
                cursor.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
                using var r=cursor.ExecuteReader();
                if(!r.Read() || r.GetString(0)!=originalJson || r.GetInt64(1)!=0)
                    throw new IOException("Saved session changed; no transition was recorded.");
            }
            if(TerminalMoveJournalDigest(installation,tx)!=head)
                throw new IOException("Move journal changed during new-session transition.");
            RequireNativePredecessorsInsideTransaction(tx,1,installation);
            using(var insert=_connection.CreateCommand())
            {
                insert.Transaction=tx;
                insert.CommandText="INSERT INTO move_gui_successor(id,installation,prior_session,next_session,"+
                    "prior_snapshot,prior_move_json,predecessor_digest,predecessor_rows,candidate_photo_id,"+
                    "candidate_path,candidate_identity,candidate_hash,candidate_length,candidate_ticks) "+
                    "VALUES(1,$install,$prior,$next,$snapshot,$move,$digest,1,$photo,$path,$identity,$hash,$length,$ticks)";
                insert.Parameters.AddWithValue("$install",installation);
                insert.Parameters.AddWithValue("$prior",before.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$next",next.SessionId.ToString("N"));
                insert.Parameters.AddWithValue("$snapshot",originalJson);
                insert.Parameters.AddWithValue("$move",JsonSerializer.Serialize(old[0]));
                insert.Parameters.AddWithValue("$digest",head.Digest);
                insert.Parameters.AddWithValue("$photo",photo.Id.ToString("N"));
                insert.Parameters.AddWithValue("$path",photo.Path);
                insert.Parameters.AddWithValue("$identity",photo.NativeIdentity);
                insert.Parameters.AddWithValue("$hash",fileHash);
                insert.Parameters.AddWithValue("$length",photo.Length);
                insert.Parameters.AddWithValue("$ticks",photo.LastWriteUtcTicks);
                insert.ExecuteNonQuery();
            }
            using(var save=_connection.CreateCommand())
            {
                save.Transaction=tx;
                save.CommandText="UPDATE workspace_checkpoint SET json=$next WHERE id=1 AND json=$prior AND copy_risk=0";
                save.Parameters.AddWithValue("$next",nextJson);
                save.Parameters.AddWithValue("$prior",originalJson);
                if(save.ExecuteNonQuery()!=1)
                    throw new IOException("Atomic successor checkpoint replacement failed.");
            }
            tx.Commit();
            if(!InspectGuiSuccessor(next).Verified)
                throw new IOException("Committed successor requires read-only inspection before use; restart read-only if uncertain.");
            return next;
        }
    }

    public string GuiSuccessorPredecessorSummary()
    {
        lock(_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT predecessor_rows,predecessor_digest FROM move_gui_successor WHERE id=1";
            using var rd=cmd.ExecuteReader();
            if(!rd.Read()) throw new IOException("No immutable predecessor binding.");
            return "Historical terminal Moves="+rd.GetInt64(0)+", SHA-256 journal="+rd.GetString(1);
        }
    }
}
