using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// M6J-A1 is a diagnostic footprint/interaction model. It must never be
// consulted as a replacement for durable journal-before-write, native
// identity checks, original coordinator ownership or one-use consent.
// Even a fully proven, lexically disjoint set is NOT mutation authority.
public enum M6JDependencyKind
{
    EmptyObservation,
    IncompleteOrUnverified,
    LexicallyDisjointNotAuthorized,
    DependencyDetected
}

public enum M6JConflictKind
{
    SamePhoto,
    SameRecordedPath,
    RecoveryRootContainsPath,
    DuplicateOperationId,
    SameRecordedNativeIdentity,
    PathAncestorOverlap
}

// A historical journal identity is evidence of a possible alias, NOT proof of
// what currently exists at a path. A missing value is never affirmative proof.
public sealed record M6JPathClaim(string Role, string Path, bool IsRecoveryRoot = false,
    string? RecordedNativeIdentity = null);

public sealed record M6JOperationFootprint(
    string Family, string JournalId, string SessionId, Guid PhotoId,
    string State, bool NativeEvidenceVerified, IReadOnlyList<M6JPathClaim> Claims);

public sealed record M6JDependencyEdge(
    string FirstFamily, string FirstJournalId,
    string SecondFamily, string SecondJournalId,
    M6JConflictKind Kind, string Explanation);

public sealed record M6JDependencyAssessment(
    M6JDependencyKind Kind, int ExaminedRows,
    IReadOnlyList<M6JDependencyEdge> Edges, string Explanation)
{
    public bool AuthorizesNativeOperation => false;
    public bool GrantsSuccessorEpoch => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool IsReadOnlyDiagnostic => true;
}

public static class M6JDependencyInspector
{
    private static readonly string[] Families = { "Copy", "Move", "Rename", "Reject" };

    public static M6JDependencyAssessment Refuse(string explanation) =>
        new(M6JDependencyKind.IncompleteOrUnverified, 0,
            Array.Empty<M6JDependencyEdge>(), explanation);

    // Lexical overlap is a necessary *warning*, not a sufficient test of
    // independence: junctions, reparse points, hard links, mounts, aliases,
    // ownership marker tampering and concurrent writes require native proofs.
    public static M6JDependencyAssessment Analyze(
        IReadOnlyList<M6JOperationFootprint> footprints,
        bool observationComplete = true,
        string observationExplanation = "Read-only footprint comparison only")
    {
        if (footprints is null || footprints.Count > 512)
            return Refuse("Missing or over-limit history; preserve native operation locks.");
        var normalized = new List<(M6JOperationFootprint Row,
            IReadOnlyList<(M6JPathClaim Claim, string Canonical)> Claims)>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        bool hasWeakProof = !observationComplete;
        foreach (var row in footprints)
        {
            if (row is null || !Families.Contains(row.Family, StringComparer.Ordinal) ||
                !Guid.TryParseExact(row.JournalId, "N", out var operationId) ||
                operationId == Guid.Empty ||
                !Guid.TryParseExact(row.SessionId, "N", out var sessionId) ||
                sessionId == Guid.Empty || row.PhotoId == Guid.Empty ||
                row.Claims is null || row.Claims.Count < 2 || row.Claims.Count > 8)
                return Refuse("Malformed journal footprint or identity; preserve all history.");
            if (!unique.Add(row.Family + ":" + row.JournalId))
                return Refuse("Duplicate journal key; do not infer a successor.");
            if (!row.NativeEvidenceVerified ||
                (row.State != "Undone" && row.State != "Committed"))
                hasWeakProof = true;
            var paths = new List<(M6JPathClaim Claim, string Canonical)>();
            foreach (var claim in row.Claims)
            {
                if (claim is null || string.IsNullOrWhiteSpace(claim.Role) ||
                    string.IsNullOrWhiteSpace(claim.Path) || claim.Path.IndexOf('\0') >= 0 ||
                    !Path.IsPathFullyQualified(claim.Path) ||
                    (claim.RecordedNativeIdentity is { } identity &&
                        (identity.Length > 256 || identity.IndexOf('\0') >= 0)))
                    return Refuse("Malformed or nonabsolute historical path; preserve locks.");
                string canonical;
                try { canonical = Path.GetFullPath(claim.Path); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return Refuse("Historical path could not be normalized.");
                }
                if (!SortingWorkspace.PathComparer.Equals(canonical, claim.Path))
                    hasWeakProof = true; // dot-segments and non-canonical aliases are not a proof.
                paths.Add((claim, canonical));
            }
            normalized.Add((row, paths));
        }
        if (footprints.Count == 0)
            return observationComplete
                ? new(M6JDependencyKind.EmptyObservation, 0,
                    Array.Empty<M6JDependencyEdge>(),
                    "No rows observed; freshness is not an operation grant.")
                : Refuse("Incomplete empty observation cannot establish freshness.");

        var edges = new List<M6JDependencyEdge>();
        for (int i=0; i<normalized.Count; i++)
        for (int j=i+1; j<normalized.Count; j++)
        {
            var a=normalized[i];var b=normalized[j];
            void Add(M6JConflictKind kind, string reason) =>
                edges.Add(new(a.Row.Family,a.Row.JournalId,
                    b.Row.Family,b.Row.JournalId,kind,reason));
            if (a.Row.PhotoId == b.Row.PhotoId)
                Add(M6JConflictKind.SamePhoto,
                    "Same saved photo identity occurs in multiple operation journals.");
            if (a.Row.JournalId == b.Row.JournalId)
                Add(M6JConflictKind.DuplicateOperationId,
                    "Operation GUID appears in multiple families.");
            bool samePath=false, recoveryOverlap=false, ancestorOverlap=false,
                sameRecordedNativeIdentity=false;
            foreach (var first in a.Claims)
            foreach (var second in b.Claims)
            {
                if (SortingWorkspace.PathComparer.Equals(first.Canonical, second.Canonical))
                    samePath=true;
                bool firstContainsSecond=Inside(second.Canonical,first.Canonical);
                bool secondContainsFirst=Inside(first.Canonical,second.Canonical);
                if ((first.Claim.IsRecoveryRoot && firstContainsSecond) ||
                    (second.Claim.IsRecoveryRoot && secondContainsFirst))
                    recoveryOverlap=true;
                // OutputRoot is a routing container shared by unrelated
                // operations; its lexical containment alone is NOT a leaf
                // ownership conflict. Owned recovery roots remain special.
                else if (first.Claim.Role != "OutputRoot" &&
                    second.Claim.Role != "OutputRoot" &&
                    (firstContainsSecond || secondContainsFirst))
                    ancestorOverlap=true;
                // Historical native identities can prove a recorded correlation,
                // but cannot prove today's identity or authorize future changes.
                if (!string.IsNullOrEmpty(first.Claim.RecordedNativeIdentity) &&
                    string.Equals(first.Claim.RecordedNativeIdentity,
                        second.Claim.RecordedNativeIdentity,StringComparison.Ordinal))
                    sameRecordedNativeIdentity=true;
            }
            if (samePath) Add(M6JConflictKind.SameRecordedPath,
                "Distinct journals refer to the same recorded source/output/temp/archive path.");
            if (recoveryOverlap) Add(M6JConflictKind.RecoveryRootContainsPath,
                "A recorded path is under the other operation's owned recovery root.");
            if (ancestorOverlap) Add(M6JConflictKind.PathAncestorOverlap,
                "One recorded claim contains the other operation's path. Recorded hierarchy is not independent native ownership.");
            if (sameRecordedNativeIdentity) Add(M6JConflictKind.SameRecordedNativeIdentity,
                "Two journals reference the same SAVED native file identity despite possibly distinct paths. Historical correlation only; verify current ownership separately.");
            if (edges.Count>2048)
                return Refuse("Too many potential dependency edges for bounded inspection; preserve all locks.");
        }
        if (hasWeakProof)
            return new(M6JDependencyKind.IncompleteOrUnverified, footprints.Count,
                edges.AsReadOnly(),
                observationExplanation+"; some rows/state/path/proof are incomplete. No operation authority.");
        if (edges.Count > 0)
            return new(M6JDependencyKind.DependencyDetected, footprints.Count,
                edges.AsReadOnly(),
                "Recorded identity/path/hierarchy correlations exist; historical identity is not a current native proof. Selective Undo is NOT authorized.");
        return new(M6JDependencyKind.LexicallyDisjointNotAuthorized, footprints.Count,
            edges.AsReadOnly(),
            "No recorded lexical or saved native-identity intersections found, but unrecorded aliases, historical receipts and crash ordering " +
            "have NOT been proven. No operation or selective Undo authority.");
    }

    private static bool Inside(string child, string root)
    {
        string prefix = Path.TrimEndingDirectorySeparator(root);
        if (SortingWorkspace.PathComparer.Equals(child,prefix)) return false;
        if (Path.EndsInDirectorySeparator(prefix))
            return child.StartsWith(prefix, SortingWorkspace.PathComparer.Equals("A","a")
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        return child.StartsWith(prefix + Path.DirectorySeparatorChar,
            SortingWorkspace.PathComparer.Equals("A","a")
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
