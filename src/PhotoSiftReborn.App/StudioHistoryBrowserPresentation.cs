using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// M3: read-only and session-scoped journal projection. Nothing constructed here
// can reconcile the journal, approve a plan, authorise Undo or inspect bytes.
// "Checked now" is deliberately NOT inferred from durable historical state.
internal sealed record StudioRecordedCopyRow(Guid Id, Guid PlanId, long Sequence,
    string State, char Key, string Source, string Destination, string Recovery,
    string? Error)
{
    internal string Display
    {
        get
        {
            string file = Path.GetFileName(Source);
            string destination = Path.GetFileName(Destination);
            string state = State switch
            {
                "Committed" => "Completed · recorded",
                "Undone" => "Undone · recorded",
                _ => "Needs attention · " + State
            };
            return $"{file}  →  {destination}  ·  {state}";
        }
    }

    internal string ExactDetails =>
        "COPY · RECORDED HISTORY ONLY" + Environment.NewLine +
        "Historical state: " + State + Environment.NewLine +
        "Checked now: Not checked in this view. Use Inspect recovery journal for current evidence." + Environment.NewLine +
        "Operation ID: " + Id + Environment.NewLine +
        "Plan ID: " + PlanId + Environment.NewLine +
        "Recorded sequence: " + Sequence + Environment.NewLine +
        "Destination key: " + Key + Environment.NewLine +
        "Exact source: " + Source + Environment.NewLine +
        "Exact destination: " + Destination + Environment.NewLine +
        "Exact owned recovery: " + Recovery + Environment.NewLine +
        (string.IsNullOrEmpty(Error) ? string.Empty : "Recorded issue: " + Error + Environment.NewLine) +
        "Reviewing this record grants no Copy, Apply, Undo or recovery permission.";
}

internal static class StudioHistoryBrowserPresentation
{
    internal static StudioRecordedCopyRow[] Project(IReadOnlyList<DurableCopyItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // Copy into a display-only, finite snapshot. Preserve all rows; never
        // project just the six most recent as the complete History browser.
        return items.Reverse().Select(row => new StudioRecordedCopyRow(row.Id,
            row.PlanId, row.Sequence, row.State, row.Key, row.Source,
            row.Destination, row.Recovery, row.Error)).ToArray();
    }

    internal static StudioRecordedCopyRow[] Filter(IReadOnlyList<StudioRecordedCopyRow> rows,
        int stateFilter, string? search)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (stateFilter is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(stateFilter));
        string term = search?.Trim() ?? string.Empty;
        return rows.Where(row =>
            (stateFilter == 0 || stateFilter == 1 && row.State == "Committed" ||
             stateFilter == 2 && row.State == "Undone" ||
             stateFilter == 3 && row.State != "Committed" && row.State != "Undone") &&
            (term.Length == 0 || row.Source.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             row.Destination.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             row.Recovery.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             row.Id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
             row.PlanId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    internal static string SuggestedNextStep(IReadOnlyList<StudioRecordedCopyRow> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (all.Count == 0) return "No Copy files recorded yet. Review saved choices to begin a new operation.";
        if (all.Any(x => x.State != "Committed" && x.State != "Undone"))
            return "A recorded operation needs attention. Inspect recovery before any new file action; preserve originals, outputs and saved state.";
        if (all.Any(x => x.State == "Committed"))
            return "Copy recorded as completed. Check current output in History before considering Undo. Recorded status is not a fresh file check.";
        return "Copy Undo is recorded. Verify the owned archive in History. Sorting may require the existing verified continuation action.";
    }

    internal static string CountLabel(IReadOnlyList<StudioRecordedCopyRow> all,
        int visible)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (visible < 0 || visible > all.Count) throw new ArgumentOutOfRangeException(nameof(visible));
        int committed = all.Count(x => x.State == "Committed");
        int undone = all.Count(x => x.State == "Undone");
        int attention = all.Count - committed - undone;
        return $"{all.Count} recorded Copy {(all.Count == 1 ? "file" : "files")} · " +
            $"{committed} completed · {undone} undone · {attention} need attention · " +
            $"Showing {visible} of {all.Count}. Historical records, not current file proof.";
    }
}
