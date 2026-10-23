using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R21: time-local, READ-ONLY native observation of exactly two committed,
/// disjoint generated Copy A and Move B journals. This is not a grant, a
/// successor review, an Undo authority, or proof of arbitrary interleaving.
/// The acceptance scope has one row per family; a third row must refuse.
/// </summary>
public sealed record M6JP4R21IndependentPairObservation(
    bool IsFreshWitness, Guid CopyId, Guid MoveId, string Reason)
{
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool GrantsSuccessorEpoch => false;
    public bool IsDurableProof => false;
    public bool CanReplay => false;
    public bool IsReadOnlyDiagnostic => true;
}

public sealed partial class WorkspaceSessionStore
{
    public M6JP4R21IndependentPairObservation InspectM6JP4R21GeneratedCommittedPair(Guid session)
    {
        lock (_dbGate)
        {
            Guid copyId=Guid.Empty, moveId=Guid.Empty;
            try
            {
                M6JP4R12GeneratedRenameReject.RequireFixture(this);
                if (session==Guid.Empty || !NativeFileCapability.IsSupported ||
                    HasUnreconciledCopy || AllCopyHistoryCount()!=1 ||
                    AllMoveHistoryCount()!=1 || AllRenameHistoryCount()!=0 ||
                    AllRejectHistoryCount()!=0 || CountAuxiliaryHistoryRows()!=0)
                    throw new IOException("R21 requires exactly one committed Copy and one committed Move, with no other operation history.");

                // Read actual SQLite keys and JSON, not just unkeyed projection;
                // a forged id or session on disk must never appear as an owner.
                (string Id,string Session,string Json) Raw(string table)
                {
                    if (table is not ("copy_items" or "move_items"))
                        throw new IOException("Unexpected R21 journal family.");
                    using var command=_connection.CreateCommand();
                    command.CommandText="SELECT id,session,json FROM "+table+" LIMIT 2";
                    using var reader=command.ExecuteReader();
                    if (!reader.Read()) throw new IOException("R21 row missing.");
                    var value=(reader.GetString(0),reader.GetString(1),reader.GetString(2));
                    if (reader.Read() || value.Item1.Length!=32 || value.Item2.Length!=32 ||
                        value.Item3.Length>32768) throw new IOException("R21 row identity or count invalid.");
                    return value;
                }
                long version=M6JDataVersion();
                var ar=Raw("copy_items");var br=Raw("move_items");
                var a=JsonSerializer.Deserialize<DurableCopyItem>(ar.Json)
                    ?? throw new IOException("R21 Copy JSON invalid.");
                var b=JsonSerializer.Deserialize<DurableMoveItem>(br.Json)
                    ?? throw new IOException("R21 Move JSON invalid.");
                copyId=a.Id;moveId=b.Id;
                if (a.Id==Guid.Empty || b.Id==Guid.Empty || a.Id==b.Id ||
                    a.SessionId!=session || b.SessionId!=session ||
                    ar.Id!=a.Id.ToString("N") || br.Id!=b.Id.ToString("N") ||
                    ar.Session!=session.ToString("N") || br.Session!=session.ToString("N") ||
                    b.InstallationId!=MoveInstallationId() ||
                    a.State!="Committed" || b.State!="Committed" ||
                    a.Error is not null || b.Error is not null ||
                    a.PhotoId==Guid.Empty || b.PhotoId==Guid.Empty || a.PhotoId==b.PhotoId ||
                    a.TargetIdentity is null || b.TargetIdentity is null ||
                    a.SourceIdentity is null || b.SourceIdentity.Length==0)
                    throw new IOException("R21 committed journal lineage or identity invalid.");

                var root=Directory.GetParent(Path.GetDirectoryName(DatabasePath)!)!.FullName;
                var src=Path.Combine(root,"source");var output=Path.Combine(root,"output");
                bool Empty(string path)=>!File.Exists(path) && !Directory.Exists(path);
                static bool Native(string path,string expected,string hash,long length) =>
                    NativeFileCapability.IsSingleLinkRegularFile(path) &&
                    NativeFileCapability.Identity(path)==expected &&
                    new FileInfo(path).Length==length &&
                    SortingWorkspace.ComputeSha256Async(path).GetAwaiter().GetResult()==hash;
                var active=new[]{a.Source,a.Destination,b.Source,b.Destination,
                    b.OriginalArchive,b.OutputArchive,a.Recovery};
                if (active.Any(x=>string.IsNullOrWhiteSpace(x) || !Path.IsPathFullyQualified(x) ||
                    !SortingWorkspace.PathComparer.Equals(x,Path.GetFullPath(x))) ||
                    active.Distinct(SortingWorkspace.PathComparer).Count()!=active.Length ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(a.Source),src) ||
                    !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(b.Source),src) ||
                    !SortingWorkspace.PathComparer.Equals(b.OutputRoot,output) ||
                    !Directory.Exists(Path.GetDirectoryName(a.Destination)!) ||
                    !Directory.Exists(Path.GetDirectoryName(b.Destination)!) ||
                    !a.Destination.StartsWith(output+Path.DirectorySeparatorChar,SortingWorkspace.PathComparison) ||
                    !b.Destination.StartsWith(output+Path.DirectorySeparatorChar,SortingWorkspace.PathComparison) ||
                    SortingWorkspace.HasLinkedExistingAncestor(src) ||
                    SortingWorkspace.HasLinkedExistingAncestor(output) ||
                    SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(a.Destination)!) ||
                    SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(b.Destination)!) ||
                    !Empty(a.Temp) || !Empty(a.Recovery) || !Empty(b.Temp) ||
                    !Empty(b.Source) || !Empty(b.OutputArchive))
                    throw new IOException("R21 exact generated roots, paths, or no-replay states invalid.");

                // The Move reviewer sealed the actual Copy-row bytes before B Intent.
                // A lexical non-edge alone is NOT proof of that consumed review.
                var review=M6JReadReview(b.Id)
                    ?? throw new IOException("R21 consumed Move review absent.");
                if (review.State!="Consumed" || review.Family!="Move" ||
                    review.Installation!=b.InstallationId.ToString("N") ||
                    review.Session!=session.ToString("N") ||
                    review.ReviewDigest!=M6JReviewDigest(review.CandidateDigest,
                        review.HistoryDigest,review.CheckpointDigest))
                    throw new IOException("R21 consumed Move approval seal invalid.");
                var candidate=M6JDecodeStored(review);
                if (candidate.Id!=b.Id || candidate.Family!="Move" ||
                    candidate.SessionId!=b.SessionId || candidate.PhotoId!=b.PhotoId ||
                    candidate.Source!=b.Source || candidate.Destination!=b.Destination ||
                    candidate.SourceIdentity!=b.SourceIdentity || candidate.SourceHash!=b.SourceHash ||
                    candidate.SourceLength!=b.SourceLength ||
                    candidate.SourceWriteTicks!=b.SourceWriteTicks)
                    throw new IOException("R21 consumed Move reviewed candidate changed.");
                var historical=new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach (string part in new[]{ar.Id,ar.Session,ar.Json})
                    historical.Append(part.Length).Append(':').Append(part);
                historical.Append("\nmove_items\nrename_items\nreject_items\n");
                if (M6JHash(historical.ToString())!=review.HistoryDigest)
                    throw new IOException("R21 Copy predecessor changed after Move approval.");

                var op=Path.GetDirectoryName(b.OriginalArchive)!;
                var rootMarker=Path.Combine(src,".photosift-reborn-recovery", ".photosift-move-owner-v1");
                var opMarker=Path.Combine(op,".photosift-operation-v1");
                if (!SortingWorkspace.PathComparer.Equals(op,
                        Path.Combine(src,".photosift-reborn-recovery",
                            b.InstallationId.ToString("N"),session.ToString("N"),b.Id.ToString("N"))) ||
                    !SortingWorkspace.PathComparer.Equals(b.OriginalArchive,
                        Path.Combine(op,Path.GetFileName(b.Source))) ||
                    !SortingWorkspace.PathComparer.Equals(b.OutputArchive,
                        Path.Combine(op,"installed-"+Path.GetFileName(b.Destination))) ||
                    SortingWorkspace.HasLinkedExistingAncestor(op) ||
                    !File.Exists(rootMarker) || !File.Exists(opMarker) ||
                    SortingWorkspace.HasSymlinkOrReparsePoint(rootMarker) ||
                    SortingWorkspace.HasSymlinkOrReparsePoint(opMarker) ||
                    File.ReadAllText(rootMarker)!="PhotoSift Reborn Move v1:"+
                        b.InstallationId.ToString("N")+"\n" ||
                    File.ReadAllText(opMarker)!="PhotoSift Reborn Move operation "+b.Id.ToString("N")+"\n" ||
                    !Native(a.Source,a.SourceIdentity,a.SourceHash,a.SourceLength) ||
                    !Native(a.Destination,a.TargetIdentity,a.SourceHash,a.SourceLength) ||
                    !Native(b.OriginalArchive,b.SourceIdentity,b.SourceHash,b.SourceLength) ||
                    !Native(b.Destination,b.TargetIdentity,b.SourceHash,b.SourceLength) ||
                    new[]{a.SourceIdentity,a.TargetIdentity,b.SourceIdentity,b.TargetIdentity}
                        .Distinct(StringComparer.Ordinal).Count()!=4 ||
                    M6JDataVersion()!=version)
                    throw new IOException("R21 current native ownership or SQLite observation is no longer fresh.");
                return new(true,a.Id,b.Id,
                    "Exactly two time-local native owned generated rows with consumed reviewed Move. This grants NO mutation or repeatability authority.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or JsonException or
                OverflowException or NotSupportedException or NullReferenceException)
            {
                return new(false,copyId,moveId,
                    "R21 refused stale/incomplete ownership: "+ex.GetType().Name);
            }
        }
    }
}
