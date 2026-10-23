using System;
using System.IO;

namespace PhotoSiftReborn;

// Read-only family/snapshot compatibility check, never a native grant.
// The owning ReviewAsync still captures/compares plan+revision, prepares
// again after explicit consent, and requires a separate Apply action.
internal static class StudioEmbeddedFamilyPolicy
{
    internal static void RequireContained(StudioExactReviewSnapshot snapshot,
        ReviewOperationMode family, bool generatedFixtureMove)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (family is not (ReviewOperationMode.Copy or ReviewOperationMode.Move))
            throw new InvalidDataException("Unsupported embedded operation family.");
        if (snapshot.SessionId == Guid.Empty || !snapshot.PlanId.HasValue || snapshot.PlanId.Value == Guid.Empty ||
            snapshot.WorkspaceRevision < 0)
            throw new InvalidDataException("Missing frozen Studio review identity.");
        if (snapshot.IsMove != (family == ReviewOperationMode.Move))
            throw new InvalidDataException("The displayed operation family changed.");
        if (snapshot.Included.IsDefault || snapshot.Skipped.IsDefault || snapshot.Included.Length == 0)
            throw new InvalidDataException("A review cannot approve zero included photographs.");
        if (snapshot.IsMove && (snapshot.Included.Length != 1 || snapshot.Skipped.Length != 0 ||
            snapshot.CollisionPolicy != CollisionChoice.Refuse ||
            snapshot.Included[0].OriginalRecovery is null ||
            snapshot.Included[0].OutputUndoRecovery is null))
            throw new InvalidDataException("Move requires exactly one source, no skips, Refuse collisions and recovery paths.");
        if (!snapshot.IsMove && generatedFixtureMove)
            throw new InvalidDataException("Copy cannot inherit a generated Move capability.");
    }
}
