using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// M6J-A2 frozen candidate for future review. No capability or grant is
/// carried by this record. No native coordinator consumes it in this revision.
/// </summary>
public sealed record M6JProspectiveCandidate(
    Guid Id, string Family, Guid SessionId, Guid PhotoId,
    string Source, string Destination, string SourceIdentity,
    string SourceHash, long SourceLength, long SourceWriteTicks);

public sealed record M6JProspectiveEvidence(Guid CandidateId, string State,
    bool BasisStillMatches, string Explanation)
{
    // Deliberately constant, including for a previously Reviewed proposal.
    public bool AuthorizesNativeOperation => false;
    public bool CanConsumeIntoIntent => false;
    public bool AuthorizesSelectiveUndo => false;
}

public sealed partial class WorkspaceSessionStore
{
    private sealed record M6JStoredReview(
        string Id, string Installation, string Session, string Family,
        string CandidateJson, string CandidateDigest, string HistoryDigest,
        string CheckpointDigest, string State, string? ReviewDigest);

    private sealed record M6JBasis(string HistoryDigest, string CheckpointDigest);

    private static string M6JHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string M6JCanonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0 ||
            !Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetFullPath(path),path,StringComparison.Ordinal) ||
            !string.Equals(path.Normalize(NormalizationForm.FormC),path,StringComparison.Ordinal))
            throw new IOException("M6J candidate path must be absolute, canonical and NFC-normalized.");
        return path.ToUpperInvariant(); // Conservative case-folded alias refusal, including on Linux.
    }

    private static bool M6JNativeSourceMatches(M6JProspectiveCandidate candidate)
    {
        try
        {
            if (!NativeFileCapability.IsSupported ||
                !NativeFileCapability.IsSingleLinkRegularFile(candidate.Source) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(candidate.Source)!) ||
                !Directory.Exists(Path.GetDirectoryName(candidate.Destination)) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(candidate.Destination)!) ||
                File.Exists(candidate.Destination) || Directory.Exists(candidate.Destination) ||
                HasCaseFoldedNfcTargetAlias(candidate.Destination) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(candidate.Source) ||
                NativeFileCapability.Identity(candidate.Source)!=candidate.SourceIdentity)
                return false;
            var first = new FileInfo(candidate.Source);
            if (first.Length!=candidate.SourceLength ||
                first.LastWriteTimeUtc.Ticks!=candidate.SourceWriteTicks) return false;
            using var stream = new FileStream(candidate.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            var after = new FileInfo(candidate.Source);
            return hash==candidate.SourceHash &&
                after.Length==candidate.SourceLength &&
                after.LastWriteTimeUtc.Ticks==candidate.SourceWriteTicks &&
                NativeFileCapability.Identity(candidate.Source)==candidate.SourceIdentity;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or InvalidOperationException)
        { return false; }
    }

    private static void M6JValidateCandidate(M6JProspectiveCandidate c)
    {
        if (c is null || c.Id==Guid.Empty || c.SessionId==Guid.Empty || c.PhotoId==Guid.Empty ||
            c.Family is not ("Copy" or "Move" or "Rename" or "Reject") ||
            string.IsNullOrWhiteSpace(c.SourceIdentity) ||
            c.SourceHash is null || c.SourceHash.Length!=64 ||
            c.SourceHash.Any(x=>!Uri.IsHexDigit(x) || char.IsLower(x)) ||
            c.SourceLength<0 || c.SourceWriteTicks<=0)
            throw new IOException("M6J exact candidate identity/byte evidence incomplete.");
        string source=M6JCanonical(c.Source);
        string target=M6JCanonical(c.Destination);
        if (source==target || target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
            source.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new IOException("M6J source and proposed destination overlap.");
    }

    private (string Raw, WorkspaceSessionSnapshot Snapshot, string Digest) M6JSavedCheckpoint()
    {
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
        using var reader=cmd.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(1)!=0)
            throw new IOException("No safe saved workspace checkpoint or unresolved copy-risk flag.");
        string json=reader.GetString(0);
        if (json.Length>64*1024*1024 || reader.Read())
            throw new IOException("Invalid checkpoint; cannot stage native review.");
        var snapshot=JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(json)
            ?? throw new IOException("Malformed saved checkpoint.");
        return (json,snapshot,M6JHash(json));
    }

    // Binds every raw SQL row in ALL families to the exact read-only proposal.
    // An unrecognized closeout/grant is refused by InspectM6JReadOnlyDependencies.
    // This digest is a change detector, NOT a cryptographic authenticity claim.
    private string M6JRawHistoryDigest()
    {
        var builder = new StringBuilder("M6J-A2-RawHistory-v1\n");
        foreach (string table in new[]{"copy_items","move_items","rename_items","reject_items"})
        {
            builder.Append(table).Append('\n');
            using var cmd=_connection.CreateCommand();
            cmd.CommandText="SELECT id,session,json FROM "+table+" ORDER BY id";
            using var reader=cmd.ExecuteReader();
            int rows=0;
            while(reader.Read())
            {
                if (++rows>512) throw new IOException("Too much history for bounded exact review.");
                for(int index=0;index<3;index++)
                {
                    string value=reader.GetString(index);
                    if(value.Length>32768)throw new IOException("Oversized historic journal.");
                    builder.Append(value.Length).Append(':').Append(value);
                }
                builder.Append('\n');
            }
        }
        return M6JHash(builder.ToString());
    }

    // Existing per-row native history proof MUST pass before evaluating the
    // proposed candidate. This is diagnostic: not a way to waive existing locks.
    private M6JBasis M6JRequireProspectiveBasis(M6JProspectiveCandidate candidate)
    {
        M6JValidateCandidate(candidate);
        var saved=M6JSavedCheckpoint();
        if(saved.Snapshot.SessionId!=candidate.SessionId ||
            !saved.Snapshot.Photos.Any(photo=>photo.Id==candidate.PhotoId &&
                SortingWorkspace.PathComparer.Equals(photo.Path,candidate.Source) &&
                photo.NativeIdentity==candidate.SourceIdentity &&
                photo.Length==candidate.SourceLength &&
                photo.LastWriteUtcTicks==candidate.SourceWriteTicks))
            throw new IOException("M6J proposed original differs from saved session/photo identity.");
        if(!M6JNativeSourceMatches(candidate))
            throw new IOException("M6J source, destination or native file evidence has changed.");
        var prior=InspectM6JReadOnlyDependencies();
        if (prior.Kind!=M6JDependencyKind.EmptyObservation &&
            prior.Kind!=M6JDependencyKind.LexicallyDisjointNotAuthorized)
            throw new IOException("M6J historical native ownership is unresolved or dependent.");
        var existing = new List<M6JOperationFootprint>();
        foreach(var entry in new[]{
            (Table:"copy_items", Family:"Copy"),
            (Table:"move_items", Family:"Move"),
            (Table:"rename_items", Family:"Rename"),
            (Table:"reject_items", Family:"Reject")})
        {
            var buffered=new List<(string Id,string Session,string Json)>();
            using (var command=_connection.CreateCommand())
            {
                command.CommandText="SELECT id,session,json FROM "+entry.Table+" ORDER BY id";
                using var reader=command.ExecuteReader();
                while(reader.Read())
                {
                    if(existing.Count+buffered.Count>=512)
                        throw new IOException("M6J predecessor inventory exceeded 512.");
                    buffered.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2)));
                }
            }
            foreach(var raw in buffered)
            {
                var row=entry.Family switch {
                    "Copy"=>M6JCopy(raw.Id,raw.Session,raw.Json,true),
                    "Move"=>M6JMove(raw.Id,raw.Session,raw.Json,true),
                    "Rename"=>M6JRename(raw.Id,raw.Session,raw.Json,true),
                    "Reject"=>M6JReject(raw.Id,raw.Session,raw.Json,true),
                    _=>throw new IOException("Unknown operation family.")
                };
                existing.Add(row);
            }
        }
        existing.Add(new M6JOperationFootprint(candidate.Family,candidate.Id.ToString("N"),
            candidate.SessionId.ToString("N"),candidate.PhotoId,"Undone",true,new[]{
                new M6JPathClaim("ProposedSource",candidate.Source),
                new M6JPathClaim("ProposedDestination",candidate.Destination)
            }));
        var conflicts=M6JDependencyInspector.Analyze(existing);
        if (conflicts.Kind!=M6JDependencyKind.LexicallyDisjointNotAuthorized ||
            conflicts.Edges.Count!=0)
            throw new IOException("M6J proposed candidate overlaps recorded native history.");
        if(!M6JNativeSourceMatches(candidate))
            throw new IOException("M6J proposed native original changed during history inspection.");
        return new(M6JRawHistoryDigest(),saved.Digest);
    }

    private M6JStoredReview? M6JReadReview(Guid id)
    {
        using var command=_connection.CreateCommand();
        command.CommandText="SELECT id,installation,session,family,candidate_json,candidate_digest,"+
            "history_digest,checkpoint_digest,state,review_digest FROM m6j_review_proposals WHERE id=$id";
        command.Parameters.AddWithValue("$id",id.ToString("N"));
        using var reader=command.ExecuteReader();
        if (!reader.Read())return null;
        var result=new M6JStoredReview(reader.GetString(0),reader.GetString(1),
            reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),
            reader.GetString(6),reader.GetString(7),reader.GetString(8),
            reader.IsDBNull(9)?null:reader.GetString(9));
        if(reader.Read())throw new IOException("Duplicate M6J review ID.");
        return result;
    }

    private static string M6JReviewDigest(string candidateDigest, string historyDigest,
        string checkpointDigest) => M6JHash("M6J-A2-Reviewed-v1\n"+candidateDigest+"\n"+
            historyDigest+"\n"+checkpointDigest);

    private static M6JProspectiveCandidate M6JDecodeStored(M6JStoredReview stored)
    {
        if (stored.CandidateJson.Length>32768 || stored.Id.Length!=32 ||
            stored.CandidateDigest!=M6JHash(stored.CandidateJson))
            throw new IOException("M6J candidate payload was replaced or exceeds bounds.");
        var candidate=JsonSerializer.Deserialize<M6JProspectiveCandidate>(stored.CandidateJson)
            ?? throw new IOException("M6J candidate cannot be parsed.");
        M6JValidateCandidate(candidate);
        if (candidate.Id.ToString("N")!=stored.Id ||
            candidate.SessionId.ToString("N")!=stored.Session ||
            candidate.Family!=stored.Family)
            throw new IOException("M6J stored review key/session/family does not match payload.");
        return candidate;
    }

    public M6JProspectiveEvidence StageM6JProspectiveReview(M6JProspectiveCandidate candidate)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            var basis=M6JRequireProspectiveBasis(candidate);
            string raw=JsonSerializer.Serialize(candidate);
            if(raw.Length>32768)throw new IOException("M6J proposal exceeds size limit.");
            string installation=MoveInstallationId().ToString("N");
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO m6j_review_proposals("+
                "id,installation,session,family,candidate_json,candidate_digest,"+
                "history_digest,checkpoint_digest,state,review_digest) VALUES("+
                "$id,$installation,$session,$family,$json,$digest,$history,$checkpoint,'Staged',NULL)";
            cmd.Parameters.AddWithValue("$id",candidate.Id.ToString("N"));
            cmd.Parameters.AddWithValue("$installation",installation);
            cmd.Parameters.AddWithValue("$session",candidate.SessionId.ToString("N"));
            cmd.Parameters.AddWithValue("$family",candidate.Family);
            cmd.Parameters.AddWithValue("$json",raw);
            cmd.Parameters.AddWithValue("$digest",M6JHash(raw));
            cmd.Parameters.AddWithValue("$history",basis.HistoryDigest);
            cmd.Parameters.AddWithValue("$checkpoint",basis.CheckpointDigest);
            if(cmd.ExecuteNonQuery()!=1)throw new IOException("M6J stage not recorded.");
            tx.Commit();
            return new(candidate.Id,"Staged",true,"Durable prospective review only; no write permission.");
        }
    }

    public M6JProspectiveEvidence ReviewM6JProspectiveCandidate(Guid id,
        M6JProspectiveCandidate exactReviewedCandidate, bool ownerAcknowledged)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            if (!ownerAcknowledged) throw new IOException("Fresh explicit exact-review acknowledgement required.");
            var stored=M6JReadReview(id) ?? throw new IOException("M6J proposal missing.");
            var candidate=M6JDecodeStored(stored);
            if (candidate!=exactReviewedCandidate || stored.State!="Staged" ||
                stored.Installation!=MoveInstallationId().ToString("N") ||
                stored.ReviewDigest is not null)
                throw new IOException("M6J staged proposal differs from exact reviewed candidate.");
            var basis=M6JRequireProspectiveBasis(candidate);
            if(basis.HistoryDigest!=stored.HistoryDigest ||
                basis.CheckpointDigest!=stored.CheckpointDigest)
                throw new IOException("M6J saved checkpoint/history changed since staging.");
            string seal=M6JReviewDigest(stored.CandidateDigest,basis.HistoryDigest,
                basis.CheckpointDigest);
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="UPDATE m6j_review_proposals SET state='Reviewed',review_digest=$seal "+
                "WHERE id=$id AND state='Staged' AND review_digest IS NULL AND "+
                "candidate_digest=$digest AND history_digest=$history AND checkpoint_digest=$checkpoint";
            cmd.Parameters.AddWithValue("$seal",seal);
            cmd.Parameters.AddWithValue("$id",stored.Id);
            cmd.Parameters.AddWithValue("$digest",stored.CandidateDigest);
            cmd.Parameters.AddWithValue("$history",basis.HistoryDigest);
            cmd.Parameters.AddWithValue("$checkpoint",basis.CheckpointDigest);
            if(cmd.ExecuteNonQuery()!=1)throw new IOException("M6J review was consumed or changed.");
            tx.Commit();
            return new(id,"Reviewed",true,
                "Explicit review sealed durably. Native Intent consumption is NOT implemented.");
        }
    }

    public M6JProspectiveEvidence InspectM6JProspectiveReview(Guid id)
    {
        lock (_dbGate)
        {
            try
            {
                var stored = M6JReadReview(id);
                if (stored is null)
                    return new(id, "Missing", false, "No saved proposal; no native authority.");
                var candidate=M6JDecodeStored(stored);
                if(stored.Installation!=MoveInstallationId().ToString("N") ||
                    stored.State is not ("Staged" or "Reviewed" or "Cancelled" or "Consumed") ||
                    (stored.State=="Staged" && stored.ReviewDigest is not null) ||
                    ((stored.State=="Reviewed" || stored.State=="Consumed") && stored.ReviewDigest!=M6JReviewDigest(
                        stored.CandidateDigest,stored.HistoryDigest,stored.CheckpointDigest)))
                    throw new IOException("M6J stored review state, seal or installation changed.");
                if(stored.State=="Cancelled")
                    return new(id,"Cancelled",false,"Cancelled; cannot be revived.");
                if(stored.State=="Consumed")
                    return new(id,"Consumed",false,
                        "Reviewed record was consumed with a journal Intent; never fresh permission.");
                var basis=M6JRequireProspectiveBasis(candidate);
                bool matches=stored.HistoryDigest==basis.HistoryDigest &&
                    stored.CheckpointDigest==basis.CheckpointDigest;
                return new(id,stored.State,matches,
                    matches ? "Exact review snapshot and native source currently match; NO write grant." :
                    "Historical files/checkpoint changed; proposal is stale and cannot be used.");
            }
            catch(Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or InvalidDataException or
                JsonException or NotSupportedException or OverflowException)
            { return new(id,"NeedsAttention",false,
                "Read-only prospective review verification refused ("+ex.GetType().Name+")."); }
        }
    }

    public M6JProspectiveEvidence CancelM6JProspectiveReview(Guid id)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            var stored=M6JReadReview(id) ?? throw new IOException("M6J review not found.");
            _=M6JDecodeStored(stored);
            if (stored.Installation!=MoveInstallationId().ToString("N") ||
                stored.State is not ("Staged" or "Reviewed"))
                throw new IOException("M6J review cannot be cancelled twice or from unknown state.");
            using var tx=_connection.BeginTransaction();
            using var cmd=_connection.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="UPDATE m6j_review_proposals SET state='Cancelled' WHERE id=$id AND state=$state";
            cmd.Parameters.AddWithValue("$id",id.ToString("N"));
            cmd.Parameters.AddWithValue("$state",stored.State);
            if(cmd.ExecuteNonQuery()!=1)throw new IOException("M6J cancellation changed concurrently.");
            tx.Commit();
            return new(id,"Cancelled",false,"No write authority; immutable cancelled review row retained.");
        }
    }
}
