using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R15: time-local, read-only proof of one marked-generated Copy A ->
/// Move B -> Rename C -> Reject D committed lineage after a cold restart.
/// Neither this report nor a positive four-handle observation authorizes
/// Reject Undo, replay, a fifth operation, general mixed history or GUI use.
/// </summary>
public sealed record M6JP4R15FourthFamilyObservation(Guid RejectId,bool IsFreshWitness,
    string Explanation,string? Fingerprint=null)
{
    public bool IsDurableProof => false;
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool CanReplayReject => false;
    public bool CanModifyJournal => false;
}

public sealed partial class WorkspaceSessionStore
{
    public M6JP4R15FourthFamilyObservation InspectM6JP4R15CommittedGeneratedFourth(
        Guid session)
    {
        lock(_dbGate)
        {
            Guid rejectId=Guid.Empty;
            try
            {
                M6JP4R12GeneratedRenameReject.RequireFixture(this);
                if(session==Guid.Empty || HasUnreconciledCopy ||
                    AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                    AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                    CountAuxiliaryHistoryRows()!=0)
                    throw new IOException("P4-R15 exact committed four-family cardinality refused.");

                // SQL id/session and serialized records are read together, rather
                // than accepting object-only projections which could hide an
                // altered row identity. Raw Copy history must still match the
                // digest sealed BEFORE the reviewed Move was consumed.
                static (string Id,string Session,string Json) Raw(
                    Microsoft.Data.Sqlite.SqliteConnection db,string table)
                {
                    if(table is not ("copy_items" or "move_items" or
                        "rename_items" or "reject_items"))
                        throw new IOException("Unknown journal family.");
                    using var cmd=db.CreateCommand();
                    cmd.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                    using var rd=cmd.ExecuteReader();
                    if(!rd.Read())throw new IOException("Journal missing.");
                    var row=(rd.GetString(0),rd.GetString(1),rd.GetString(2));
                    if(rd.Read() || row.Item1.Length!=32 || row.Item2.Length!=32 ||
                        row.Item3.Length>32768)
                        throw new IOException("Duplicate or oversized historical journal.");
                    return row;
                }
                long versionBefore=M6JDataVersion();
                var copyRaw=Raw(_connection,"copy_items");
                var moveRaw=Raw(_connection,"move_items");
                var renameRaw=Raw(_connection,"rename_items");
                var rejectRaw=Raw(_connection,"reject_items");
                var copy=JsonSerializer.Deserialize<DurableCopyItem>(copyRaw.Json)
                    ?? throw new IOException("Copy journal unreadable.");
                var move=JsonSerializer.Deserialize<DurableMoveItem>(moveRaw.Json)
                    ?? throw new IOException("Move journal unreadable.");
                var rename=JsonSerializer.Deserialize<DurableRenameItem>(renameRaw.Json)
                    ?? throw new IOException("Rename journal unreadable.");
                var reject=JsonSerializer.Deserialize<DurableRejectItem>(rejectRaw.Json)
                    ?? throw new IOException("Reject journal unreadable.");
                rejectId=reject.Id;
                Guid install=MoveInstallationId();
                bool ExactRow(Guid id,Guid rowSession,(string Id,string Session,string Json) raw) =>
                    id!=Guid.Empty && id.ToString("N")==raw.Id &&
                    rowSession==session && rowSession.ToString("N")==raw.Session;
                if(!ExactRow(copy.Id,copy.SessionId,copyRaw) ||
                    !ExactRow(move.Id,move.SessionId,moveRaw) ||
                    !ExactRow(rename.Id,rename.SessionId,renameRaw) ||
                    !ExactRow(reject.Id,reject.SessionId,rejectRaw) ||
                    move.InstallationId!=install || rename.InstallationId!=install ||
                    reject.InstallationId!=install ||
                    copy.State!="Committed" || move.State!="Committed" ||
                    rename.State!="Committed" || reject.State!="Committed" ||
                    copy.Error is not null || move.Error is not null ||
                    rename.Error is not null || reject.Error is not null ||
                    copy.PhotoId==move.PhotoId || move.PhotoId!=rename.PhotoId ||
                    reject.PhotoId!=rename.PhotoId ||
                    reject.SessionRevision!=rename.Revision ||
                    copy.TargetIdentity is null || move.TargetIdentity is null ||
                    move.TempIdentity is null ||
                    move.SourceHash!=rename.Hash ||
                    move.SourceLength!=rename.Length ||
                    move.Destination!=rename.Source ||
                    move.TargetIdentity!=rename.NativeIdentity ||
                    rename.Target!=reject.Source ||
                    rename.NativeIdentity!=reject.SourceIdentity ||
                    rename.Hash!=reject.SourceHash ||
                    rename.Length!=reject.SourceLength ||
                    rename.WriteTicks!=reject.SourceWriteTicks ||
                    rename.ReviewedDirectoryIdentity is null)
                    throw new IOException("P4-R15 committed four-family identity or lineage changed.");
                var review=M6JReadReview(move.Id)
                    ?? throw new IOException("P4-R15 consumed Move review missing.");
                if(review.State!="Consumed" || review.Family!="Move" ||
                    review.Installation!=install.ToString("N") ||
                    review.Session!=session.ToString("N") ||
                    review.ReviewDigest!=M6JReviewDigest(review.CandidateDigest,
                        review.HistoryDigest,review.CheckpointDigest))
                    throw new IOException("P4-R15 original Move review seal changed.");
                var candidate=M6JDecodeStored(review);
                if(candidate.Family!="Move" || candidate.Id!=move.Id ||
                    candidate.SessionId!=session || candidate.PhotoId!=move.PhotoId ||
                    candidate.Source!=move.Source ||
                    candidate.Destination!=move.Destination ||
                    candidate.SourceIdentity!=move.SourceIdentity ||
                    candidate.SourceHash!=move.SourceHash ||
                    candidate.SourceLength!=move.SourceLength ||
                    candidate.SourceWriteTicks!=move.SourceWriteTicks)
                    throw new IOException("P4-R15 consumed Move candidate changed.");
                var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach(string value in new[]{copyRaw.Id,copyRaw.Session,copyRaw.Json})
                    history.Append(value.Length).Append(':').Append(value);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                if(M6JHash(history.ToString())!=review.HistoryDigest)
                    throw new IOException("P4-R15 originally approved Copy journal bytes changed.");

                string fixture=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                string output=Path.Combine(fixture,"output");
                string parent=Path.GetDirectoryName(reject.Source)
                    ?? throw new IOException("P4-R15 missing Reject parent.");
                bool Empty(string path)=>!File.Exists(path) && !Directory.Exists(path);
                if(!Path.IsPathFullyQualified(reject.Source) ||
                    !Path.IsPathFullyQualified(reject.Archive) ||
                    Path.GetFullPath(reject.Source)!=reject.Source ||
                    Path.GetFullPath(reject.Archive)!=reject.Archive ||
                    reject.Source.Normalize(NormalizationForm.FormC)!=reject.Source ||
                    reject.Archive.Normalize(NormalizationForm.FormC)!=reject.Archive ||
                    !SortingWorkspace.PathComparer.Equals(parent,Path.GetDirectoryName(reject.Archive)) ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(parent),output) ||
                    !Path.GetFileName(reject.Archive).Equals(
                        ".photosift-reborn-generated-reject-"+reject.Id.ToString("N")+
                        Path.GetExtension(reject.Source),StringComparison.Ordinal) ||
                    !SortingWorkspace.PathComparer.Equals(
                        Path.GetDirectoryName(rename.Source),parent) ||
                    !Empty(reject.Source) || !Empty(rename.Source) ||
                    !Empty(move.Temp) || !Empty(move.OutputArchive) ||
                    !Empty(copy.Temp) || !Empty(copy.Recovery) ||
                    SortingWorkspace.HasLinkedExistingAncestor(parent) ||
                    NativeFileCapability.DirectoryIdentity(parent)!=rename.ReviewedDirectoryIdentity ||
                    NativeFileCapability.DeviceId(reject.Archive)!=NativeFileCapability.DeviceId(parent) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(reject.Archive) ||
                    NativeFileCapability.Identity(reject.Archive)!=reject.SourceIdentity ||
                    new FileInfo(reject.Archive).Length!=reject.SourceLength ||
                    new FileInfo(reject.Archive).LastWriteTimeUtc.Ticks!=reject.SourceWriteTicks)
                    throw new IOException("P4-R15 archived Reject or exact target path is no longer owned.");

                var pins=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
                    new(copy.Source,copy.SourceIdentity,copy.SourceHash,
                        copy.SourceLength,copy.SourceWriteTicks),
                    new(copy.Destination,copy.TargetIdentity,copy.SourceHash,copy.SourceLength),
                    new(move.OriginalArchive,move.SourceIdentity,move.SourceHash,
                        move.SourceLength,move.SourceWriteTicks),
                    new(reject.Archive,reject.SourceIdentity,reject.SourceHash,
                        reject.SourceLength,reject.SourceWriteTicks));
                if(!pins.IsFreshWitness || pins.IsDurableProof ||
                    pins.AuthorizesNativeOperation || pins.AuthorizesSelectiveUndo)
                    throw new IOException("P4-R15 four simultaneous pinned source proofs refused.");
                if(Raw(_connection,"copy_items")!=copyRaw ||
                    Raw(_connection,"move_items")!=moveRaw ||
                    Raw(_connection,"rename_items")!=renameRaw ||
                    Raw(_connection,"reject_items")!=rejectRaw ||
                    M6JReadReview(move.Id)!=review || M6JDataVersion()!=versionBefore)
                    throw new IOException("P4-R15 journal or consumed grant drifted during observation.");
                return new(rejectId,true,
                    "Time-local marked-generated four-family ownership witnessed; absolutely no Undo or replay permission.",
                    M6JHash("M6J-P4R15-Four-Family-Readonly-v1\n"+
                        copyRaw.Json+moveRaw.Json+renameRaw.Json+rejectRaw.Json+
                        review.ReviewDigest+pins.ObservationFingerprint));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or
                NotSupportedException or JsonException or OverflowException or
                NullReferenceException)
            {
                return new(rejectId,false,
                    "P4-R15 generated four-family observation refused ("+
                    ex.GetType().Name+"); preserve the original journals.");
            }
        }
    }
}
