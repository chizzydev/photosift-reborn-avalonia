using System.Security.Cryptography;
using System.Text.Json;

namespace PhotoSiftReborn;

/// <summary>
/// An observation of the entire installation, not just the current session.
/// A terminal row is NOT a successor grant, and a changed saved workspace
/// revision never promotes any row into new native filesystem authority.
/// </summary>
public enum UnifiedHistoryKind
{
    Fresh,
    ActiveOwned,
    AllTerminalVerified,
    NeedsRecovery,
    ForeignMixed,
    UnsupportedScope
}

public sealed record UnifiedHistoryAssessment(
    UnifiedHistoryKind Kind, int CopyRows, int MoveRows, int RenameRows,
    int RejectRows, int AuxiliaryRows, bool CopyRisk, string Explanation)
{
    public bool AuthorizesNativeOperation => false;
    public bool GrantsSuccessorEpoch => false;
    public int TotalRows => CopyRows + MoveRows + RenameRows + RejectRows;
}

public sealed partial class WorkspaceSessionStore
{
    private sealed record HistoryBytes(string Id, string Session, string Json);

    // This method cannot run a workspace action, mutate SQL, create a receipt,
    // or consume an authorization. It is NOT used in place of any coordinator's
    // strict existing operation/history checks. The only positive terminal
    // observation in this initial increment is ONE native-proven undone row.
    public UnifiedHistoryAssessment InspectUnifiedHistory()
    {
        lock (_dbGate)
        {
            int copies = AllCopyHistoryCount();
            int moves = AllMoveHistoryCount();
            int renames = AllRenameHistoryCount();
            int rejects = AllRejectHistoryCount();
            var risk = HasUnreconciledCopy;
            int auxiliary=CountAuxiliaryHistoryRows();
            UnifiedHistoryAssessment Report(UnifiedHistoryKind kind,string why)
                =>new(kind,copies,moves,renames,rejects,auxiliary,risk,why);
            if (!NativeFileCapability.IsSupported ||
                !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
                return Report(UnifiedHistoryKind.UnsupportedScope,
                    "This installation has no verified native filesystem identity support.");
            if (risk)
                return Report(UnifiedHistoryKind.NeedsRecovery,
                    "An earlier Copy may not be reconciled. Preserve the saved journal and inspect recovery.");
            if (auxiliary>0)
                return Report(UnifiedHistoryKind.NeedsRecovery,
                    "Historical receipts, closeouts or consent artifacts exist. Their complete predecessor chain must be checked.");
            if (copies + moves + renames + rejects == 0)
                return Report(UnifiedHistoryKind.Fresh,
                    "No prior native operation journal exists in this installation. Fresh does not authorize a write.");
            // Never silently evaluate only one operation family when multiple
            // histories exist. An append-only cross-family predecessor ledger
            // is needed before independently validated mixes can be promoted.
            int families=(copies>0?1:0)+(moves>0?1:0)+(renames>0?1:0)+(rejects>0?1:0);
            if (families>1)
                return Report(UnifiedHistoryKind.ForeignMixed,
                    "Multiple operation histories exist. No unified predecessor chain is established.");
            // We cannot infer an entire same-type predecessor chain merely
            // by inspecting one last item. Existing type-specific receipts
            // remain essential and may describe distinct sessions/photos.
            if (copies + moves + renames + rejects != 1)
                return Report(UnifiedHistoryKind.NeedsRecovery,
                    "Multiple historical rows require verified predecessor receipts before further action.");
            try
            {
                if (copies == 1)
                {
                    var raw=SingleHistoryBytes("copy_items");
                    var item=JsonSerializer.Deserialize<DurableCopyItem>(raw.Json);
                    if (item is null || item.Id == Guid.Empty || item.SessionId == Guid.Empty ||
                        item.Id.ToString("N")!=raw.Id || item.SessionId.ToString("N")!=raw.Session)
                        return Report(UnifiedHistoryKind.NeedsRecovery,"Copy journal identity is inconsistent.");
                    bool stateProven=item.Error is null && (item.State switch {
                        "Committed" => CheckCopyOwnedFile(item.Destination,item.TargetIdentity,
                            item.SourceHash,item.SourceLength),
                        "Undone" => !File.Exists(item.Destination) && !Directory.Exists(item.Destination) &&
                            CheckCopyOwnedFile(item.Recovery,item.TargetIdentity,
                                item.SourceHash,item.SourceLength),
                        _ => false
                    });
                    return ProvenStatus(item.State,stateProven,"Copy",Report);
                }
                if (moves == 1)
                {
                    var raw=SingleHistoryBytes("move_items");
                    var item=JsonSerializer.Deserialize<DurableMoveItem>(raw.Json);
                    if (item is null || item.Id==Guid.Empty || item.SessionId==Guid.Empty ||
                        item.InstallationId!=MoveInstallationId() ||
                        item.Id.ToString("N")!=raw.Id || item.SessionId.ToString("N")!=raw.Session)
                        return Report(UnifiedHistoryKind.NeedsRecovery,"Move journal identity is inconsistent.");
                    return ProvenStatus(item.State,
                        item.Error is null && DurableMoveCoordinator.ProveTerminalUndoneEvidence(item),"Move",Report);
                }
                if (renames == 1)
                {
                    var raw=SingleHistoryBytes("rename_items");
                    var item=JsonSerializer.Deserialize<DurableRenameItem>(raw.Json);
                    if (item is null || item.Id==Guid.Empty || item.SessionId==Guid.Empty ||
                        item.InstallationId!=MoveInstallationId() ||
                        item.Id.ToString("N")!=raw.Id || item.SessionId.ToString("N")!=raw.Session ||
                        item.ReviewedDirectoryIdentity is null)
                        return Report(UnifiedHistoryKind.NeedsRecovery,"Rename journal lacks anchored ownership evidence.");
                    return ProvenStatus(item.State,item.Error is null && DurableRenameCoordinator.ProveState(item),
                        "Rename",Report);
                }
                {
                    var raw=SingleHistoryBytes("reject_items");
                    var item=JsonSerializer.Deserialize<DurableRejectItem>(raw.Json);
                    if (item is null || item.Id==Guid.Empty || item.SessionId==Guid.Empty ||
                        item.InstallationId!=MoveInstallationId() ||
                        item.Id.ToString("N")!=raw.Id || item.SessionId.ToString("N")!=raw.Session)
                        return Report(UnifiedHistoryKind.NeedsRecovery,"Reject journal identity is inconsistent.");
                    string parent=Path.GetDirectoryName(item.Source) ?? "";
                    var observer=DurableRejectCoordinator.ForOrdinaryHistoricalEvidence(this,parent);
                    bool verified=item.Error is null && (item.State switch {
                        "Undone" => observer.ProveTerminalUndone(item,item.InstallationId,historicalOnly:true),
                        "Committed" => observer.Inspect() is {Evidence:"OWNED_ARCHIVE_VERIFIED"},
                        _ => false
                    });
                    return ProvenStatus(item.State,verified,"Reject",Report);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or JsonException or NullReferenceException or
                InvalidOperationException or NotSupportedException or OverflowException)
            {
                return Report(UnifiedHistoryKind.NeedsRecovery,
                    "Historical ownership could not be verified ("+ex.GetType().Name+"). Preserve all files.");
            }
        }
    }

    private static UnifiedHistoryAssessment ProvenStatus(string state,bool evidence,
        string family,Func<UnifiedHistoryKind,string,UnifiedHistoryAssessment> report)
    {
        if(!evidence)
            return report(UnifiedHistoryKind.NeedsRecovery,
                family+" history is incomplete or does not match present native files.");
        if(state=="Committed")
            return report(UnifiedHistoryKind.ActiveOwned,
                family+" output/recovery currently verifies, but remains an active operation.");
        if(state=="Undone")
            return report(UnifiedHistoryKind.AllTerminalVerified,
                "One "+family+" Undo verifies its native evidence. This is NOT a receipt chain or new operation approval.");
        return report(UnifiedHistoryKind.NeedsRecovery,
            "The historical "+family+" state is not terminal.");
    }

    // A database may have zero operation rows yet retain a grant/closeout
    // capable of affecting later operations. In this version NONE of these
    // are promoted; each requires an explicit chain parser before use.
    private int CountAuxiliaryHistoryRows()
    {
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT "+string.Join("+",new[]{
            "rename_closeout", "move_closeout", "move_epoch_ticket",
            "move_epoch_grant", "move_gui_successor", "move_epoch_ledger",
            "move_epoch_authorization", "move_gui_third_session",
            "reject_terminal_receipts", "reject_successor_grants",
            "reject_gui_second_session", "copy_successor_grants"
        }.Select(table=>"(SELECT COUNT(*) FROM "+table+")"));
        return checked(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    private HistoryBytes SingleHistoryBytes(string table)
    {
        // Callers supply an internal, enumerated literal. NEVER accept a
        // user-supplied table identifier as SQL text.
        if(table is not ("copy_items" or "move_items" or "rename_items" or "reject_items"))
            throw new InvalidDataException("Unsupported history table.");
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="SELECT id,session,json FROM "+table;
        using var rd=cmd.ExecuteReader();
        if(!rd.Read()) throw new InvalidDataException("Expected native journal row missing.");
        string id=rd.GetString(0),session=rd.GetString(1),json=rd.GetString(2);
        if(json.Length>32768 || rd.Read())
            throw new InvalidDataException("Unexpected journal row count/length.");
        return new HistoryBytes(id,session,json);
    }

    private static bool CheckCopyOwnedFile(string path,string? identity,string hash,long length)
    {
        if(identity is null || hash is null || hash.Length!=64 || length<1 ||
            !File.Exists(path) || SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(path)!)) return false;
        try
        {
            var before=new FileInfo(path);
            long ticks=before.LastWriteTimeUtc.Ticks;
            if(before.Length!=length || NativeFileCapability.Identity(path)!=identity)return false;
            using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                if(Convert.ToHexString(SHA256.HashData(fs))!=hash)return false;
            var after=new FileInfo(path);
            return after.Length==length && after.LastWriteTimeUtc.Ticks==ticks &&
                NativeFileCapability.Identity(path)==identity;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException){return false;}
    }
}
