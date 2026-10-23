using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoSiftReborn;

// M6H5: saved JOURNAL projection only. No SessionStore, native coordinator,
// filesystem IO, recovery scan, writable capability, or cross-family replay.
// A recorded Undo is never a current proof that source/archive bytes remain.
internal static class StudioHistoryExperiencePresentation
{
    internal const string EvidenceBoundary =
        "Saved operation history is session-scoped and NOT live file evidence. " +
        "Use Inspect recovery journal for a separate current-evidence check; " +
        "that inspection never grants Apply, Undo, replay or history reset.";

    internal static string Summary(IReadOnlyList<StudioRecordedFamilyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return "No recorded file operations in this session. Other sessions may hold " +
                   "binding recovery history. Inspect recovery before acting.";
        int copies=rows.Count(x=>x.Family==StudioNativeFamily.Copy);
        int moves=rows.Count(x=>x.Family==StudioNativeFamily.Move);
        int renames=rows.Count(x=>x.Family==StudioNativeFamily.Rename);
        int rejects=rows.Count(x=>x.Family==StudioNativeFamily.Reject);
        int attention=rows.Count(x=>x.Outcome==StudioRecordedOutcome.NeedsAttention);
        int pending=rows.Count(x=>x.Outcome==StudioRecordedOutcome.Pending);
        if (copies+moves+renames+rejects!=rows.Count)
            throw new InvalidOperationException("Unsupported recorded operation family.");
        return $"{rows.Count} recorded in this session  |  Copy {copies}  |  Move {moves}  |  " +
               $"Rename {renames}  |  Reject {rejects}.  " +
               (attention>0 ? $"{attention} need attention. Preserve saved state and inspect recovery." :
                pending>0 ? $"{pending} pending Reject proposal(s); not yet executed." :
                "Recorded outcomes only - not current file verification.");
    }

    internal static bool ShowCopyDiagnostics(IReadOnlyList<StudioRecordedFamilyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Any(x=>x.Family==StudioNativeFamily.Copy);
    }

    internal static string ChangesDecisionBoundary =>
        "Saved destination choices and Reject-review marks are NOT disk actions. " +
        "Historical Undo does not automatically clear a saved choice or lift a recovery lock.";

    // Existing Rename closeout authority remains in its original guarded
    // handler. This startup cue never asserts that all Renames are committed
    // or promises that a historical Undo is sufficient for closeout.
    internal const string RenameStartupLocked =
        "Rename history retained. Recorded Undo may already be complete, " +
        "but is not current verification. Inspect recovery before the existing " +
        "verified Rename closeout. New file operations remain locked.";
}
