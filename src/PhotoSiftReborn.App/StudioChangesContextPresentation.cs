using System;

namespace PhotoSiftReborn;

// Display text ONLY. The native handlers' existing authorization predicates
// remain authoritative. Never use this presentation output as an edit gate.
internal static class StudioChangesContextPresentation
{
    internal static string Explain(bool healthy, bool moveMode, bool moveHistory,
        bool renameLocked, bool rejectHistory, bool copyBarrier,
        bool journalAttention, bool copyHistoryLocked)
    {
        if (!healthy)
            return "RECOVERY UNAVAILABLE — preserve saved operations, photographs and SQLite; all file actions remain blocked.";
        int familyLocks = (moveHistory ? 1 : 0) + (renameLocked ? 1 : 0) +
            (rejectHistory ? 1 : 0) + (copyHistoryLocked ? 1 : 0);
        if (familyLocks > 1)
            return "MULTIPLE FILE-OPERATION HISTORY LOCKS — inspect each independent journal. " +
                   "Do not mix families, reset history or infer permission from an Undone label.";
        if (copyBarrier || journalAttention)
            return "RECOVERY ATTENTION — file actions remain blocked. Inspect exact current evidence; " +
                   "do not replay or clear recorded history.";
        if (rejectHistory)
            return "REJECT HISTORY LOCK — originals use app-owned recovery, never Trash. " +
                   "Saved Reject marks are not file operations. Inspect the existing native journal; no auto replay.";
        if (moveHistory)
            return "MOVE HISTORY LOCK — an Undo may already be recorded, but it is not current verification. " +
                   "Inspect restored source and owned archives; no automatic continuation.";
        if (renameLocked)
            return "RENAME HISTORY LOCK — recorded Undo is not current proof. " +
                   "Check the original name and native identity; verified closeout is separate.";
        if (copyHistoryLocked)
            return "COPY HISTORY LOCK — recorded Undo is not current archive verification. " +
                   "Inspect source and owned outputs before existing guarded continuation.";
        return moveMode
            ? "MOVE selected — a source is relocated only after explicit path review and approval."
            : "COPY selected — originals remain in place. Review exact paths before applying.";
    }
}
