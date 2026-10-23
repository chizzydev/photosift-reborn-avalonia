using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhotoSiftReborn;

public sealed partial class WorkspaceSessionStore
{
    // The M6J-A1 audit reads every SQLite operation family and corroborates
    // the existing per-row native proof inventory. Never invoked inside
    // Put*Intent, Apply, Undo, Stage, Approve or a successor grant path.
    // SQLite data_version flags other-connection commits during the pass.
    // It is still not an immutable authorization snapshot.
    public M6JDependencyAssessment InspectM6JReadOnlyDependencies()
    {
        lock (_dbGate)
        {
            try
            {
                long versionBefore=M6JDataVersion();
                var prior=InspectUnifiedHistoryInventory();
                if (!prior.ScanComplete || prior.Installation.TotalRows>512)
                    return M6JDependencyInspector.Refuse(
                        "Existing native inventory incomplete or oversized; preserve all journal locks.");
                var observed=new Dictionary<string,UnifiedHistoryRowEvidence>(StringComparer.Ordinal);
                foreach (var item in prior.Rows)
                {
                    string key=item.Family+":"+item.JournalId;
                    if (!observed.TryAdd(key,item))
                        return M6JDependencyInspector.Refuse("Duplicate inventory key; preserve history.");
                }
                var footprints=new List<M6JOperationFootprint>();
                foreach (var pair in new [] {
                    (Table:"copy_items",Family:"Copy"),
                    (Table:"move_items",Family:"Move"),
                    (Table:"rename_items",Family:"Rename"),
                    (Table:"reject_items",Family:"Reject")})
                {
                    var rows=new List<(string Id,string Session,string Json)>();
                    using (var cmd=_connection.CreateCommand())
                    {
                        cmd.CommandText="SELECT id,session,json FROM "+pair.Table+" ORDER BY rowid LIMIT 513";
                        using var rd=cmd.ExecuteReader();
                        while(rd.Read())
                        {
                            if (rows.Count+footprints.Count>=512)
                                return M6JDependencyInspector.Refuse("History exceeds 512 bounded records.");
                            rows.Add((rd.GetString(0),rd.GetString(1),rd.GetString(2)));
                        }
                    }
                    foreach (var row in rows)
                    {
                        if (row.Json.Length>32768 || row.Id.Length!=32 || row.Session.Length!=32 ||
                            !observed.TryGetValue(pair.Family+":"+row.Id,out var verified) ||
                            verified.SessionId!=row.Session)
                            return M6JDependencyInspector.Refuse("Native inventory changed or journal key invalid.");
                        bool nativeProven=verified.Proof is
                            UnifiedRowProof.NativeActiveVerified or UnifiedRowProof.NativeTerminalVerified;
                        M6JOperationFootprint footprint=pair.Family switch {
                            "Copy" => M6JCopy(row.Id,row.Session,row.Json,nativeProven),
                            "Move" => M6JMove(row.Id,row.Session,row.Json,nativeProven),
                            "Rename" => M6JRename(row.Id,row.Session,row.Json,nativeProven),
                            "Reject" => M6JReject(row.Id,row.Session,row.Json,nativeProven),
                            _ => throw new InvalidDataException("Unknown family")
                        };
                        if (footprint.State!=verified.State)
                            return M6JDependencyInspector.Refuse("State changed during read-only analysis.");
                        footprints.Add(footprint);
                    }
                }
                if (footprints.Count!=prior.Installation.TotalRows ||
                    observed.Count!=footprints.Count ||
                    versionBefore!=M6JDataVersion())
                    return M6JDependencyInspector.Refuse("History count/version changed during inspection.");
                bool full = prior.Installation.AuxiliaryRows==0 &&
                    !prior.Installation.CopyRisk &&
                    !prior.Rows.Any(x=>x.Proof==UnifiedRowProof.NotVerified);
                return M6JDependencyInspector.Analyze(footprints,full,
                    "Historical native state/auxiliary receipts require new per-epoch proof");
            }
            catch(Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or
                JsonException or UnauthorizedAccessException or ArgumentException or
                OverflowException or NotSupportedException or NullReferenceException)
            {
                return M6JDependencyInspector.Refuse(
                    "Read-only inspection could not establish history ("+ex.GetType().Name+").");
            }
        }
    }

    private long M6JDataVersion()
    {
        using var cmd=_connection.CreateCommand();
        cmd.CommandText="PRAGMA data_version";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static M6JOperationFootprint M6JCopy(string id,string session,string json,bool proved)
    {
        var item=JsonSerializer.Deserialize<DurableCopyItem>(json)
            ?? throw new InvalidDataException("Invalid Copy row");
        CheckFootprintIdentity(id,session,item.Id,item.SessionId);
        return new("Copy",id,session,item.PhotoId,item.State,proved,new[] {
            new M6JPathClaim("Source",item.Source,RecordedNativeIdentity:item.SourceIdentity),
            new M6JPathClaim("Destination",item.Destination,RecordedNativeIdentity:item.TargetIdentity),
            new M6JPathClaim("Temp",item.Temp,RecordedNativeIdentity:item.TempIdentity),
            new M6JPathClaim("Recovery",item.Recovery)
        });
    }

    private static M6JOperationFootprint M6JMove(string id,string session,string json,bool proved)
    {
        var item=JsonSerializer.Deserialize<DurableMoveItem>(json)
            ?? throw new InvalidDataException("Invalid Move row");
        CheckFootprintIdentity(id,session,item.Id,item.SessionId);
        return new("Move",id,session,item.PhotoId,item.State,proved,new[] {
            new M6JPathClaim("Source",item.Source,RecordedNativeIdentity:item.SourceIdentity),
            new M6JPathClaim("Destination",item.Destination,RecordedNativeIdentity:item.TargetIdentity),
            new M6JPathClaim("Temp",item.Temp,RecordedNativeIdentity:item.TempIdentity),
            new M6JPathClaim("OriginalArchive",item.OriginalArchive,RecordedNativeIdentity:item.SourceIdentity),
            new M6JPathClaim("OutputArchive",item.OutputArchive),
            new M6JPathClaim("OutputRoot",item.OutputRoot),
            new M6JPathClaim("OwnedRecoveryRoot",Path.GetDirectoryName(item.OriginalArchive) ?? "",true)
        });
    }

    private static M6JOperationFootprint M6JRename(string id,string session,string json,bool proved)
    {
        var item=JsonSerializer.Deserialize<DurableRenameItem>(json)
            ?? throw new InvalidDataException("Invalid Rename row");
        CheckFootprintIdentity(id,session,item.Id,item.SessionId);
        return new("Rename",id,session,item.PhotoId,item.State,proved,new[] {
            new M6JPathClaim("Source",item.Source,RecordedNativeIdentity:item.NativeIdentity),
            new M6JPathClaim("Target",item.Target,RecordedNativeIdentity:item.NativeIdentity)
        });
    }

    private static M6JOperationFootprint M6JReject(string id,string session,string json,bool proved)
    {
        var item=JsonSerializer.Deserialize<DurableRejectItem>(json)
            ?? throw new InvalidDataException("Invalid Reject row");
        CheckFootprintIdentity(id,session,item.Id,item.SessionId);
        return new("Reject",id,session,item.PhotoId,item.State,proved,new[] {
            new M6JPathClaim("Source",item.Source,RecordedNativeIdentity:item.SourceIdentity),
            new M6JPathClaim("Archive",item.Archive,RecordedNativeIdentity:item.SourceIdentity),
            new M6JPathClaim("OwnedRecoveryRoot",Path.GetDirectoryName(item.Archive) ?? "",true)
        });
    }

    private static void CheckFootprintIdentity(string id,string session,Guid actualId,Guid actualSession)
    {
        if (actualId==Guid.Empty || actualSession==Guid.Empty ||
            actualId.ToString("N")!=id || actualSession.ToString("N")!=session)
            throw new InvalidDataException("Journal identity differs from SQLite key.");
    }
}
