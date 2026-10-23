using System;
using System.IO;

namespace PhotoSiftReborn;

// M6A presentation ticket ONLY. Passing this gate never prepares or approves
// native Rename. The existing coordinator, history and Apply own all authority.
internal static class StudioRenameReviewGate
{
    internal static void RequireFrozen(RenameProposal proposal, Guid session,
        long revision, Guid? photoId, string? originalPath)
    {
        if (proposal is null || session == Guid.Empty || photoId is null ||
            proposal.Id == Guid.Empty || proposal.SessionId != session ||
            proposal.PhotoId != photoId || proposal.PhotoId == Guid.Empty ||
            proposal.Revision != revision || string.IsNullOrWhiteSpace(originalPath) ||
            !SortingWorkspace.PathComparer.Equals(proposal.Source, originalPath) ||
            string.IsNullOrWhiteSpace(proposal.Target) ||
            SortingWorkspace.PathComparer.Equals(proposal.Source,proposal.Target) ||
            !SortingWorkspace.PathComparer.Equals(Path.GetDirectoryName(proposal.Source) ?? "",
                Path.GetDirectoryName(proposal.Target) ?? "") ||
            !string.Equals(Path.GetExtension(proposal.Source),Path.GetExtension(proposal.Target),
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(proposal.Hash) || proposal.Hash.Length != 64 ||
            string.IsNullOrWhiteSpace(proposal.NativeIdentity) || proposal.Length < 0)
            throw new InvalidDataException("The exact Rename proposal or selected original has changed. Review again.");
    }
}

// Independent from the Copy/Move review lease. Never reusable after End,
// never accepts a late callback, and never grants native Apply permission.
internal sealed class StudioRenameReviewLease
{
    private long _generation;
    private bool _active;
    private bool _decided;
    internal bool Active => _active;
    internal bool DecisionPending => _active && _decided;
    internal long Begin()
    {
        if (_active) throw new InvalidOperationException("A Rename review is already active.");
        _active = true;
        _decided = false;
        return ++_generation;
    }
    internal bool TryDecide(long ticket)
    {
        if (!_active || _decided || _generation != ticket) return false;
        _decided = true;
        return true;
    }
    internal void End(long ticket)
    {
        if (!_active || ticket != _generation)
            throw new InvalidOperationException("Rename review ownership changed.");
        _active = false;
        _decided = false;
        ++_generation;
    }
}
