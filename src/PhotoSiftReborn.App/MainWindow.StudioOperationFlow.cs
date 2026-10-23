namespace PhotoSiftReborn;

// MainWindow remains the ONLY command/approval owner. The attempt is merely an
// immutable identity ticket; it cannot confer or reconstruct native authority.
public sealed partial class MainWindow
{
    private long _studioReviewGeneration;

    private StudioReviewAttempt BeginStudioReview(Guid planId, long revision,
        ReviewOperationMode mode, CollisionChoice collision)
    {
        ++_studioReviewGeneration;
        return new StudioReviewAttempt(_studioReviewGeneration, _work.SessionId,
            planId, revision, mode, collision);
    }

    private void RequireStudioReviewCurrent(StudioReviewAttempt attempt)
    {
        if (!attempt.Matches(_studioReviewGeneration, _work.SessionId,
            _work.CurrentPlanId, _work.Revision, _modeGate.Selected, _work.CollisionMode))
            throw new InvalidOperationException(
                "The reviewed photo choices or operation changed. Review the exact paths again.");
    }

    private void EndStudioReview(StudioReviewAttempt attempt)
    {
        if (attempt.Generation == _studioReviewGeneration)
            ++_studioReviewGeneration;
    }

    private StudioExactReviewSnapshot CaptureStudioExactReview(
        IReadOnlyList<SortPlanItem> entries, IReadOnlyList<SkippedCollision> skips,
        bool move, Guid? moveId, Guid? planId, long revision,
        Guid sessionId, CollisionChoice collision)
    {
        Guid? installation = move
            ? _sessionStore?.MoveInstallationId() ?? throw new IOException(
                "Move installation identity is unavailable for exact review.")
            : null;
        return StudioExactReviewSnapshot.Create(sessionId,planId,revision,move,
            collision,entries,skips,installation,moveId);
    }
}
