using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace PhotoSiftReborn;

// Data for human inspection ONLY. None of these records can prepare, approve,
// execute, undo, reconcile, save, or restore a native file operation.
internal sealed record StudioExactReviewRow(int Ordinal, char RouteKey, string Source,
    string Destination, long ExpectedLength, string? OriginalRecovery,
    string? OutputUndoRecovery)
{
    internal string Name => Path.GetFileName(Source);
    internal string FriendlyLine => $"{Ordinal}. {Name}  →  {RouteKey} / {Path.GetFileName(Path.GetDirectoryName(Destination))}/{Path.GetFileName(Destination)}";
}
internal sealed record StudioSkippedReviewRow(string Source, string AttemptedDestination, string Reason)
{
    internal string FriendlyLine => $"{Path.GetFileName(Source)} — {Reason}";
}
internal sealed record StudioExactReviewSnapshot(Guid SessionId, Guid? PlanId,
    long WorkspaceRevision, bool IsMove, CollisionChoice CollisionPolicy,
    ImmutableArray<StudioExactReviewRow> Included,
    ImmutableArray<StudioSkippedReviewRow> Skipped)
{
    // Capture native-reviewed *values*, including skipped membership, exactly once.
    // A displayed path is never reconstructed from these friendly labels.
    internal static StudioExactReviewSnapshot Create(Guid session, Guid? planId,
        long revision, bool move, CollisionChoice collision,
        IReadOnlyList<SortPlanItem> entries, IReadOnlyList<SkippedCollision> skipped,
        Guid? moveInstallationId = null, Guid? moveId = null)
    {
        if (entries is null || skipped is null) throw new ArgumentNullException("review items");
        if (entries.Count == 0 && skipped.Count == 0)
            throw new InvalidDataException("Review contains no included or skipped paths.");
        if (move && (entries.Count != 1 || skipped.Count != 0 ||
                     collision != CollisionChoice.Refuse || moveInstallationId is null || moveId is null))
            throw new InvalidDataException("Move review requires one exact native proposal, no skips and Refuse collisions.");
        var rows = ImmutableArray.CreateBuilder<StudioExactReviewRow>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Destination))
                throw new InvalidDataException("Exact review cannot represent an empty source or destination.");
            string? archive = null;
            string? undoArchive = null;
            if (move)
            {
                string parent = Path.GetDirectoryName(entry.Source) ??
                    throw new InvalidDataException("Move source parent is unavailable.");
                string recovery = Path.Combine(parent, ".photosift-reborn-recovery",
                    moveInstallationId!.Value.ToString("N"), session.ToString("N"), moveId!.Value.ToString("N"));
                archive = Path.Combine(recovery, Path.GetFileName(entry.Source));
                undoArchive = Path.Combine(recovery, "installed-" + Path.GetFileName(entry.Destination));
            }
            rows.Add(new StudioExactReviewRow(i + 1, entry.Key,
                entry.Source, entry.Destination, entry.ExpectedLength, archive, undoArchive));
        }
        var excluded = skipped.Select(s => new StudioSkippedReviewRow(
            s.Source, s.OriginalDestination, s.Reason)).ToImmutableArray();
        return new StudioExactReviewSnapshot(session, planId, revision, move, collision,
            rows.MoveToImmutable(), excluded);
    }

    internal string CopyAllExactDetails()
    {
        var sb = new StringBuilder();
        sb.AppendLine(IsMove ? "MOVE exact review (not approval)" : "COPY exact review (not approval)");
        sb.AppendLine($"Session: {SessionId}");
        sb.AppendLine($"Plan: {PlanId?.ToString() ?? "unavailable"} · Revision: {WorkspaceRevision}");
        sb.AppendLine($"Collision policy: {CollisionPolicy} · Included: {Included.Length} · Skipped: {Skipped.Length}");
        foreach (var row in Included)
        {
            sb.AppendLine($"#{row.Ordinal} [{row.RouteKey}]");
            sb.AppendLine("Exact source: " + row.Source);
            sb.AppendLine("Exact destination: " + row.Destination);
            if (row.OriginalRecovery is not null) sb.AppendLine("Retained original recovery: " + row.OriginalRecovery);
            if (row.OutputUndoRecovery is not null) sb.AppendLine("Output Undo recovery: " + row.OutputUndoRecovery);
        }
        foreach (var row in Skipped)
        {
            sb.AppendLine("Skipped source: " + row.Source);
            sb.AppendLine("Attempted destination: " + row.AttemptedDestination);
            sb.AppendLine("Skip reason: " + row.Reason);
        }
        return sb.ToString();
    }

    // Inform about non-printing/bidirectional Unicode without mutating the
    // separately selectable original native path value.
    internal static string DisplayWarning(string exactPath)
    {
        var warnings = new List<string>();
        foreach (char c in exactPath)
        {
            var cat = char.GetUnicodeCategory(c);
            if (char.IsControl(c) || cat is System.Globalization.UnicodeCategory.Format)
                warnings.Add($"U+{(int)c:X4}");
            if (warnings.Count == 6) break;
        }
        return warnings.Count == 0 ? string.Empty :
            "Caution: invisible or direction-control character(s): " + string.Join(", ", warnings) +
            ". The exact copy buttons preserve these characters.";
    }
}

// The App operation owner compares all fields after each await. A presenter
// cannot create a grant from a matching value; this is cancellation correlation.
internal sealed record StudioReviewAttempt(long Generation, Guid SessionId,
    Guid PlanId, long Revision, ReviewOperationMode Mode, CollisionChoice Collision)
{
    internal bool Matches(long generation, Guid session, Guid? planId,
        long revision, ReviewOperationMode mode, CollisionChoice collision) =>
        Generation == generation && SessionId == session && PlanId == planId &&
        Revision == revision && Mode == mode && Collision == collision;
}
