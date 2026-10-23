using Avalonia.Controls;
using Avalonia.Threading;

namespace PhotoSiftReborn;

// Separate contained Rename REVIEW only. No coordinator calls or filesystem
// changes occur here. Native approval and Apply are still owned by RenameDialogAsync.
public sealed partial class MainWindow
{
    private readonly StudioRenameReviewLease _studioRenameLease = new();
    private long _studioRenameTicket;
    private TaskCompletionSource<bool>? _studioRenameDecision;
    private StudioRenameExactView? _studioRenameView;

    private async Task<bool> ShowStudioRenameReviewAsync(RenameProposal frozen)
    {
        if (!_busy || _studioActivePage != StudioPage.Changes ||
            _studioEmbeddedLease.Active || _studioRenameLease.Active || _studioRejectLease.Active ||
            _studioNormalChanges is null || _studioReviewSurface is null ||
            _studioReviewContent is null || _studioNavigationHeader is null)
            throw new InvalidOperationException("Rename requires an exclusive Changes review surface.");
        StudioRenameReviewGate.RequireFrozen(frozen,_work.SessionId,_work.Revision,
            _work.Current?.Id,_work.Current?.Path);
        var ticket=_studioRenameLease.Begin();
        _studioRenameTicket=ticket;
        var decision=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _studioRenameDecision=decision;
        var view=new StudioRenameExactView(frozen);
        _studioRenameView=view;
        _studioReviewContent.Content=view;
        _studioNormalChanges.IsEnabled=false;
        _studioNormalChanges.IsVisible=false;
        _studioNavigationHeader.IsEnabled=false;
        _studioReviewSurface.IsVisible=true;
        _studioReviewPendingMessage!.Text="Check this exact Rename, then approve or cancel. No file has changed.";
        _heldKeys.Clear();
        view.ApproveRequested+=(_,_)=>FinishStudioRenameReviewDecision(ticket,true);
        view.DeclineRequested+=(_,_)=>FinishStudioRenameReviewDecision(ticket,false);
        Dispatcher.UIThread.Post(()=>
        {
            if (_studioRenameLease.Active && _studioRenameTicket==ticket &&
                !_studioRenameLease.DecisionPending) view.FocusCancel();
        },DispatcherPriority.Input);
        return await decision.Task;
    }

    private void FinishStudioRenameReviewDecision(long ticket,bool approve)
    {
        if (!_studioRenameLease.TryDecide(ticket))return;
        if (_studioRenameView is not null) _studioRenameView.IsEnabled=false;
        if (_studioReviewPendingMessage is not null)
            _studioReviewPendingMessage.Text=approve
                ? "Checking the selected original again. Apply is a separate action."
                : "Cancelling Rename review; no approval.";
        _studioRenameDecision?.TrySetResult(approve);
    }

    private void CancelStudioRenameReview()
    {
        if (_studioRenameLease.Active && !_studioRenameLease.DecisionPending)
            FinishStudioRenameReviewDecision(_studioRenameTicket,false);
    }

    private void EndStudioRenameReview()
    {
        if (!_studioRenameLease.Active)return;
        _studioRenameLease.End(_studioRenameTicket);
        if (_studioReviewContent is not null) _studioReviewContent.Content=null;
        if (_studioReviewSurface is not null) _studioReviewSurface.IsVisible=false;
        if (_studioNormalChanges is not null)
        {
            _studioNormalChanges.IsVisible=true;
            _studioNormalChanges.IsEnabled=true;
        }
        if (_studioNavigationHeader is not null) _studioNavigationHeader.IsEnabled=true;
        _studioRenameView=null;
        _studioRenameDecision=null;
        _heldKeys.Clear();
        // Do not focus Apply: approval is not a mutation instruction.
        Dispatcher.UIThread.Post(()=> { if (!_studioRenameLease.Active) _renameFixture.Focus(); },
            DispatcherPriority.Input);
    }
}
