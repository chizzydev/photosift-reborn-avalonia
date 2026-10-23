using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace PhotoSiftReborn;

// M4B: one contained Copy OR native Move exact-path Review within Changes.
// MainWindow is still the sole native operation/approval owner. This view host
// only collects one yes/no request. It never prepares, applies, or saves files.
public sealed partial class MainWindow
{
    private readonly StudioEmbeddedReviewLease _studioEmbeddedLease = new();
    private long _studioEmbeddedTicket;
    private TaskCompletionSource<bool>? _studioEmbeddedDecision;
    private StudioExactReviewView? _studioEmbeddedView;
    private Grid? _studioNormalChanges;
    private Border? _studioReviewSurface;
    private ContentControl? _studioReviewContent;
    private TextBlock? _studioReviewPendingMessage;
    private WrapPanel? _studioNavigationHeader;

    private async Task<bool> ShowStudioEmbeddedFamilyReviewAsync(
        StudioExactReviewSnapshot frozen, bool generatedFixtureMove)
    {
        if (_studioActivePage != StudioPage.Changes || !_busy ||
            _studioRejectLease.Active ||
            _studioNormalChanges is null || _studioReviewSurface is null ||
            _studioReviewContent is null || _studioNavigationHeader is null)
            throw new InvalidOperationException("An exact Copy Review needs the active, exclusively owned Changes workspace.");
        if (frozen.SessionId != _work.SessionId || frozen.PlanId != _work.CurrentPlanId ||
            frozen.WorkspaceRevision != _work.Revision || frozen.CollisionPolicy != _work.CollisionMode)
            throw new InvalidOperationException("The exact Review snapshot is no longer current.");

        // The immutable display snapshot is not a grant. Native proof and
        // independent Apply remain owned by MainWindow.ReviewAsync/ApplyAsync.
        var family = frozen.IsMove ? ReviewOperationMode.Move : ReviewOperationMode.Copy;
        StudioEmbeddedFamilyPolicy.RequireContained(frozen, family, generatedFixtureMove);
        var ticket = _studioEmbeddedLease.Begin(family);
        _studioEmbeddedTicket = ticket;
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _studioEmbeddedDecision = decision;
        var view = new StudioExactReviewView(frozen, generatedFixtureMove);
        _studioEmbeddedView = view;
        _studioReviewContent.Content = view;
        _studioNormalChanges.IsEnabled = false;
        _studioNormalChanges.IsVisible = false;
        _studioNavigationHeader.IsEnabled = false;
        _studioReviewSurface.IsVisible = true;
        _studioReviewPendingMessage!.Text = "Finish or cancel this review before changing pages, photos or settings.";
        _heldKeys.Clear();
        view.ApproveRequested += (_,_) => FinishStudioEmbeddedDecision(ticket,family,true);
        view.DeclineRequested += (_,_) => FinishStudioEmbeddedDecision(ticket,family,false);
        Dispatcher.UIThread.Post(() =>
        {
            if (_studioEmbeddedLease.Active && _studioEmbeddedTicket == ticket &&
                !_studioEmbeddedLease.DecisionPending) view.FocusCancel();
        }, DispatcherPriority.Input);
        // The operation owner retains its UI lease through the post-approval
        // native proof in ReviewAsync. Only ReviewAsync.finally may release it.
        return await decision.Task;
    }

    private void FinishStudioEmbeddedDecision(long ticket, ReviewOperationMode family, bool approved)
    {
        if (!_studioEmbeddedLease.TryDecide(ticket,family)) return; // no double click, late Escape or stale callback
        if (_studioEmbeddedView is not null) _studioEmbeddedView.IsEnabled = false;
        if (_studioReviewPendingMessage is not null)
            _studioReviewPendingMessage.Text = approved
                ? "Checking these exact paths again before recording approval. No files are changed."
                : "Cancelling Review. No Apply permission will be granted.";
        _studioEmbeddedDecision?.TrySetResult(approved);
    }

    private void CancelStudioEmbeddedReview()
    {
        if (_studioEmbeddedLease.Active && !_studioEmbeddedLease.DecisionPending)
            FinishStudioEmbeddedDecision(_studioEmbeddedTicket,
                _studioEmbeddedLease.ActiveFamily!.Value,false);
    }

    private void EndStudioEmbeddedReview()
    {
        if (!_studioEmbeddedLease.Active) return;
        // Never re-enable background commands until all native rechecks and
        // approval bookkeeping have completed or failed.
        _studioEmbeddedLease.End(_studioEmbeddedTicket);
        if (_studioReviewContent is not null) _studioReviewContent.Content = null;
        if (_studioReviewSurface is not null) _studioReviewSurface.IsVisible = false;
        if (_studioNormalChanges is not null)
        {
            _studioNormalChanges.IsVisible = true;
            _studioNormalChanges.IsEnabled = true;
        }
        if (_studioNavigationHeader is not null) _studioNavigationHeader.IsEnabled = true;
        _studioEmbeddedView = null;
        _studioEmbeddedDecision = null;
        _heldKeys.Clear();
        // When Review exits with approved proof, the next deliberate click is
        // Apply; no Enter cascade or hidden focus on an Apply button.
        Dispatcher.UIThread.Post(() => { if (!_studioEmbeddedLease.Active) _review.Focus(); },
            DispatcherPriority.Input);
    }

    // Registered ONCE at Window construction. Tunnel so Escape works even
    // when a selectable read-only path box has focus. No Focus shortcuts leak.
    private void OnStudioEmbeddedPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_studioEmbeddedLease.Active && !_studioRenameLease.Active && !_studioRejectLease.Active) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_studioRejectLease.Active) CancelStudioRejectReview();
            else if (_studioRenameLease.Active) CancelStudioRenameReview();
            else CancelStudioEmbeddedReview();
            return;
        }
        // The review's own Tab cycle, selection arrows, Enter/Space and
        // read-only selection/copy remain usable. Other modifier shortcuts
        // cannot invoke a background command while this lease exists.
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0 &&
            !(e.KeyModifiers == KeyModifiers.Control && (e.Key == Key.C || e.Key == Key.A)))
            e.Handled = true;
    }
}
