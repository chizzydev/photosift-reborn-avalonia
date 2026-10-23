using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// A recorded row is NOT an on-disk identity proof or a replay/Undo ticket.
internal sealed record StudioRecordedFamilyRow(Guid Id, StudioNativeFamily Family,
    Guid SessionId, long Sequence, string State, string Source, string Destination,
    string Recovery, string? Error, string? OutputUndoRecovery = null)
{
    // A durable Reject Stage/Approved row remains pending, not a cancelled
    // transient dialog. The same states in other journals need investigation.
    internal StudioRecordedOutcome Outcome => !string.IsNullOrWhiteSpace(Error)
        ? StudioRecordedOutcome.NeedsAttention
        : StudioFamilyHistoryPresentation.Classify(Family, State);
    internal string Display
    {
        get
        {
            string category = Outcome switch
            {
                StudioRecordedOutcome.Completed => "Completed",
                StudioRecordedOutcome.Undone => "Undone",
                StudioRecordedOutcome.Pending => "Pending native Reject proposal",
                _ => "Needs attention"
            };
            return $"{Family} · {Path.GetFileName(Source)} · {category} · recorded";
        }
    }
    internal string NextStep => StudioFamilyHistoryPresentation.NextStep(this);
    internal string ExactDetails =>
        Family.ToString().ToUpperInvariant() + " · RECORDED HISTORY ONLY" + Environment.NewLine +
        "Historical state: " + State + Environment.NewLine +
        "History category: " + Outcome + Environment.NewLine +
        "Next step: " + NextStep + Environment.NewLine +
        "Checked now: Not checked here. Use the recovery inspector for current evidence." + Environment.NewLine +
        "Operation ID: " + Id + Environment.NewLine +
        "Session ID: " + SessionId + Environment.NewLine +
        "Recorded sequence: " + (Family == StudioNativeFamily.Reject
            ? "not tracked by the Reject journal" : Sequence.ToString()) + Environment.NewLine +
        "Exact source: " + Source + Environment.NewLine +
        "Exact destination / renamed target: " + Destination + Environment.NewLine +
        "Exact owned recovery (if recorded): " + (Recovery.Length == 0 ? "not recorded for this family" : Recovery) + Environment.NewLine +
        (OutputUndoRecovery is null ? string.Empty : "Exact output Undo archive: " + OutputUndoRecovery + Environment.NewLine) +
        (string.IsNullOrEmpty(Error) ? "" : "Recorded issue: " + Error + Environment.NewLine) +
        "Recorded state alone is NOT current proof and grants no approval, file change or Undo.";
}

internal enum StudioRecordedOutcome { Completed, Undone, Pending, NeedsAttention }

internal static class StudioFamilyHistoryPresentation
{
    internal static StudioRecordedOutcome Classify(StudioNativeFamily family, string? state) =>
        state switch
        {
            "Committed" => StudioRecordedOutcome.Completed,
            "Undone" => StudioRecordedOutcome.Undone,
            "Staged" or "Approved" when family == StudioNativeFamily.Reject => StudioRecordedOutcome.Pending,
            _ => StudioRecordedOutcome.NeedsAttention
        };

    // Guidance is historical, never a live recheck, approval or Undo capability.
    internal static string NextStep(StudioRecordedFamilyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Outcome == StudioRecordedOutcome.NeedsAttention)
            return "Preserve originals, outputs, recovery and SQLite state. Inspect current evidence; do not retry or start another operation.";
        if (row.Outcome == StudioRecordedOutcome.Pending)
            return "A native Reject proposal is durably recorded. Use its existing guarded recovery controls; closing this page does not cancel it.";
        if (row.Outcome == StudioRecordedOutcome.Completed)
            return row.Family switch
            {
                StudioNativeFamily.Copy => "Inspect current original, output and owned recovery before separately considering verified Copy Undo.",
                StudioNativeFamily.Move => "Inspect current source, destination and both owned recovery locations before separately considering guarded Move Undo.",
                StudioNativeFamily.Rename => "Inspect exact original and renamed paths before separately considering guarded Rename Undo.",
                StudioNativeFamily.Reject => "Inspect original and owned Reject recovery before separately considering guarded restoration; never Trash.",
                _ => throw new ArgumentOutOfRangeException(nameof(row))
            };
        // Undone already recorded: DO NOT instruct another Undo. Historical
        // Undo never verifies current bytes/identity or automatically unlocks.
        return row.Family switch
        {
            StudioNativeFamily.Copy => "Copy Undo recorded. Inspect original and retained owned output archive; no automatic replay or release.",
            StudioNativeFamily.Move => "Move Undo recorded. Inspect restored source and retained output Undo archive; no automatic replay or release.",
            StudioNativeFamily.Rename => "Rename Undo recorded. Inspect restored original name and native identity; verified closeout is separate, not automatic.",
            StudioNativeFamily.Reject => "Reject Undo recorded. Inspect restored original and retained owned journal; saved Reject marks are separate.",
            _ => throw new ArgumentOutOfRangeException(nameof(row))
        };
    }

    internal static string Triage(IReadOnlyList<StudioRecordedFamilyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int attention=rows.Count(x=>x.Outcome==StudioRecordedOutcome.NeedsAttention);
        int pending=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Pending);
        int completed=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Completed);
        int undone=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Undone);
        if(attention>0) return $"{attention} recorded item(s) need attention · {pending} pending native Reject proposal(s). " +
            "Preserve all files and inspect recovery before any operation; status is not current proof.";
        if(pending>0) return $"{pending} durable Reject proposal(s) pending · {completed} completed · {undone} undone. " +
            "The proposal survives leaving this page; check its native controls. Not checked now.";
        if(rows.Count==0) return "No operations recorded in this session. Older or foreign-session journals may still lock actions. Inspect recovery.";
        return completed>0
            ? $"{completed} completed · {undone} undone in this session. Check current recovery evidence before separately considering guarded Undo; no automatic authorization."
            : $"0 completed · {undone} already undone in this session. Undo is recorded, NOT live verification. Inspect current evidence; do not repeat or automatically release recovery locks.";
    }
    // Reads are performed by the existing owner/store; this pure projection
    // receives saved snapshots and NEVER calls reconciliation/native operators.
    internal static StudioRecordedFamilyRow[] Project(Guid session,
        IReadOnlyList<DurableCopyItem> copies, IReadOnlyList<DurableMoveItem> moves,
        IReadOnlyList<DurableRenameItem> renames, IReadOnlyList<DurableRejectItem> rejects)
    {
        if (session == Guid.Empty) throw new InvalidDataException("Missing history session.");
        ArgumentNullException.ThrowIfNull(copies);
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(renames);
        ArgumentNullException.ThrowIfNull(rejects);
        var rows = new List<StudioRecordedFamilyRow>();
        foreach (var item in copies)
            rows.Add(new(item.Id,StudioNativeFamily.Copy,item.SessionId,item.Sequence,item.State,
                item.Source,item.Destination,item.Recovery,item.Error));
        foreach (var item in moves)
            rows.Add(new(item.Id,StudioNativeFamily.Move,item.SessionId,item.Sequence,item.State,
                item.Source,item.Destination,
                item.OriginalArchive,item.Error,item.OutputArchive));
        foreach (var item in renames)
            rows.Add(new(item.Id,StudioNativeFamily.Rename,item.SessionId,item.Sequence,item.State,
                item.Source,item.Target,string.Empty,item.Error));
        foreach (var item in rejects)
            rows.Add(new(item.Id,StudioNativeFamily.Reject,item.SessionId,0,item.State,
                item.Source,"Original relocated into app-owned recovery",item.Archive,item.Error));
        var identities = new HashSet<(StudioNativeFamily,Guid)>();
        foreach (var row in rows)
        {
            if (row.Id == Guid.Empty || row.SessionId != session ||
                !identities.Add((row.Family,row.Id)) ||
                string.IsNullOrWhiteSpace(row.Source) || string.IsNullOrWhiteSpace(row.State))
                throw new InvalidDataException("Malformed, foreign or duplicate historical record.");
        }
        return rows.OrderBy(x=>x.Family).ThenByDescending(x=>x.Sequence).ThenBy(x=>x.Id).ToArray();
    }
    internal static StudioRecordedFamilyRow[] Filter(IReadOnlyList<StudioRecordedFamilyRow> rows,
        int family, string? search) => Filter(rows,family,0,search);

    // Family and recorded-status filters combine, without promoting historical
    // Completed or Undone rows into present-time verified state.
    internal static StudioRecordedFamilyRow[] Filter(IReadOnlyList<StudioRecordedFamilyRow> rows,
        int family, int outcome, string? search)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (family is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(family));
        if (outcome is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(outcome));
        string term=search?.Trim() ?? "";
        return rows.Where(x=>(family==0 || (int)x.Family==family-1) &&
            (outcome==0 || (int)x.Outcome==outcome-1) &&
            (term.Length==0 || x.Family.ToString().Contains(term,StringComparison.OrdinalIgnoreCase) ||
             x.Source.Contains(term,StringComparison.OrdinalIgnoreCase) ||
             x.Destination.Contains(term,StringComparison.OrdinalIgnoreCase) ||
             x.Recovery.Contains(term,StringComparison.OrdinalIgnoreCase) ||
             (x.OutputUndoRecovery?.Contains(term,StringComparison.OrdinalIgnoreCase) ?? false) ||
             (x.Error?.Contains(term,StringComparison.OrdinalIgnoreCase) ?? false) ||
             x.Id.ToString().Contains(term,StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    internal static int SelectedIndex(IReadOnlyList<StudioRecordedFamilyRow> rows,
        StudioNativeFamily? family, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if(family is null || id is null) return -1;
        for(int i=0;i<rows.Count;i++)
            if(rows[i].Family==family.Value && rows[i].Id==id.Value) return i;
        return -1;
    }
    internal static string CountLabel(IReadOnlyList<StudioRecordedFamilyRow> all, int visible)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (visible<0 || visible>all.Count) throw new ArgumentOutOfRangeException(nameof(visible));
        return $"{all.Count} recorded files in this session · Showing {visible} · " +
            $"Copy {all.Count(x=>x.Family==StudioNativeFamily.Copy)} · " +
            $"Move {all.Count(x=>x.Family==StudioNativeFamily.Move)} · " +
            $"Rename {all.Count(x=>x.Family==StudioNativeFamily.Rename)} · " +
            $"Reject {all.Count(x=>x.Family==StudioNativeFamily.Reject)}. " +
            "Other sessions may also contain binding history; inspect recovery. Not checked now.";
    }
}
