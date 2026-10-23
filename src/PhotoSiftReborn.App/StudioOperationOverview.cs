using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoSiftReborn;

// M6H2: recorded, session-scoped summary ONLY. This class never reads a live
// filesystem path, issues approval, checks archived evidence, or changes files.
internal static class StudioOperationOverview
{
    internal static string Summary(IReadOnlyList<StudioRecordedFamilyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int completed=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Completed);
        int undone=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Undone);
        int pending=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Pending);
        int attention=rows.Count(x=>x.Outcome==StudioRecordedOutcome.NeedsAttention);
        return rows.Count==0
            ? "No file operations recorded in this session. Earlier recovery history may still apply."
            : $"{rows.Count} recorded operation(s) · {completed} completed · {undone} undone · {pending} pending · {attention} need attention.";
    }

    internal static string[] Recent(IReadOnlyList<StudioRecordedFamilyRow> rows, int maxRows = 4)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if(maxRows is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maxRows));
        return rows.OrderByDescending(x => x.Outcome==StudioRecordedOutcome.NeedsAttention)
            .ThenByDescending(x => x.Outcome==StudioRecordedOutcome.Pending)
            .ThenByDescending(x => x.Sequence)
            .ThenBy(x => x.Id)
            .Take(maxRows)
            .Select(x => x.Display)
            .ToArray();
    }

    internal const string EvidenceBoundary =
        "Recorded status only, not current-file verification. Inspect recovery before any Undo or new operation.";
}
