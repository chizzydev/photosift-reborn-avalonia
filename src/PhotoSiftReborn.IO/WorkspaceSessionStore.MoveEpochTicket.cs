using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// B2 ticket is a permanently recorded, read-only proposal for a subsequent
/// Move. It is not executable authorization, and no SQLite Move Intent guard is
/// relaxed. Stale tickets remain observable rather than being silently reset.
/// </summary>
public sealed record StagedMoveEpochEvidence(bool Present, bool Verified,
    int PredecessorRows, string Reason);

public sealed partial class WorkspaceSessionStore
{
    private static string CandidateDigest(DurableMoveItem row)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        DigestField(hash,"PhotoSiftReborn.StagedMoveEpoch.v1");
        DigestField(hash,JsonSerializer.Serialize(row));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool ValidateProposedMove(DurableMoveItem row)
    {
        // Strictly read-only. These are necessary but NOT sufficient for native
        // Move authority. Exact GUI approval and fresh executor checks come later.
        if (row.State!="Intent" || row.Error is not null ||
            row.Id==Guid.Empty || row.InstallationId==Guid.Empty ||
            row.SessionId==Guid.Empty || row.PhotoId==Guid.Empty || row.PlanId==Guid.Empty ||
            row.TempIdentity is not null || row.TargetIdentity is not null ||
            row.SourceLength<1 || row.PlanRevision<0 ||
            string.IsNullOrWhiteSpace(row.SourceHash) || row.SourceHash.Length!=64 ||
            string.IsNullOrWhiteSpace(row.SourceIdentity) ||
            new[]{row.Source,row.Destination,row.Temp,row.OriginalArchive,
                row.OutputArchive,row.OutputRoot}.Any(p=>string.IsNullOrWhiteSpace(p) ||
                    !Path.IsPathFullyQualified(p)))
            return false;
        try
        {
            var source=Path.GetFullPath(row.Source);
            var dest=Path.GetFullPath(row.Destination);
            var output=Path.GetFullPath(row.OutputRoot);
            if (SortingWorkspace.PathComparer.Equals(source,dest) ||
                !Directory.Exists(output) || !File.Exists(source) ||
                File.Exists(dest) || Directory.Exists(dest) ||
                File.Exists(row.Temp) || Directory.Exists(row.Temp) ||
                File.Exists(row.OriginalArchive) || Directory.Exists(row.OriginalArchive) ||
                File.Exists(row.OutputArchive) || Directory.Exists(row.OutputArchive) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(source)!) ||
                SortingWorkspace.HasLinkedExistingAncestor(output) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(dest)!) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(source) ||
                !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(dest),
                    Path.Combine(output,Path.GetFileName(Path.GetDirectoryName(dest)!))) ||
                !SortingWorkspace.PathComparer.Equals(Path.GetFileName(dest),Path.GetFileName(source)) ||
                NativeFileCapability.DeviceId(source)!=NativeFileCapability.DeviceId(output))
                return false;
            var info=new FileInfo(source);
            return info.Length==row.SourceLength &&
                info.LastWriteTimeUtc.Ticks==row.SourceWriteTicks &&
                NativeFileCapability.Identity(source)==row.SourceIdentity &&
                SortingWorkspace.ComputeSha256Async(source).GetAwaiter().GetResult()==row.SourceHash;
        }
        catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException or NotSupportedException)
        { return false; }
    }

    private bool NoPriorSourceOrDestination(DurableMoveItem row)
    {
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT json FROM move_items";
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            string raw=reader.GetString(0);
            if(raw.Length>32768) return false;
            DurableMoveItem? old;
            try { old=JsonSerializer.Deserialize<DurableMoveItem>(raw); }
            catch(JsonException) {return false;}
            if(old is null || old.SessionId==row.SessionId || old.PhotoId==row.PhotoId ||
                SortingWorkspace.PathComparer.Equals(old.Source,row.Source) ||
                SortingWorkspace.PathComparer.Equals(old.Destination,row.Destination) ||
                SortingWorkspace.PathComparer.Equals(old.OriginalArchive,row.OriginalArchive) ||
                SortingWorkspace.PathComparer.Equals(old.OutputArchive,row.OutputArchive))
                return false;
        }
        return true;
    }

    public StagedMoveEpochEvidence InspectStagedMoveEpoch()
    {
        lock(_dbGate)
        {
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT installation,predecessor_digest,predecessor_rows,candidate_json,candidate_digest " +
                "FROM move_epoch_ticket WHERE id=1";
            using var reader=cmd.ExecuteReader();
            if(!reader.Read()) return new StagedMoveEpochEvidence(false,false,0,
                "No staged successor; no new Move permitted.");
            string install=reader.GetString(0), oldDigest=reader.GetString(1);
            int rows=checked((int)reader.GetInt64(2));
            string json=reader.GetString(3), hash=reader.GetString(4);
            reader.Close();
            if(json.Length>32768 || oldDigest.Length!=64 || hash.Length!=64 || rows<1 ||
                install!=MoveInstallationId().ToString("N"))
                return new StagedMoveEpochEvidence(true,false,rows,"Ticket database identity invalid.");
            DurableMoveItem? candidate;
            try {candidate=JsonSerializer.Deserialize<DurableMoveItem>(json);}
            catch(JsonException) {candidate=null;}
            if(candidate is null || candidate.InstallationId.ToString("N")!=install ||
                CandidateDigest(candidate)!=hash ||
                !string.Equals(JsonSerializer.Serialize(candidate),json,StringComparison.Ordinal))
                return new StagedMoveEpochEvidence(true,false,rows,"Ticket candidate binding invalid.");
            var proof=InspectTerminalMoveCloseout();
            if(!proof.Verified || proof.RecordedRows!=rows ||
                TerminalMoveJournalDigest(install)!=(rows,oldDigest) ||
                !NoPriorSourceOrDestination(candidate) || !ValidateProposedMove(candidate))
                return new StagedMoveEpochEvidence(true,false,rows,
                    "Prior native history, SQLite dependencies or candidate bytes/paths changed.");
            return new StagedMoveEpochEvidence(true,true,rows,
                "Exact successor proposal staged and reverified. NOT a Move execution grant.");
        }
    }

    public StagedMoveEpochEvidence StageNextMoveEpochProposal(DurableMoveItem candidate)
    {
        lock(_dbGate)
        {
            RequireNoR30Writer();
            if(candidate.InstallationId!=MoveInstallationId() ||
                !ValidateProposedMove(candidate) || !NoPriorSourceOrDestination(candidate))
                throw new IOException("Successor Move proposal invalid, stale, overlapping or unowned.");
            var proof=InspectTerminalMoveCloseout();
            if(!proof.Verified || proof.RecordedRows<1 || HasUnreconciledCopy)
                throw new IOException("No freshly verified, terminal historical Move closeout.");
            var install=MoveInstallationId().ToString("N");
            var head=TerminalMoveJournalDigest(install);
            if(head.Count!=proof.RecordedRows)
                throw new IOException("Historical journal changed during staged review.");
            string json=JsonSerializer.Serialize(candidate);
            if(json.Length>32768) throw new IOException("Staged Move review is oversized.");
            var digest=CandidateDigest(candidate);
            using var tx=_connection.BeginTransaction();
            // Race barrier: SQLite serializes this read with the ticket INSERT.
            using (var cmd=_connection.CreateCommand())
            {
                cmd.Transaction=tx;
                cmd.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM rename_closeout),"+
                    "(SELECT copy_risk FROM workspace_checkpoint WHERE id=1)";
                using var rd=cmd.ExecuteReader();
                if(!rd.Read() || rd.GetInt64(0)!=0 || rd.GetInt64(1)!=head.Count ||
                    rd.GetInt64(2)!=0 || rd.GetInt64(3)!=0 ||
                    (!rd.IsDBNull(4) && rd.GetInt64(4)!=0))
                    throw new IOException("Dependent operation history changed before ticket insertion.");
            }
            if(TerminalMoveJournalDigest(install,tx)!=head)
                throw new IOException("Move history changed inside ticket transaction.");
            using(var cmd=_connection.CreateCommand())
            {
                cmd.Transaction=tx;
                cmd.CommandText="SELECT installation,predecessor_digest,predecessor_rows,candidate_json,candidate_digest " +
                    "FROM move_epoch_ticket WHERE id=1";
                using var r=cmd.ExecuteReader();
                if(r.Read())
                {
                    if(r.GetString(0)!=install || r.GetString(1)!=head.Digest ||
                        r.GetInt64(2)!=head.Count || r.GetString(3)!=json || r.GetString(4)!=digest)
                        throw new IOException("An existing staged Move proposal is immutable; no retargeting.");
                }
                else
                {
                    r.Close();
                    cmd.CommandText="INSERT INTO move_epoch_ticket(id,installation,predecessor_digest,"+
                        "predecessor_rows,candidate_json,candidate_digest) " +
                        "VALUES(1,$install,$old,$count,$candidate,$new)";
                    cmd.Parameters.AddWithValue("$install",install);
                    cmd.Parameters.AddWithValue("$old",head.Digest);
                    cmd.Parameters.AddWithValue("$count",head.Count);
                    cmd.Parameters.AddWithValue("$candidate",json);
                    cmd.Parameters.AddWithValue("$new",digest);
                    cmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
            var result=InspectStagedMoveEpoch();
            if(!result.Verified) throw new IOException("Staged ticket could not be reverified.");
            return result;
        }
    }
}
