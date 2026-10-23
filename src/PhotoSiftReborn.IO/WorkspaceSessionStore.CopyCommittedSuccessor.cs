using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// P6G3C-R0 narrow production-safety seam: ONE reviewed Copy following ONE
/// genuinely Committed Copy, on a DIFFERENT native original and disjoint paths.
/// This is not a mixed-family grant, general multi-epoch chain, or GUI shortcut.
/// It does not authorize Move, Rename, Reject, or an unreviewed Copy.
/// </summary>
public sealed record CopyCommittedSuccessorGrant(Guid Id, Guid SessionId, Guid PhotoId,
    Guid PlanId, long Revision);

public sealed partial class WorkspaceSessionStore
{
    private sealed record CopySuccessorBasis(DurableCopyItem Prior, string RawJson,
        string Digest, string CheckpointDigest);
    private sealed record CopySuccessorStored(string GrantId, string Installation,
        string Session, string PredecessorId, string PredecessorDigest,
        string CheckpointDigest, string CandidateJson, string? UsedIntent, long State);

    private static string CopySuccessorHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string CanonicalCopyPath(string raw)
    {
        if (!Path.IsPathFullyQualified(raw) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(Path.GetFullPath(raw))!) ||
            ((File.Exists(raw) || Directory.Exists(raw)) &&
             SortingWorkspace.HasSymlinkOrReparsePoint(raw)))
            throw new IOException("Successor path is not local, absolute or contains a linked component.");
        // Case-folded NFC alias guard is conservative on Linux as well.
        return Path.GetFullPath(raw).Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
    private static bool HasCaseFoldedNfcTargetAlias(string target)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(target))!;
        if (!Directory.Exists(folder)) return false;
        string wanted=Path.GetFileName(target).Normalize(NormalizationForm.FormC);
        foreach(string existing in Directory.EnumerateFileSystemEntries(folder))
            if(string.Equals(Path.GetFileName(existing).Normalize(NormalizationForm.FormC),
                    wanted,StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private static bool CopyRecoveryVolumeMatches(PreparedCopy p)
    {
        // Copies may originate on a different device, but their output and
        // eventual owned-Undo recovery MUST remain on one local device.
        string parent=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(p.OutputRoot))!;
        return Directory.Exists(p.OutputRoot) && Directory.Exists(parent) &&
            NativeFileCapability.DeviceId(p.OutputRoot)==NativeFileCapability.DeviceId(parent);
    }

    private static bool CandidateDisjoint(PreparedCopy p, DurableCopyItem old)
    {
        string[] historical={old.Source,old.Destination,old.Temp,old.Recovery};
        string[] candidate={p.Source,p.Destination};
        if (old.PhotoId==p.PhotoId || old.SourceIdentity==p.Identity ||
            old.TargetIdentity==p.Identity || p.Source==p.Destination) return false;
        var oldSet=historical.Select(CanonicalCopyPath).ToHashSet(StringComparer.Ordinal);
        var next=candidate.Select(CanonicalCopyPath).ToArray();
        if (next[0]==next[1] || next.Any(oldSet.Contains)) return false;
        foreach (var first in historical.Select(CanonicalCopyPath))
            foreach (var second in next)
                if (second.StartsWith(first+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
                    first.StartsWith(second+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                    return false;
        return true;
    }

    // Invoked inside the _dbGate and again before native work. A row being
    // called "Committed" is not proof: verify original, committed output,
    // target inode, bytes, leaf parents and unchanged journal JSON.
    private CopySuccessorBasis ProveSingleCommittedCopy(PreparedCopy candidate,
        string? requiredDigest=null)
    {
        if (!NativeFileCapability.IsSupported || HasUnreconciledCopy ||
            AllMoveHistoryCount()!=0 || AllRenameHistoryCount()!=0 ||
            AllRejectHistoryCount()!=0 || CountAuxiliaryHistoryRows()>1)
            throw new IOException("Foreign or unverified native history prevents Copy successor.");
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT id,session,json FROM copy_items";
        using var reader=cmd.ExecuteReader();
        if (!reader.Read()) throw new IOException("Committed Copy predecessor missing.");
        string id=reader.GetString(0), session=reader.GetString(1), json=reader.GetString(2);
        if (json.Length>32768 || reader.Read())
            throw new IOException("Only one predecessor row is supported in the first Copy successor slice.");
        var prior=JsonSerializer.Deserialize<DurableCopyItem>(json)
            ?? throw new InvalidDataException("Malformed historical Copy.");
        if (prior.Id==Guid.Empty || prior.SessionId==Guid.Empty ||
            prior.Id.ToString("N")!=id || prior.SessionId.ToString("N")!=session ||
            prior.State!="Committed" ||
            prior.Error is not null || prior.TargetIdentity is null ||
            string.IsNullOrWhiteSpace(prior.SourceIdentity) ||
            string.IsNullOrWhiteSpace(prior.SourceHash) || prior.SourceHash.Length!=64 ||
            prior.SessionId!=candidate.SessionId ||
            !CandidateDisjoint(candidate,prior))
            throw new IOException("Historical Copy is not a disjoint, owned committed predecessor.");
        string priorDigest=CopySuccessorHash(id+"\n"+session+"\n"+json);
        if (requiredDigest is not null && priorDigest!=requiredDigest)
            throw new IOException("Predecessor journal changed after exact review.");
        // Dispose reader before using other SQL/native proof on the store.
        reader.Dispose();
        if (!NativeFileCapability.IsSingleLinkRegularFile(prior.Source) ||
            !NativeFileCapability.IsSingleLinkRegularFile(prior.Destination) ||
            !NativeFileCapability.IsSingleLinkRegularFile(candidate.Source) ||
            !CheckCopyOwnedFile(prior.Source,prior.SourceIdentity,prior.SourceHash,prior.SourceLength) ||
            !CheckCopyOwnedFile(prior.Destination,prior.TargetIdentity,prior.SourceHash,prior.SourceLength) ||
            File.Exists(prior.Temp) || Directory.Exists(prior.Temp) ||
            File.Exists(prior.Recovery) || Directory.Exists(prior.Recovery) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(prior.Source)!) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(prior.Destination)!) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(candidate.Source)!) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(candidate.Destination)!) ||
            File.Exists(candidate.Destination) || Directory.Exists(candidate.Destination) ||
            HasCaseFoldedNfcTargetAlias(candidate.Destination) ||
            !CopyRecoveryVolumeMatches(candidate))
            throw new IOException("Committed Copy native ownership, source, or next output has changed.");
        if (!CheckCopyOwnedFile(candidate.Source,candidate.Identity,candidate.Hash,candidate.Length) ||
            new FileInfo(candidate.Source).LastWriteTimeUtc.Ticks!=candidate.WriteTicks)
            throw new IOException("Reviewed next original changed before authorization.");
        var checkpoint=Load() ?? throw new IOException("No durable saved workspace for successor.");
        if (checkpoint.SessionId!=candidate.SessionId || checkpoint.Revision!=candidate.Revision ||
            !checkpoint.Photos.Any(x=>x.Id==candidate.PhotoId &&
                SortingWorkspace.PathComparer.Equals(x.Path,candidate.Source) &&
                x.NativeIdentity==candidate.Identity && x.Length==candidate.Length &&
                x.LastWriteUtcTicks==candidate.WriteTicks))
            throw new IOException("Saved candidate checkpoint, identity, or revision changed.");
        string checkpointDigest=CopySuccessorHash(JsonSerializer.Serialize(checkpoint));
        return new(prior,json,priorDigest,checkpointDigest);
    }

    /// <summary>
    /// Called only after ordinary explicit Review/ApprovePlan; stores a
    /// one-use exact candidate permit. No native file operations.
    /// </summary>
    public CopyCommittedSuccessorGrant ApproveCommittedCopySuccessor(
        SortingWorkspace workspace, PreparedCopy candidate)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            if (!workspace.IsCurrentPlanApproved || workspace.CurrentPlanId!=candidate.PlanId ||
                workspace.SessionId!=candidate.SessionId || workspace.Revision!=candidate.Revision ||
                workspace.GetApprovedPlan().Count!=1 ||
                !SortingWorkspace.PathComparer.Equals(workspace.GetApprovedPlan()[0].Source,candidate.Source) ||
                !SortingWorkspace.PathComparer.Equals(workspace.GetApprovedPlan()[0].Destination,candidate.Destination) ||
                JsonSerializer.Serialize(workspace.CaptureSession())!=
                    JsonSerializer.Serialize(Load()))
                throw new IOException("Fresh saved, single-item Copy review and explicit approval required.");
            using(var c=_connection.CreateCommand())
            {
                c.CommandText="SELECT COUNT(*) FROM copy_successor_grants";
                if (Convert.ToInt64(c.ExecuteScalar())!=0)
                    throw new IOException("An earlier Copy successor grant already exists; inspect it before another epoch.");
            }
            if (CountAuxiliaryHistoryRows()!=0)
                throw new IOException("Auxiliary native history requires separate predecessor review.");
            var basis=ProveSingleCommittedCopy(candidate);
            var approval=new CopyCommittedSuccessorGrant(Guid.NewGuid(),candidate.SessionId,
                candidate.PhotoId,candidate.PlanId,candidate.Revision);
            var installation=MoveInstallationId().ToString("N");
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO copy_successor_grants("+
                "id,installation,session,predecessor_id,predecessor_digest,"+
                "checkpoint_digest,candidate_json,used_intent,state) "+
                "VALUES($id,$installation,$session,$prior,$digest,$checkpoint,$candidate,NULL,0)";
            cmd.Parameters.AddWithValue("$id",approval.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$installation",installation);
            cmd.Parameters.AddWithValue("$session",approval.SessionId.ToString("N"));
            cmd.Parameters.AddWithValue("$prior",basis.Prior.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$digest",basis.Digest);
            cmd.Parameters.AddWithValue("$checkpoint",basis.CheckpointDigest);
            cmd.Parameters.AddWithValue("$candidate",JsonSerializer.Serialize(candidate));
            if(cmd.ExecuteNonQuery()!=1) throw new IOException("Successor approval not persisted.");
            tx.Commit();return approval;
        }
    }

    public bool HasAnyCopySuccessorGrant
    {
        get { lock(_dbGate) { using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM copy_successor_grants";
            return Convert.ToInt64(cmd.ExecuteScalar())>0; } }
    }

    private CopySuccessorStored ReadCopyGrant(Guid grantId)
    {
        using var c=_connection.CreateCommand();
        c.CommandText="SELECT id,installation,session,predecessor_id,predecessor_digest,"+
            "checkpoint_digest,candidate_json,used_intent,state FROM copy_successor_grants WHERE id=$id";
        c.Parameters.AddWithValue("$id",grantId.ToString("N"));
        using var r=c.ExecuteReader();
        if(!r.Read()) throw new IOException("No durable successor approval exists.");
        return new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),
            r.GetString(4),r.GetString(5),r.GetString(6),
            r.IsDBNull(7)?null:r.GetString(7),r.GetInt64(8));
    }

    /// <summary>Full PreparedCopy equality blocks a caller changing output-root
    /// or reviewed path context after explicit approval.</summary>
    public void RequireExactCopySuccessorPlan(CopyCommittedSuccessorGrant permit,
        PreparedCopy reviewed)
    {
        lock(_dbGate)
        {
            var stored=ReadCopyGrant(permit.Id);
            if(stored.State!=0 || stored.UsedIntent is not null ||
                permit.Id.ToString("N")!=stored.GrantId ||
                permit.SessionId.ToString("N")!=stored.Session ||
                permit.PhotoId!=reviewed.PhotoId || permit.PlanId!=reviewed.PlanId ||
                permit.Revision!=reviewed.Revision ||
                stored.CandidateJson!=JsonSerializer.Serialize(reviewed))
                throw new IOException("The successor Apply plan no longer matches exact approval.");
        }
    }

    /// <summary>Exact grant consumption and new Intent are ONE SQLite transaction.</summary>
    public void PutCopySuccessorIntent(DurableCopyItem item, Guid grantId)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(item.State!="Intent" || item.Id==Guid.Empty || item.SessionId==Guid.Empty ||
                item.PlanId==Guid.Empty || item.PhotoId==Guid.Empty ||
                HasUnreconciledCopy || CountAuxiliaryHistoryRows()!=1)
                throw new IOException("Copy successor Intent cannot use unresolved native history.");
            var grant=ReadCopyGrant(grantId);
            var candidate=JsonSerializer.Deserialize<PreparedCopy>(grant.CandidateJson)
                ?? throw new IOException("Successor approval payload is malformed.");
            if(grant.State!=0 || grant.UsedIntent is not null ||
                grant.Installation!=MoveInstallationId().ToString("N") ||
                grant.Session!=item.SessionId.ToString("N") ||
                grant.PredecessorId==item.Id.ToString("N") ||
                candidate.SessionId!=item.SessionId || candidate.PhotoId!=item.PhotoId ||
                candidate.PlanId!=item.PlanId || candidate.Revision!=item.PlanRevision ||
                candidate.Key!=item.Key || candidate.Source!=item.Source ||
                candidate.Destination!=item.Destination ||
                candidate.Hash!=item.SourceHash || candidate.Identity!=item.SourceIdentity ||
                candidate.Length!=item.SourceLength || candidate.WriteTicks!=item.SourceWriteTicks)
                throw new IOException("Reviewed Copy candidate no longer matches its single-use grant.");
            var basis=ProveSingleCommittedCopy(candidate,grant.PredecessorDigest);
            if(basis.Prior.Id.ToString("N")!=grant.PredecessorId ||
                basis.CheckpointDigest!=grant.CheckpointDigest)
                throw new IOException("Committed Copy or saved workspace changed after approval.");
            var checkpointJson=JsonSerializer.Serialize(Load());
            using var tx=_connection.BeginTransaction();
            using var use=_connection.CreateCommand();use.Transaction=tx;
            // Compare the exact preflight SQL bytes INSIDE the same transaction
            // that consumes the grant and records the first new Intent. The
            // native source/target proof is repeated after commit but BEFORE
            // any filesystem write (SQLite and a filesystem are not atomic).
            use.CommandText="UPDATE copy_successor_grants SET state=1,used_intent=$intent "+
                "WHERE id=$grant AND state=0 AND used_intent IS NULL "+
                "AND (SELECT json FROM copy_items WHERE id=$prior)=$prior_json "+
                "AND (SELECT json FROM workspace_checkpoint WHERE id=1)=$saved_json "+
                "AND (SELECT COUNT(*) FROM copy_items)=1 "+
                "AND (SELECT COUNT(*) FROM move_items)=0 "+
                "AND (SELECT COUNT(*) FROM rename_items)=0 "+
                "AND (SELECT COUNT(*) FROM reject_items)=0";
            use.Parameters.AddWithValue("$intent",item.Id.ToString("N"));
            use.Parameters.AddWithValue("$grant",grantId.ToString("N"));
            use.Parameters.AddWithValue("$prior",basis.Prior.Id.ToString("N"));
            use.Parameters.AddWithValue("$prior_json",basis.RawJson);
            use.Parameters.AddWithValue("$saved_json",checkpointJson);
            if(use.ExecuteNonQuery()!=1) throw new IOException("Single-use successor approval already consumed.");
            using var write=_connection.CreateCommand();write.Transaction=tx;
            write.CommandText="INSERT INTO copy_items(id,session,sequence,json) VALUES($id,$session,$seq,$json)";
            write.Parameters.AddWithValue("$id",item.Id.ToString("N"));
            write.Parameters.AddWithValue("$session",item.SessionId.ToString("N"));
            write.Parameters.AddWithValue("$seq",item.Sequence);
            write.Parameters.AddWithValue("$json",JsonSerializer.Serialize(item));
            if(write.ExecuteNonQuery()!=1) throw new IOException("Successor Intent not durably recorded.");
            tx.Commit();
        }
    }

    /// <summary>
    /// Used before selective Undo; a two-row history and a grant label alone
    /// are NOT sufficient. Revalidate exact grant binding and both native
    /// committed targets. Never depend on the saved checkpoint's later revision.
    /// </summary>
    public void RequireIndependentLatestCopyUndo(Guid session)
    {
        lock(_dbGate)
        {
            if(HasUnreconciledCopy || AllCopyHistoryCount()!=2 ||
                AllMoveHistoryCount()!=0 || AllRenameHistoryCount()!=0 ||
                AllRejectHistoryCount()!=0 || CountAuxiliaryHistoryRows()!=1)
                throw new IOException("Selective Copy Undo lacks an exclusive verified native chain.");
            string grantId;
            using(var c=_connection.CreateCommand())
            {
                c.CommandText="SELECT id FROM copy_successor_grants";
                grantId=c.ExecuteScalar() as string ?? throw new IOException("Copy grant missing.");
            }
            if(!Guid.TryParseExact(grantId,"N",out var gid) || gid==Guid.Empty)
                throw new IOException("Invalid successor grant key.");
            var grant=ReadCopyGrant(gid);
            if(grant.State!=1 || grant.UsedIntent is null ||
                grant.Installation!=MoveInstallationId().ToString("N") ||
                grant.Session!=session.ToString("N"))
                throw new IOException("Copy successor grant not durably consumed.");
            var candidate=JsonSerializer.Deserialize<PreparedCopy>(grant.CandidateJson)
                ?? throw new IOException("Malformed Copy successor approval.");
            var rows=CopyItems(session);
            if(rows.Count!=2 || rows[0].Id.ToString("N")!=grant.PredecessorId ||
                rows[1].Id.ToString("N")!=grant.UsedIntent ||
                rows[0].State!="Committed" || rows[1].State!="Committed" ||
                rows.Any(x=>x.Error is not null) ||
                candidate.PhotoId!=rows[1].PhotoId ||
                candidate.Source!=rows[1].Source || candidate.Destination!=rows[1].Destination ||
                candidate.Identity!=rows[1].SourceIdentity || candidate.Hash!=rows[1].SourceHash ||
                candidate.PlanId!=rows[1].PlanId || candidate.Revision!=rows[1].PlanRevision ||
                !CandidateDisjoint(candidate,rows[0]) ||
                !CopyRecoveryVolumeMatches(candidate))
                throw new IOException("Committed Copy rows do not match the exact successor lineage.");
            using var c2=_connection.CreateCommand();
            c2.CommandText="SELECT json FROM copy_items WHERE id=$id";
            c2.Parameters.AddWithValue("$id",grant.PredecessorId);
            var raw=c2.ExecuteScalar() as string ?? throw new IOException("Prior Copy row missing.");
            if(CopySuccessorHash(grant.PredecessorId+"\n"+session.ToString("N")+"\n"+raw)!=grant.PredecessorDigest ||
                !NativeFileCapability.IsSingleLinkRegularFile(rows[0].Source) ||
                !NativeFileCapability.IsSingleLinkRegularFile(rows[0].Destination) ||
                !NativeFileCapability.IsSingleLinkRegularFile(rows[1].Source) ||
                !NativeFileCapability.IsSingleLinkRegularFile(rows[1].Destination) ||
                !CheckCopyOwnedFile(rows[0].Source,rows[0].SourceIdentity,rows[0].SourceHash,rows[0].SourceLength) ||
                !CheckCopyOwnedFile(rows[0].Destination,rows[0].TargetIdentity,rows[0].SourceHash,rows[0].SourceLength) ||
                !CheckCopyOwnedFile(rows[1].Source,rows[1].SourceIdentity,rows[1].SourceHash,rows[1].SourceLength) ||
                !CheckCopyOwnedFile(rows[1].Destination,rows[1].TargetIdentity,rows[1].SourceHash,rows[1].SourceLength))
                throw new IOException("Prior or latest native file changed; selective Undo refused.");
        }
    }

    /// <summary>Re-proves existing committed predecessor after durable Intent,
    /// before new filesystem mutation. If false, keep the Intent for recovery.</summary>
    public void RecheckCommittedCopyAfterIntent(Guid grantId, Guid intentId)
    {
        lock(_dbGate)
        {
            var grant=ReadCopyGrant(grantId);
            if(grant.State!=1 || grant.UsedIntent!=intentId.ToString("N") ||
                CountAuxiliaryHistoryRows()!=1 ||
                AllCopyHistoryCount()!=2 || AllMoveHistoryCount()!=0 ||
                AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0)
                throw new IOException("Successor Intent or competing history changed before native operation.");
            var candidate=JsonSerializer.Deserialize<PreparedCopy>(grant.CandidateJson)
                ?? throw new IOException("Malformed successor grant after Intent.");
            // Verify the new durable Intent was not swapped or rewritten between
            // grant consumption and first native filesystem action.
            using (var intended=_connection.CreateCommand())
            {
                intended.CommandText="SELECT json FROM copy_items WHERE id=$id";
                intended.Parameters.AddWithValue("$id",intentId.ToString("N"));
                var rawIntent=intended.ExecuteScalar() as string
                    ?? throw new IOException("Durable successor Intent is missing.");
                var intent=JsonSerializer.Deserialize<DurableCopyItem>(rawIntent)
                    ?? throw new IOException("Durable successor Intent is malformed.");
                var output=Path.GetDirectoryName(candidate.Destination)!;
                var parent=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(candidate.OutputRoot))
                    ?? throw new IOException("Successor recovery parent is missing.");
                string expectedTemp=Path.Combine(output,".photosift-"+intentId.ToString("N")+".part");
                string expectedRecovery=Path.Combine(parent,".photosift-reborn-recovery",
                    candidate.SessionId.ToString("N"),intentId.ToString("N"),
                    Path.GetFileName(candidate.Destination));
                if(intent.Id!=intentId || intent.SessionId!=candidate.SessionId ||
                    intent.PhotoId!=candidate.PhotoId || intent.PlanId!=candidate.PlanId ||
                    intent.PlanRevision!=candidate.Revision || intent.Key!=candidate.Key ||
                    intent.State!="Intent" || intent.Error is not null ||
                    intent.Source!=candidate.Source || intent.Destination!=candidate.Destination ||
                    intent.Temp!=expectedTemp || intent.Recovery!=expectedRecovery ||
                    intent.SourceHash!=candidate.Hash || intent.SourceIdentity!=candidate.Identity ||
                    intent.SourceLength!=candidate.Length || intent.SourceWriteTicks!=candidate.WriteTicks ||
                    intent.TempIdentity is not null || intent.TargetIdentity is not null)
                    throw new IOException("New durable Intent changed before native operation.");
            }
            // One new Intent exists: filter it out and verify the unchanged predecessor.
            using var query=_connection.CreateCommand();
            query.CommandText="SELECT json FROM copy_items WHERE id=$id";
            query.Parameters.AddWithValue("$id",grant.PredecessorId);
            var raw=query.ExecuteScalar() as string ?? throw new IOException("Predecessor vanished.");
            if(CopySuccessorHash(grant.PredecessorId+"\n"+candidate.SessionId.ToString("N")+"\n"+raw)!=grant.PredecessorDigest)
                throw new IOException("Predecessor row mutated after grant consumption.");
            var row=JsonSerializer.Deserialize<DurableCopyItem>(raw) ?? throw new IOException("Malformed prior row.");
            if(row.State!="Committed" || row.Error is not null ||
                !NativeFileCapability.IsSingleLinkRegularFile(row.Source) ||
                !NativeFileCapability.IsSingleLinkRegularFile(row.Destination) ||
                !NativeFileCapability.IsSingleLinkRegularFile(candidate.Source) ||
                CopySuccessorHash(JsonSerializer.Serialize(Load()))!=grant.CheckpointDigest ||
                !CheckCopyOwnedFile(row.Source,row.SourceIdentity,row.SourceHash,row.SourceLength) ||
                !CheckCopyOwnedFile(row.Destination,row.TargetIdentity,row.SourceHash,row.SourceLength) ||
                !CheckCopyOwnedFile(candidate.Source,candidate.Identity,candidate.Hash,candidate.Length) ||
                File.Exists(candidate.Destination) || Directory.Exists(candidate.Destination) ||
                HasCaseFoldedNfcTargetAlias(candidate.Destination) ||
                !CopyRecoveryVolumeMatches(candidate))
                throw new IOException("Previous or candidate native bytes changed after Intent.");
        }
    }
}
