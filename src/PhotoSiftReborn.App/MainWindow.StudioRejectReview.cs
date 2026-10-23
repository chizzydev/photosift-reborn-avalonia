using Avalonia.Controls;
using Avalonia.Threading;

namespace PhotoSiftReborn;

// M6B ordinary durable Reject: contained consent is presentation ONLY.
// Distinct Stage, Approve, Execute and Undo remain owned by the native coordinator.
public sealed partial class MainWindow
{
    private readonly StudioRejectReviewLease _studioRejectLease=new();
    private long _studioRejectTicket;
    private TaskCompletionSource<bool>? _studioRejectDecision;
    private StudioRejectExactView? _studioRejectView;

    private async Task<bool> ShowStudioRejectReviewAsync(DurableRejectItem frozen,StudioRejectPhase phase)
    {
        if(_studioActivePage!=StudioPage.Changes || _busy || !_storeHealthy ||
            _studioEmbeddedLease.Active || _studioRenameLease.Active || _studioRejectLease.Active ||
            _studioNormalChanges is null || _studioReviewSurface is null ||
            _studioReviewContent is null || _studioNavigationHeader is null ||
            _sessionStore is null)
            throw new InvalidOperationException("Reject review requires an exclusively owned, healthy Changes workspace.");
        StudioRejectReviewGate.RequireExact(frozen,phase,_work.SessionId,
            frozen.InstallationId,frozen.PhotoId);
        // OrdinaryFirstRejectRow independently checks stored history, native
        // observation phase/evidence and exact session/photo path.
        var observed=OrdinaryFirstRejectRow(StudioRejectReviewGate.ExpectedState(phase),
            StudioRejectReviewGate.ExpectedEvidence(phase));
        if(observed is null || !Equals(observed,frozen))
            throw new InvalidOperationException("The native durable Reject row changed before showing exact review.");
        var ticket=_studioRejectLease.Begin(phase);
        _studioRejectTicket=ticket;
        try
        {
            var decision=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _studioRejectDecision=decision;
            var view=new StudioRejectExactView(frozen,phase);
            _studioRejectView=view;
            _studioReviewContent.Content=view;
            _studioNormalChanges.IsEnabled=false;
            _studioNormalChanges.IsVisible=false;
            _studioNavigationHeader.IsEnabled=false;
            _studioReviewSurface.IsVisible=true;
            // A contained ordinary Reject dialog is not a modal Window. Block
            // global drop/import/shortcut actions while awaiting the decision.
            // OrdinaryFirstRejectRow allows an exact post-consent read only
            // while the phase-bound decision is already single-use.
            _busy=true;
            _studioReviewPendingMessage!.Text="Finish this exact Reject phase or cancel. No automatic operation or retry.";
            _heldKeys.Clear();
            view.ApproveRequested+=(_,_)=>FinishStudioRejectDecision(ticket,phase,true);
            view.DeclineRequested+=(_,_)=>FinishStudioRejectDecision(ticket,phase,false);
            Dispatcher.UIThread.Post(()=>
            {
                if(_studioRejectLease.Active && _studioRejectTicket==ticket &&
                    !_studioRejectLease.DecisionPending)view.FocusCancel();
            },DispatcherPriority.Input);
            return await decision.Task;
        }
        catch
        {
            EndStudioRejectReview();
            throw;
        }
    }

    private void FinishStudioRejectDecision(long ticket,StudioRejectPhase phase,bool approved)
    {
        if(!_studioRejectLease.TryDecide(ticket,phase))return;
        if(_studioRejectView is not null)_studioRejectView.IsEnabled=false;
        if(_studioReviewPendingMessage is not null)
            _studioReviewPendingMessage.Text=approved
                ? "Rechecking native stage/evidence before the separate coordinator action."
                : "Cancelling this phase. Journal and original remain unchanged.";
        _studioRejectDecision?.TrySetResult(approved);
    }
    private void CancelStudioRejectReview()
    {
        if(_studioRejectLease.Active && !_studioRejectLease.DecisionPending &&
            _studioRejectLease.Phase is { } phase)
            FinishStudioRejectDecision(_studioRejectTicket,phase,false);
    }
    private void EndStudioRejectReview()
    {
        if(!_studioRejectLease.Active)return;
        _studioRejectLease.End(_studioRejectTicket);
        if(_studioReviewContent is not null)_studioReviewContent.Content=null;
        if(_studioReviewSurface is not null)_studioReviewSurface.IsVisible=false;
        if(_studioNormalChanges is not null)
        {
            _studioNormalChanges.IsVisible=true;
            _studioNormalChanges.IsEnabled=true;
        }
        if(_studioNavigationHeader is not null)_studioNavigationHeader.IsEnabled=true;
        _studioRejectDecision=null;_studioRejectView=null;
        _busy=false;
        _heldKeys.Clear();
        // Never focus the relocation or Undo button on review completion.
        Dispatcher.UIThread.Post(()=> { if(!_studioRejectLease.Active) _ordinaryFirstRejectApprove.Focus(); },
            DispatcherPriority.Input);
    }
    private async Task<bool> ConfirmOrdinaryRejectPhaseAsync(DurableRejectItem row,
        StudioRejectPhase phase,string title,string explanation,string acceptText)
    {
        StudioRejectReviewGate.RequireExact(row,phase,_work.SessionId,row.InstallationId,row.PhotoId);
        if(_studioActivePage==StudioPage.Changes)
            return await ShowStudioRejectReviewAsync(row,phase);
        // Historical standalone modal remains for non-Studio callers. It does
        // not relax any native phase check or introduce a new operation path.
        return await ConfirmNativeRejectAsync(title,explanation,acceptText);
    }
}
