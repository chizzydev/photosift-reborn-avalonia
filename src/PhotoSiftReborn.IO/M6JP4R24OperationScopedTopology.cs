using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoSiftReborn;

/// <summary>
/// P4-R24 is an OPERATION-SCOPED READ-ONLY classification over an exact set of
/// historical footprints.  Even a fully disjoint/verified observation is NOT
/// an approval, a successor grant, a durable snapshot, or filesystem authority.
/// A future actual multi-row writer needs a separately reviewed operation ID,
/// a consumed per-row grant, complete installation history and fresh native
/// pinning inside a transaction plus just before each physical effect.
/// </summary>
public sealed record M6JP4R24ScopedTopologyObservation(
    string TargetFamily, string TargetJournalId, bool TargetFound,
    int ExaminedRows, bool HistoryFullyVerified, bool NoRecordedTargetEdges,
    int TargetEdgeCount, int OtherEdgeCount, M6JDependencyKind HistoryKind,
    string Reason)
{
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool GrantsSuccessorEpoch => false;
    public bool AuthorizesOrdinaryMixedFamily => false;
    public bool IsDurableProof => false;
    public bool IsReadOnlyDiagnostic => true;
}

public static class M6JP4R24OperationScopedTopology
{
    // Pure bounded decision model: do NOT invoke from any Commit/Undo/Intent
    // writer as an alternative to that writer's exact-native consent checks.
    public static M6JP4R24ScopedTopologyObservation Inspect(
        IReadOnlyList<M6JOperationFootprint>? history,
        string family, string journalId, bool completeObservation = true)
    {
        static M6JP4R24ScopedTopologyObservation Refuse(
            string family, string key, int count, string reason) =>
            new(family,key,false,count,false,false,0,0,
                M6JDependencyKind.IncompleteOrUnverified,reason);
        if (history is null || history.Count == 0 || history.Count > 512 ||
            family is not ("Copy" or "Move" or "Rename" or "Reject") ||
            !Guid.TryParseExact(journalId,"N",out var requested) || requested == Guid.Empty)
            return Refuse(family,journalId,history?.Count ?? 0,
                "Missing, invalid, or over-limit operation/history. No native permission.");
        var report=M6JDependencyInspector.Analyze(history,completeObservation,
            "Some historical native ownership/consent evidence is incomplete.");
        if (report.Kind == M6JDependencyKind.IncompleteOrUnverified ||
            report.Kind == M6JDependencyKind.EmptyObservation ||
            report.ExaminedRows != history.Count)
            return Refuse(family,journalId,history.Count,
                "Incomplete inventory, historical aliases, malformed rows or unresolved states.");
        // A target is always an exact family+journal key, never a session-wide
        // latest row, lexical path match or photo-id lookup.
        var target=history.Where(x => x.Family == family && x.JournalId == journalId).ToArray();
        if (target.Length != 1)
            return Refuse(family,journalId,history.Count,
                "Exact requested operation row is absent or ambiguous.");
        int related=report.Edges.Count(e =>
            (e.FirstFamily==family && e.FirstJournalId==journalId) ||
            (e.SecondFamily==family && e.SecondJournalId==journalId));
        // All edges elsewhere must remain visible; no 'isolated target' result
        // is allowed to suppress unrelated, possibly blocking predecessor work.
        int unrelated=report.Edges.Count-related;
        return new(family,journalId,true,history.Count,true,related==0,
            related,unrelated,report.Kind,
            "Time-local historical footprint classification only. Per-row consent, " +
            "current native handles, auxiliary history, and exact CAS are NOT proven.");
    }
}
