using System.Text;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R5: independently reconstruct the exactly-one generated Copy -> Move
/// transaction lineage after restart. Historical row bytes and fresh native
/// ownership must agree. This is NOT a transaction, a durable capability,
/// a coordinator entry point, an epoch, or permission to mutate files.
/// </summary>
public sealed record M6JP4R5ColdLineageReport(Guid MoveId, bool Verified,
    string Explanation, string? ObservationFingerprint = null)
{
    public bool AuthorizesFileOperation => false;
    public bool AuthorizesMoveExecution => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool CanRepairJournal => false;
}

public sealed partial class WorkspaceSessionStore
{
    public M6JP4R5ColdLineageReport InspectM6JP4R5GeneratedConsumedLineage(Guid moveId)
    {
        lock (_dbGate)
        {
            try
            {
                if (moveId == Guid.Empty || !OperatingSystem.IsWindows())
                    throw new IOException("Windows generated-lineage observation only.");
                string stateFolder = Path.GetDirectoryName(DatabasePath)
                    ?? throw new IOException("Missing SQLite state directory.");
                string root = Directory.GetParent(stateFolder)?.FullName
                    ?? throw new IOException("Missing fixture root.");
                string marker = Path.Combine(root, "P3B2_TEST_ONLY.marker");
                if (Path.GetFileName(stateFolder) != "state" ||
                    !Path.GetFileName(root).StartsWith("PHOTOSIFT_REBORN_P1_DISPOSABLE_",
                        StringComparison.Ordinal) ||
                    !File.Exists(marker) ||
                    File.ReadAllText(marker) != "synthetic-interruption-only" ||
                    SortingWorkspace.HasLinkedExistingAncestor(root))
                    throw new IOException("Lineage is restricted to marked disposable test files.");
                if (HasUnreconciledCopy || AllCopyHistoryCount() != 1 ||
                    AllMoveHistoryCount() != 1 || AllRenameHistoryCount() != 0 ||
                    AllRejectHistoryCount() != 0 || CountAuxiliaryHistoryRows() != 0)
                    throw new IOException("Unexpected historical operation/auxiliary cardinality.");
                long versionBefore = M6JDataVersion();
                static (string Id, string Session, string Json) SingleRaw(
                    Microsoft.Data.Sqlite.SqliteConnection db, string table)
                {
                    if (table is not ("copy_items" or "move_items"))
                        throw new IOException("Unknown journal family.");
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "SELECT id,session,json FROM " + table + " LIMIT 2";
                    using var rd = cmd.ExecuteReader();
                    if (!rd.Read()) throw new IOException("Missing exact journal.");
                    string id = rd.GetString(0), session = rd.GetString(1), json = rd.GetString(2);
                    if (rd.Read() || id.Length != 32 || session.Length != 32 ||
                        json.Length > 32768)
                        throw new IOException("Duplicate, malformed or oversized journal.");
                    return (id, session, json);
                }
                var copyRaw = SingleRaw(_connection, "copy_items");
                var moveRaw = SingleRaw(_connection, "move_items");
                if (moveRaw.Id != moveId.ToString("N"))
                    throw new IOException("Requested Move differs from stored row identity.");
                var copy = JsonSerializer.Deserialize<DurableCopyItem>(copyRaw.Json)
                    ?? throw new IOException("Copy JSON is invalid.");
                var move = JsonSerializer.Deserialize<DurableMoveItem>(moveRaw.Json)
                    ?? throw new IOException("Move JSON is invalid.");
                if (JsonSerializer.Serialize(move) != moveRaw.Json ||
                    copy.Id.ToString("N") != copyRaw.Id ||
                    copy.SessionId.ToString("N") != copyRaw.Session ||
                    copy.State != "Committed" || copy.Error is not null ||
                    move.Id != moveId || move.Id.ToString("N") != moveRaw.Id ||
                    move.SessionId.ToString("N") != moveRaw.Session ||
                    move.InstallationId != MoveInstallationId() ||
                    move.State != "Intent" || move.Error is not null ||
                    move.TempIdentity is not null || move.TargetIdentity is not null ||
                    move.PhotoId == copy.PhotoId ||
                    copy.TargetIdentity is null)
                    throw new IOException("Native journal identity or state not exact.");
                var proposal = M6JReadReview(moveId)
                    ?? throw new IOException("Move review disappeared.");
                if (proposal.State != "Consumed" || proposal.Family != "Move" ||
                    proposal.Installation != move.InstallationId.ToString("N") ||
                    proposal.Session != move.SessionId.ToString("N") ||
                    proposal.ReviewDigest != M6JReviewDigest(proposal.CandidateDigest,
                        proposal.HistoryDigest, proposal.CheckpointDigest))
                    throw new IOException("Consumed review seal does not match the Move.");
                var exactCandidate = M6JDecodeStored(proposal);
                if (exactCandidate.Family != "Move" || exactCandidate.Id != move.Id ||
                    exactCandidate.SessionId != move.SessionId ||
                    exactCandidate.PhotoId != move.PhotoId ||
                    exactCandidate.Source != move.Source ||
                    exactCandidate.Destination != move.Destination ||
                    exactCandidate.SourceIdentity != move.SourceIdentity ||
                    exactCandidate.SourceHash != move.SourceHash ||
                    exactCandidate.SourceLength != move.SourceLength ||
                    exactCandidate.SourceWriteTicks != move.SourceWriteTicks)
                    throw new IOException("Reviewed proposal does not match the durable Move Intent.");
                var history = new StringBuilder("M6J-A2-RawHistory-v1\ncopy_items\n");
                foreach (string value in new[]{copyRaw.Id, copyRaw.Session, copyRaw.Json})
                    history.Append(value.Length).Append(':').Append(value);
                history.Append("\nmove_items\nrename_items\nreject_items\n");
                if (M6JHash(history.ToString()) != proposal.HistoryDigest)
                    throw new IOException("Committed predecessor's exact historical bytes changed.");
                string beforeWitness = RequireP4R3PreCommitWitness(copy, move);
                // A second observation and raw row reread catch changes while
                // this particular read-only call is in progress; not a native lock
                // held across its return, nor an executable capability.
                string afterWitness = RequireP4R3PreCommitWitness(copy, move);
                if (!string.Equals(beforeWitness, afterWitness, StringComparison.Ordinal) ||
                    SingleRaw(_connection, "copy_items") != copyRaw ||
                    SingleRaw(_connection, "move_items") != moveRaw ||
                    M6JReadReview(moveId) != proposal ||
                    M6JDataVersion() != versionBefore)
                    throw new IOException("Journal/fixture changed within read-only inspection.");
                var fingerprint = M6JHash("M6J-P4R5-Readonly-v1\n" +
                    copyRaw.Id + "\n" + copyRaw.Json + "\n" +
                    moveRaw.Id + "\n" + moveRaw.Json + "\n" +
                    proposal.ReviewDigest + "\n" + afterWitness);
                return new(moveId, true,
                    "Exact generated journal lineage and fresh Windows native bytes matched at observation time; NO operation authority.",
                    fingerprint);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or NotSupportedException or
                JsonException or OverflowException or NullReferenceException)
            {
                return new(moveId, false,
                    "Generated-only cold-lineage inspection refused (" + e.GetType().Name + "); preserve all journals.");
            }
        }
    }
}
