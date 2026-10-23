using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PhotoSiftReborn;

/// <summary>
/// R25 P0 is a DURABLE, separately acknowledged review seal for an exact
/// third generated-file operation: two genuinely committed, separately
/// reviewed Copy predecessors followed by ONE proposed Move. It never writes
/// Move Intent or touches files. Existing Move executors DO NOT consume it.
/// A future positive native Move writer requires a separate acceptance gate.
/// </summary>
public sealed record M6JP4R25ThirdMoveReviewReceipt(Guid ReviewId, string ReviewedDigest,
    string CopyHistoryDigest, string SavedCheckpointDigest)
{
    public bool IsDurablyReviewedOnly => true;
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesMoveIntent => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool AuthorizesOrdinaryMixedFamily => false;
}

public sealed partial class WorkspaceSessionStore
{
    /// <summary>Called only for a separately marked, disposable test fixture.
    /// Creates one owner-acknowledged, operation-ID-scoped Reviewed row. The
    /// exact native journal, grant, approved workspace and candidate must be
    /// freshly valid at review time; no Move execution is granted by this row.</summary>
    internal M6JP4R25ThirdMoveReviewReceipt ApproveM6JP4R25ThirdGeneratedMoveReview(
        SortingWorkspace work, PreparedCopy prepared, M6JProspectiveCandidate candidate,
        bool explicitlyAcknowledged)
    {
        lock (_dbGate)
        {
            RequireNoR30Writer();
            M6JP4R12GeneratedRenameReject.RequireFixture(this);
            if(!explicitlyAcknowledged || candidate is null || prepared is null ||
               candidate.Family!="Move" || candidate.Id==Guid.Empty ||
               candidate.SessionId==Guid.Empty || candidate.PhotoId==Guid.Empty ||
               candidate.SessionId!=prepared.SessionId || candidate.PhotoId!=prepared.PhotoId ||
               candidate.Source!=prepared.Source || candidate.Destination!=prepared.Destination ||
               candidate.SourceIdentity!=prepared.Identity || candidate.SourceHash!=prepared.Hash ||
               candidate.SourceLength!=prepared.Length || candidate.SourceWriteTicks!=prepared.WriteTicks ||
               !work.IsCurrentPlanApproved || work.SessionId!=prepared.SessionId ||
               work.CurrentPlanId!=prepared.PlanId || work.Revision!=prepared.Revision ||
               work.GetApprovedPlan().Count!=1 ||
               !SortingWorkspace.PathComparer.Equals(work.GetApprovedPlan()[0].Source,prepared.Source) ||
               !SortingWorkspace.PathComparer.Equals(work.GetApprovedPlan()[0].Destination,prepared.Destination) ||
               JsonSerializer.Serialize(work.CaptureSession())!=JsonSerializer.Serialize(Load()) ||
               AllCopyHistoryCount()!=2 || AllMoveHistoryCount()!=0 ||
               AllRenameHistoryCount()!=0 || AllRejectHistoryCount()!=0 ||
               CountAuxiliaryHistoryRows()!=1 || HasUnreconciledCopy)
                throw new IOException("R25 requires a fresh exact third photo, independently approved plan, two committed Copies, and explicit acknowledgement.");
            M6JValidateCandidate(candidate);
            RequireIndependentLatestCopyUndo(candidate.SessionId); // actual native files + consumed predecessor Copy grant
            var existing=CopyItems(candidate.SessionId);
            if(existing.Count!=2 || existing[0].Id==existing[1].Id ||
               existing.Any(r=>r.State!="Committed" || r.Error is not null ||
                 r.PhotoId==candidate.PhotoId || r.SourceIdentity==candidate.SourceIdentity ||
                 r.TargetIdentity==candidate.SourceIdentity))
                throw new IOException("R25 previous Copy photo/identity or terminal state is not distinct.");
            string[] prior=existing.SelectMany(r=>new[]{r.Source,r.Destination,r.Temp,r.Recovery}).ToArray();
            string[] next={candidate.Source,candidate.Destination};
            foreach(var p in next)
            {
                string normalized=M6JCanonical(p);
                if(prior.Any(x=>M6JCanonical(x)==normalized))
                    throw new IOException("R25 candidate overlaps independently owned prior Copy path.");
            }
            var saved=M6JSavedCheckpoint();
            if(saved.Snapshot.SessionId!=candidate.SessionId ||
               saved.Snapshot.Revision!=prepared.Revision ||
               !saved.Snapshot.Photos.Any(p=>p.Id==candidate.PhotoId &&
                   p.Path==candidate.Source && p.NativeIdentity==candidate.SourceIdentity &&
                   p.Length==candidate.SourceLength && p.LastWriteUtcTicks==candidate.SourceWriteTicks) ||
               !M6JNativeSourceMatches(candidate))
                throw new IOException("R25 generated third source/checkpoint identity is stale.");

            // Capture and re-check the exact raw predecessor rows and single-use
            // consumed grant. Historical SHA is a change detector, not a MAC.
            var immutable=existing.ToDictionary(x=>x.Id.ToString("N"),x=>JsonSerializer.Serialize(x),
                StringComparer.Ordinal);
            string grantRow;
            using(var cmd=_connection.CreateCommand())
            {
                cmd.CommandText="SELECT id || ':' || installation || ':' || session || ':' || "+
                    "predecessor_id || ':' || predecessor_digest || ':' || checkpoint_digest || ':' || "+
                    "candidate_json || ':' || used_intent || ':' || state FROM copy_successor_grants";
                using var rd=cmd.ExecuteReader();
                if(!rd.Read())throw new IOException("R25 previous one-use grant is missing.");
                grantRow=rd.GetString(0);
                if(rd.Read() || grantRow.Length>32768)
                    throw new IOException("R25 ambiguous or oversized previous grant.");
            }
            string history=M6JRawHistoryDigest(),checkpoint=saved.Digest;
            string raw=JsonSerializer.Serialize(candidate);
            string candidateDigest=M6JHash(raw);
            string reviewSeal=M6JReviewDigest(candidateDigest,history,checkpoint);
            var install=MoveInstallationId().ToString("N");
            using var tx=_connection.BeginTransaction();
            using(var sql=_connection.CreateCommand())
            {
                sql.Transaction=tx;
                sql.CommandText="SELECT id,json FROM copy_items ORDER BY id";
                using var rd=sql.ExecuteReader();
                int count=0;
                while(rd.Read())
                {
                    string id=rd.GetString(0),json=rd.GetString(1);
                    if(++count>2 || !immutable.TryGetValue(id,out var priorJson) || priorJson!=json)
                        throw new IOException("R25 predecessor raw JSON changed inside approval transaction.");
                }
                if(count!=2)throw new IOException("R25 exact two predecessor Copy rows disappeared.");
            }
            using(var sql=_connection.CreateCommand())
            {
                sql.Transaction=tx;
                sql.CommandText="SELECT id || ':' || installation || ':' || session || ':' || "+
                    "predecessor_id || ':' || predecessor_digest || ':' || checkpoint_digest || ':' || "+
                    "candidate_json || ':' || used_intent || ':' || state FROM copy_successor_grants";
                using var rd=sql.ExecuteReader();
                if(!rd.Read() || rd.GetString(0)!=grantRow || rd.Read())
                    throw new IOException("R25 one-use Copy grant changed inside transaction.");
            }
            using(var sql=_connection.CreateCommand())
            {
                sql.Transaction=tx;
                sql.CommandText="SELECT json,copy_risk FROM workspace_checkpoint WHERE id=1";
                using var rd=sql.ExecuteReader();
                if(!rd.Read() || rd.GetInt64(1)!=0 || M6JHash(rd.GetString(0))!=checkpoint || rd.Read())
                    throw new IOException("R25 reviewed saved workspace changed inside transaction.");
            }
            using(var sql=_connection.CreateCommand())
            {
                sql.Transaction=tx;
                sql.CommandText="SELECT (SELECT COUNT(*) FROM copy_items),"+
                    "(SELECT COUNT(*) FROM move_items),"+
                    "(SELECT COUNT(*) FROM rename_items),"+
                    "(SELECT COUNT(*) FROM reject_items),"+
                    "(SELECT COUNT(*) FROM m6j_review_proposals)";
                using var rd=sql.ExecuteReader();
                if(!rd.Read() || rd.GetInt64(0)!=2 || rd.GetInt64(1)!=0 ||
                   rd.GetInt64(2)!=0 || rd.GetInt64(3)!=0 || rd.GetInt64(4)!=0)
                    throw new IOException("R25 additional history or previous review appeared.");
            }
            if(!M6JNativeSourceMatches(candidate) ||
               existing.Any(r=>!NativeFileCapability.IsSingleLinkRegularFile(r.Source) ||
                   !NativeFileCapability.IsSingleLinkRegularFile(r.Destination) ||
                   !CheckCopyOwnedFile(r.Source,r.SourceIdentity,r.SourceHash,r.SourceLength) ||
                   !CheckCopyOwnedFile(r.Destination,r.TargetIdentity,r.SourceHash,r.SourceLength)))
                throw new IOException("R25 fresh native files diverged during review transaction.");
            using(var sql=_connection.CreateCommand())
            {
                sql.Transaction=tx;
                sql.CommandText="INSERT INTO m6j_review_proposals("+
                    "id,installation,session,family,candidate_json,candidate_digest,"+
                    "history_digest,checkpoint_digest,state,review_digest) VALUES("+
                    "$id,$installation,$session,'Move',$candidate,$digest,$history,$checkpoint,'Reviewed',$seal)";
                sql.Parameters.AddWithValue("$id",candidate.Id.ToString("N"));
                sql.Parameters.AddWithValue("$installation",install);
                sql.Parameters.AddWithValue("$session",candidate.SessionId.ToString("N"));
                sql.Parameters.AddWithValue("$candidate",raw);
                sql.Parameters.AddWithValue("$digest",candidateDigest);
                sql.Parameters.AddWithValue("$history",history);
                sql.Parameters.AddWithValue("$checkpoint",checkpoint);
                sql.Parameters.AddWithValue("$seal",reviewSeal);
                if(sql.ExecuteNonQuery()!=1)
                    throw new IOException("R25 third-Move reviewed seal was not recorded exactly once.");
            }
            tx.Commit();
            return new(candidate.Id,reviewSeal,history,checkpoint);
        }
    }
}
