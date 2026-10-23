namespace PhotoSiftReborn;

// Operation UI exclusion only: never a native authorization, never persisted.
// Exactly one active Changes Review. Its decision is single-use and the caller
// must still prove the frozen plan before it can grant its separate Apply.
internal sealed class StudioEmbeddedReviewLease
{
    private long _generation;
    private bool _active;
    private bool _decided;
    private ReviewOperationMode _family;
    internal bool Active => _active;
    internal ReviewOperationMode? ActiveFamily => _active ? _family : null;
    internal bool DecisionPending => _active && _decided;
    // The legacy no-argument form is Copy only, for cumulative M2 tests.
    internal long Begin(ReviewOperationMode family = ReviewOperationMode.Copy)
    {
        if (_active) throw new InvalidOperationException("A Studio Review is already in progress.");
        if (family is not (ReviewOperationMode.Copy or ReviewOperationMode.Move))
            throw new ArgumentOutOfRangeException(nameof(family));
        _family = family;
        _active = true;
        _decided = false;
        return ++_generation;
    }
    // Captured-family decision: a delayed callback from another family is not
    // an approval, even when a view has been destroyed or rebuilt.
    internal bool TryDecide(long generation, ReviewOperationMode family = ReviewOperationMode.Copy)
    {
        if (!_active || _decided || generation != _generation || family != _family) return false;
        _decided = true;
        return true;
    }
    internal void End(long generation)
    {
        if (!_active || generation != _generation)
            throw new InvalidOperationException("Studio Review ownership changed; close the operation lane safely.");
        _active = false;
        _decided = false;
        _family = ReviewOperationMode.Copy;
        ++_generation;
    }
}
