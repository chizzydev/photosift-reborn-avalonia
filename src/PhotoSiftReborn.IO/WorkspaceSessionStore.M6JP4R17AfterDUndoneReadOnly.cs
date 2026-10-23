using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R17: time-local read-only check after committed D Undo,
/// before or after exact C Rename undo, never a general Undo grant.
/// Neither this report nor a positive four-handle observation authorizes
/// Reject Undo, replay, a fifth operation, general mixed history or GUI use.
/// </summary>
internal sealed record M6JP4R17GuardedObservation(Guid RejectId,bool IsFreshWitness,
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
    internal M6JP4R17GuardedObservation InspectM6JP4R17AfterDUndone(
        Guid session, string expectedRenameState)
    {
        lock(_dbGate)
        {
            Guid rejectId=Guid.Empty;
            try
            {
                if(expectedRenameState is not ("Committed" or "UndoIntent" or "Undone"))
                    throw new IOException("P4-R17 forbidden C history state.");
            }
            catch(IOException) { return new(Guid.Empty,false,"P4-R17 refused unrecognized state."); }
            try
            {
                M6JP4R12GeneratedRenameReject.RequireFixture(this);
                if(session==Guid.Empty || HasUnreconciledCopy ||
                    AllCopyHistoryCount()!=1 || AllMoveHistoryCount()!=1 ||
                    AllRenameHistoryCount()!=1 || AllRejectHistoryCount()!=1 ||
                    CountAuxiliaryHistoryRows()!=0)
                    throw new IOException("P4-R17 exact committed four-family cardinality refused.");

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
                    rename.State!=expectedRenameState || reject.State!="Undone" ||
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
                    throw new IOException("P4-R17 committed four-family identity or lineage changed.");
                var review=M6JReadReview(move.Id)
                    ?? throw new IOException("P4-R17 consumed Move review missing.");
                if(review.State!="Consumed" || review.Family!="Move" ||
                    review.Installation!=install.ToString("N") ||
                    review.Session!=session.ToString("N") ||
                    review.ReviewDigest!=M6JReviewDigest(review.CandidateDigest,
                        review.HistoryDigest,review.CheckpointDigest))
                    throw new IOException("P4-R17 original Move review seal changed.");
                var candidate=M6JDecodeStored(review);
                if(candidate.Family!="Move" || candidate.Id!=move.Id ||
                    candidate.SessionId!=session || candidate.PhotoId!=move.PhotoId ||
                    candidate.Source!=move.Source ||
                    candidate.Destination!=move.Destination ||
                    candidate.SourceIdentity!=move.SourceIdentity ||
                    candidate.SourceHash!=move.SourceHash ||
                    candidate.SourceLength!=move.SourceLength ||
                    candidate.SourceWriteTicks!=move.SourceWriteTicks)
                    throw new IOException("P4-R17 consumed Move candidate changed.");
                var history=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach(string value in new[]{copyRaw.Id,copyRaw.Session,copyRaw.Json})
                    history.Append(value.Length).Append(':').Append(value);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                if(M6JHash(history.ToString())!=review.HistoryDigest)
                    throw new IOException("P4-R17 originally approved Copy journal bytes changed.");

                string fixture=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                string output=Path.Combine(fixture,"output");
                string parent=Path.GetDirectoryName(reject.Source)
                    ?? throw new IOException("P4-R17 missing Reject parent.");
                bool Empty(string path)=>!File.Exists(path) && !Directory.Exists(path);
                string ownedC = expectedRenameState=="Undone" ? rename.Source : rename.Target;
                string vacantC = expectedRenameState=="Undone" ? rename.Target : rename.Source;
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
                    !Empty(vacantC) || !Empty(reject.Archive) ||
                    !Empty(move.Temp) || !Empty(move.OutputArchive) ||
                    !Empty(copy.Temp) || !Empty(copy.Recovery) ||
                    SortingWorkspace.HasLinkedExistingAncestor(parent) ||
                    NativeFileCapability.DirectoryIdentity(parent)!=rename.ReviewedDirectoryIdentity ||
                    NativeFileCapability.DeviceId(ownedC)!=NativeFileCapability.DeviceId(parent) ||
                    !NativeFileCapability.IsSingleLinkRegularFile(ownedC) ||
                    NativeFileCapability.Identity(ownedC)!=reject.SourceIdentity ||
                    new FileInfo(ownedC).Length!=reject.SourceLength ||
                    new FileInfo(ownedC).LastWriteTimeUtc.Ticks!=reject.SourceWriteTicks)
                    throw new IOException("P4-R17 archived Reject or exact target path is no longer owned.");

                var pins=M6JP4FreshPinnedPredecessor.InspectGeneratedSelectiveMoveUndo(
                    new(copy.Source,copy.SourceIdentity,copy.SourceHash,
                        copy.SourceLength,copy.SourceWriteTicks),
                    new(copy.Destination,copy.TargetIdentity,copy.SourceHash,copy.SourceLength),
                    new(move.OriginalArchive,move.SourceIdentity,move.SourceHash,
                        move.SourceLength,move.SourceWriteTicks),
                    new(ownedC,reject.SourceIdentity,reject.SourceHash,
                        reject.SourceLength,reject.SourceWriteTicks));
                if(!pins.IsFreshWitness || pins.IsDurableProof ||
                    pins.AuthorizesNativeOperation || pins.AuthorizesSelectiveUndo)
                    throw new IOException("P4-R17 four simultaneous pinned source proofs refused.");
                if(Raw(_connection,"copy_items")!=copyRaw ||
                    Raw(_connection,"move_items")!=moveRaw ||
                    Raw(_connection,"rename_items")!=renameRaw ||
                    Raw(_connection,"reject_items")!=rejectRaw ||
                    M6JReadReview(move.Id)!=review || M6JDataVersion()!=versionBefore)
                    throw new IOException("P4-R17 journal or consumed grant drifted during observation.");
                return new(rejectId,true,
                    "Time-local four-family after-D-undone ownership checked; no write or replay authority.",
                    M6JHash("M6J-P4R17-C-After-D-Undone-Readonly-v1\n"+
                        copyRaw.Json+moveRaw.Json+renameRaw.Json+rejectRaw.Json+
                        review.ReviewDigest+pins.ObservationFingerprint));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or
                NotSupportedException or JsonException or OverflowException or
                NullReferenceException)
            {
                return new(rejectId,false,
                    "P4-R17 generated four-family observation refused ("+
                    ex.GetType().Name+"); preserve the original journals.");
            }
        }
    }
}
