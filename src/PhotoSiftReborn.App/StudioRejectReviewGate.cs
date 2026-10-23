using System;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// An immutable presentation-only phase tag. NOT a native approval or consent grant.
internal enum StudioRejectPhase { ApproveStaged, ExecuteApproved, RestoreCommitted }

internal static class StudioRejectReviewGate
{
    internal static string ExpectedState(StudioRejectPhase phase) => phase switch
    {
        StudioRejectPhase.ApproveStaged => "Staged",
        StudioRejectPhase.ExecuteApproved => "Approved",
        StudioRejectPhase.RestoreCommitted => "Committed",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    internal static string ExpectedEvidence(StudioRejectPhase phase) => phase switch
    {
        StudioRejectPhase.ApproveStaged or StudioRejectPhase.ExecuteApproved => "UNTOUCHED_SOURCE_VERIFIED",
        StudioRejectPhase.RestoreCommitted => "OWNED_ARCHIVE_VERIFIED",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    internal static void RequireExact(DurableRejectItem row, StudioRejectPhase phase, Guid session,
        Guid installation, Guid photoId)
    {
        if (row is null || session == Guid.Empty || installation == Guid.Empty || photoId == Guid.Empty ||
            row.Id == Guid.Empty || row.SessionId != session || row.InstallationId != installation ||
            row.PhotoId != photoId || row.SessionRevision < 0 ||
            row.State != ExpectedState(phase) || !string.IsNullOrEmpty(row.Error) ||
            string.IsNullOrWhiteSpace(row.Source) || string.IsNullOrWhiteSpace(row.Archive) ||
            SortingWorkspace.PathComparer.Equals(row.Source,row.Archive) ||
            !Path.IsPathFullyQualified(row.Source) || !Path.IsPathFullyQualified(row.Archive) ||
            row.SourceHash is null || row.SourceHash.Length != 64 ||
            !row.SourceHash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(row.SourceIdentity) ||
            row.SourceLength < 1 || row.SourceWriteTicks <= 0)
            throw new InvalidDataException("Exact durable Reject stage, original or owned recovery identity was not proved.");
    }
}

// Distinct from Copy/Move and Rename. Phase + generation must both match.
// Ending a lease invalidates even late callbacks for the same underlying row.
internal sealed class StudioRejectReviewLease
{
    private long _generation;
    private bool _active;
    private bool _decided;
    private StudioRejectPhase _phase;
    internal bool Active => _active;
    internal bool DecisionPending => _active && _decided;
    internal StudioRejectPhase? Phase => _active ? _phase : null;
    internal long Begin(StudioRejectPhase phase)
    {
        _ = StudioRejectReviewGate.ExpectedState(phase);
        if (_active) throw new InvalidOperationException("An ordinary Reject review is already active.");
        _active=true; _decided=false; _phase=phase;
        return ++_generation;
    }
    internal bool TryDecide(long ticket, StudioRejectPhase phase)
    {
        if (!_active || _decided || ticket!=_generation || phase!=_phase)return false;
        _decided=true;return true;
    }
    internal void End(long ticket)
    {
        if (!_active || _generation!=ticket)
            throw new InvalidOperationException("Reject review lease owner changed.");
        _active=false;_decided=false;++_generation;
    }
}
