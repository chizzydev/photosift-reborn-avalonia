using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// M6H4: A presentation-only explanation of a SAVED journal row. No native
// coordinator, SessionStore, filesystem, serializer or grant is used here.
// Never confuse past operation semantics with file evidence observed now.
internal sealed record StudioHistoryInsight(string Heading, string RecordedMeaning,
    string SafeNextStep, string EvidenceBoundary)
{
    internal string Display => Heading + Environment.NewLine + RecordedMeaning +
        Environment.NewLine + "NEXT: " + SafeNextStep + Environment.NewLine + EvidenceBoundary;
}

internal static class StudioHistoryInsightPresentation
{
    internal const string NotVerified =
        "RECORDED ONLY - Current source, destination and recovery identities have NOT been checked in this view.";

    internal static StudioHistoryInsight For(StudioRecordedFamilyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Id == Guid.Empty || row.SessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(row.Source) || string.IsNullOrWhiteSpace(row.State))
            throw new InvalidDataException("Cannot display a malformed saved journal row as a healthy operation.");
        string familyEffect = row.Family switch
        {
            StudioNativeFamily.Copy => "Copy is designed to retain the source and create a separate destination. The recorded state does not verify either file today.",
            StudioNativeFamily.Move => "Move is designed to relocate a source and retain owned recovery evidence. Do not infer a current location from the journal.",
            StudioNativeFamily.Rename => "Rename changes a filename, not intended image bytes. Neither name nor native identity has been rechecked here.",
            StudioNativeFamily.Reject => "Reject relocates the ORIGINAL into owned recovery, never Trash. Staged and Approved are proposals, not relocation.",
            _ => throw new ArgumentOutOfRangeException(nameof(row),"Unsupported native family")
        };
        string outcome = row.Outcome switch
        {
            StudioRecordedOutcome.Completed => "COMPLETED (historical)",
            StudioRecordedOutcome.Undone => "UNDONE (historical)",
            StudioRecordedOutcome.Pending => "PENDING REJECT (historical)",
            StudioRecordedOutcome.NeedsAttention => "NEEDS ATTENTION (historical)",
            _ => throw new ArgumentOutOfRangeException(nameof(row),"Unsupported recorded outcome")
        };
        // Even Undone and Committed do not authorize a new native action.
        // The per-family, per-outcome next step is recorded guidance ONLY.
        // It never consumes a native grant or claims to have inspected paths.
        string next = StudioFamilyHistoryPresentation.NextStep(row);
        return new StudioHistoryInsight(row.Family + " / " + outcome,
            familyEffect, next, NotVerified);
    }

    // A read-only session inventory, never an authorization or a global
    // recovery overview. The four journals are scoped to the saved session.
    internal static string SessionBoundary(IReadOnlyList<StudioRecordedFamilyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return "No operations recorded in this session. Other sessions may hold binding recovery history; inspect recovery.";
        int pending = rows.Count(x => x.Outcome == StudioRecordedOutcome.Pending);
        int attention = rows.Count(x => x.Outcome == StudioRecordedOutcome.NeedsAttention);
        return "SESSION-SCOPED RECORDED HISTORY - " + rows.Count + " saved operation(s); " +
            attention + " need attention; " + pending + " pending Reject. " +
            "No current native identity, file bytes or archive ownership checked. Inspect recovery before any action.";
    }
}
