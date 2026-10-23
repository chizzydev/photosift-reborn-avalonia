using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// Diagnostic read-only detail. No row in this inventory is a grant, a receipt,
/// an approval, or evidence that a subsequent epoch may execute. An item can
/// be locally proven while the INSTALLATION still has foreign/mixed history.
/// The inventory is deliberately not a stable authorization snapshot.
/// </summary>
public enum UnifiedRowProof
{
    NativeTerminalVerified,
    NativeActiveVerified,
    NotVerified
}

public sealed record UnifiedHistoryRowEvidence(
    string Family, string JournalId, string SessionId, string State,
    UnifiedRowProof Proof, string Explanation)
{
    public bool AuthorizesNativeOperation => false;
}

public sealed record UnifiedHistoryInventory(
    UnifiedHistoryAssessment Installation,
    IReadOnlyList<UnifiedHistoryRowEvidence> Rows,
    bool ScanComplete, string Explanation)
{
    public bool AuthorizesNativeOperation => false;
    public bool GrantsSuccessorEpoch => false;
    public bool IsRecoveryReviewRequired =>
        !ScanComplete || Installation.Kind is UnifiedHistoryKind.NeedsRecovery or
        UnifiedHistoryKind.ForeignMixed or UnifiedHistoryKind.UnsupportedScope ||
        Rows.Any(x => x.Proof == UnifiedRowProof.NotVerified);
}

public sealed partial class WorkspaceSessionStore
{
    // For a prospective plain-language history/recovery viewer. NOT for a
    // stage/apply/undo guard: native coordinators must revalidate their own
    // exact journal, saved checkpoint, identity, and fresh approval.
    // A bounded synchronous read + native hash pass; call from a worker rather
    // than the Avalonia UI thread. There are no schema changes or SQL writes.
    public UnifiedHistoryInventory InspectUnifiedHistoryInventory()
    {
        lock (_dbGate)
        {
            var installation = InspectUnifiedHistory();
            var entries = new List<UnifiedHistoryRowEvidence>();
            const int maxRows = 512;
            bool complete = true;
            string reason = "Read-only per-row native evidence; never operation authority.";
            if (installation.TotalRows > maxRows)
            {
                return new UnifiedHistoryInventory(installation, entries.AsReadOnly(), false,
                    "History exceeds the bounded inspection limit; no rows promoted.");
            }
            // Read every journal family: a valid row in one family must not
            // hide corruption or an outstanding operation in another.
            foreach (string table in new[] {"copy_items", "move_items", "rename_items", "reject_items"})
            {
                string family = table switch {
                    "copy_items" => "Copy", "move_items" => "Move",
                    "rename_items" => "Rename", "reject_items" => "Reject",
                    _ => throw new InvalidDataException("Unknown native journal")
                };
                try
                {
                    // Drain the SQLite reader BEFORE per-row native proof:
                    // native proof calls other store queries on this connection.
                    // Keeping a reader open across those nested queries would
                    // depend on provider behavior and risks an inconsistent view.
                    var raw = new List<(string Id,string Session,string Json)>();
                    using (var cmd = _connection.CreateCommand())
                    {
                        // Identifiers are internal constants, never user input.
                        cmd.CommandText = "SELECT id,session,json FROM " + table + " ORDER BY rowid LIMIT 513";
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            if (entries.Count + raw.Count == maxRows) {
                                complete = false;
                                reason = "Native journal increased beyond the bounded inspection limit.";
                                break;
                            }
                            string id = reader.GetString(0);
                            string session = reader.GetString(1);
                            string json = reader.GetString(2);
                            raw.Add((id,session,json));
                        }
                    }
                    if (!complete) break;
                    foreach(var row in raw)
                        entries.Add(InspectUnifiedRow(family,row.Id,row.Session,row.Json));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException or JsonException or
                    NullReferenceException or InvalidOperationException or NotSupportedException or OverflowException)
                {
                    complete = false;
                    reason = "Historical inspection stopped safely (" + ex.GetType().Name + ").";
                    break;
                }
            }
            // Reject an inventory with a torn/changed DB row count. SQLite
            // externally changed between statements cannot become authority.
            if (entries.Count != installation.TotalRows)
            {
                complete = false;
                reason = "Native history counts changed or could not all be read.";
            }
            if (entries.Any(x => x.Proof == UnifiedRowProof.NotVerified))
                reason = "One or more historical operations need recovery inspection; no new action approved.";
            if (installation.AuxiliaryRows != 0)
                reason = "Historical auxiliary receipts/grants need a verified predecessor chain; no action approved.";
            if (installation.Kind == UnifiedHistoryKind.ForeignMixed)
                reason = "Mixed native histories have no unified predecessor chain; preserve all history.";
            if (installation.Kind == UnifiedHistoryKind.UnsupportedScope)
            {
                complete = false;
                reason = "Native identity is unsupported; no native historical proof.";
            }
            return new UnifiedHistoryInventory(installation, entries.AsReadOnly(), complete, reason);
        }
    }

    private UnifiedHistoryRowEvidence InspectUnifiedRow(string family, string id,
        string session, string json)
    {
        UnifiedHistoryRowEvidence Refuse(string state, string why) =>
            new(family,id,session,state,UnifiedRowProof.NotVerified,why);
        if (id.Length != 32 || session.Length != 32 || json.Length > 32768 ||
            !Guid.TryParseExact(id,"N",out var nativeId) || nativeId==Guid.Empty ||
            !Guid.TryParseExact(session,"N",out var nativeSession) || nativeSession==Guid.Empty)
            return Refuse("Invalid","Native journal key or payload is invalid.");
        if (!NativeFileCapability.IsSupported ||
            !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
            return Refuse("Unsupported","Native proof unavailable on this operating system.");
        try
        {
            string state;
            bool proved = false;
            bool undone = false;
            var installation = MoveInstallationId();
            switch (family)
            {
                case "Copy":
                {
                    var row = JsonSerializer.Deserialize<DurableCopyItem>(json);
                    if (row is null || row.Id != nativeId || row.SessionId != nativeSession ||
                        row.PhotoId==Guid.Empty || row.SourceHash is null || row.SourceHash.Length!=64)
                        return Refuse("Invalid","Copy journal identity disagrees with the stored key.");
                    state=row.State; undone=state=="Undone";
                    proved = row.Error is null && (state switch {
                        "Committed" => CheckCopyOwnedFile(row.Destination,row.TargetIdentity,row.SourceHash,row.SourceLength),
                        "Undone" => !File.Exists(row.Destination) && !Directory.Exists(row.Destination) &&
                            CheckCopyOwnedFile(row.Recovery,row.TargetIdentity,row.SourceHash,row.SourceLength),
                        _ => false
                    });
                    break;
                }
                case "Move":
                {
                    var row = JsonSerializer.Deserialize<DurableMoveItem>(json);
                    if (row is null || row.Id!=nativeId || row.SessionId!=nativeSession ||
                        row.PhotoId==Guid.Empty || row.InstallationId!=installation)
                        return Refuse("Invalid","Move journal identity/installation disagrees.");
                    state=row.State; undone=state=="Undone";
                    proved=row.Error is null && undone &&
                        DurableMoveCoordinator.ProveTerminalUndoneEvidence(row);
                    break;
                }
                case "Rename":
                {
                    var row = JsonSerializer.Deserialize<DurableRenameItem>(json);
                    if (row is null || row.Id!=nativeId || row.SessionId!=nativeSession ||
                        row.PhotoId==Guid.Empty || row.InstallationId!=installation ||
                        row.ReviewedDirectoryIdentity is null)
                        return Refuse("Invalid","Rename journal has inconsistent anchored identity.");
                    state=row.State; undone=state=="Undone";
                    proved=row.Error is null && (state=="Undone" || state=="Committed") &&
                        DurableRenameCoordinator.ProveState(row);
                    break;
                }
                case "Reject":
                {
                    var row = JsonSerializer.Deserialize<DurableRejectItem>(json);
                    if (row is null || row.Id!=nativeId || row.SessionId!=nativeSession ||
                        row.PhotoId==Guid.Empty || row.InstallationId!=installation ||
                        !Path.IsPathFullyQualified(row.Source))
                        return Refuse("Invalid","Reject journal has inconsistent anchored identity.");
                    state=row.State; undone=state=="Undone";
                    // For a mixed/multiple history, never use Inspect() to
                    // mistakenly prove the first current-session Reject as the
                    // requested row. Historical-only terminal proof is exact.
                    proved=row.Error is null && undone &&
                        DurableRejectCoordinator.ForOrdinaryHistoricalEvidence(this,
                            Path.GetDirectoryName(row.Source)!)
                            .ProveTerminalUndone(row,installation,historicalOnly:true);
                    break;
                }
                default: return Refuse("Invalid","Unknown native journal family.");
            }
            var proof=proved
                ? (undone ? UnifiedRowProof.NativeTerminalVerified : UnifiedRowProof.NativeActiveVerified)
                : UnifiedRowProof.NotVerified;
            return new UnifiedHistoryRowEvidence(family,id,session,state,proof,
                proved ? (undone ? "Restored original/owned evidence verifies now." :
                    "Active owned native evidence verifies now.") :
                    "Unresolved, unsupported or changed native evidence; inspect recovery.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException or JsonException or
            NullReferenceException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            return Refuse("Invalid","Native history cannot be proved (" + ex.GetType().Name + ").");
        }
    }
}
