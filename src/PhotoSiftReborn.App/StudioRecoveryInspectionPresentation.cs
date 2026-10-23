using System;

namespace PhotoSiftReborn;

// M6H6: formatting ONLY. These values are supplied by existing independent
// journal inspections, not by a new reconciliation or filesystem authority.
// Zero current-session rows NEVER means globally safe, or that a different
// operation family's source/archive has been verified.
internal static class StudioRecoveryInspectionPresentation
{
    internal static string SessionHeading(int copies, int moves, int renames, int rejects)
    {
        if (copies < 0 || moves < 0 || renames < 0 || rejects < 0)
            throw new ArgumentOutOfRangeException(nameof(copies), "Negative recovery inventory is not valid evidence.");
        return $"RECORDED JOURNALS IN THIS SESSION: Copy {copies} | Move {moves} | Rename {renames} | Reject {rejects}." +
            Environment.NewLine +
            "Session counts are historical only. Independent evidence sections below describe observed files; " +
            "other sessions may still hold binding recovery history.";
    }

    internal static string CopyEvidence(int archived, int verified, int needsAttention)
    {
        if (archived < 0 || verified < 0 || verified > archived || needsAttention < 0)
            throw new ArgumentOutOfRangeException(nameof(verified), "Invalid Copy archive evidence totals.");
        return $"COPY ARCHIVE EVIDENCE NOW: {verified}/{archived} VERIFIED, " +
            $"{archived - verified} ARCHIVE ALERT(S); COPY JOURNAL NEED ATTENTION: {needsAttention}.";
    }

    internal static string NextStep(StudioNativeFamily family, string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return "UNKNOWN HISTORICAL STATE. Preserve journals and files; no new operation or retry.";
        if (state == "Undone") return family switch
        {
            StudioNativeFamily.Move => "Move Undo is recorded already. Inspect restored source and retained output archive; never repeat Undo automatically. All existing locks remain.",
            StudioNativeFamily.Rename => "Rename Undo is recorded already. Inspect restored original name and native identity; separately guarded closeout is not automatic.",
            StudioNativeFamily.Reject => "Reject Undo is recorded already. Inspect restored original and owned journal; saved Reject-review marks are separate.",
            StudioNativeFamily.Copy => "Copy Undo is recorded already. Inspect source and owned output archive; do not replay, restore or clean up automatically.",
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        return family switch
        {
            StudioNativeFamily.Move => "Move history blocks new file operations. Only existing independently guarded native recovery controls may be considered after verification; no automatic replay.",
            StudioNativeFamily.Rename => "Rename history blocks new file operations until existing verified recovery/closeout gates are met. No automatic Undo or closeout.",
            StudioNativeFamily.Reject => "Reject history is durable, including proposals. Use the existing verified owned-recovery controls only; never Trash or automatic relocation.",
            StudioNativeFamily.Copy => "Copy history is not a grant. Inspect the current original, output and archive before independently considering existing guarded Undo.",
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
    }
}
