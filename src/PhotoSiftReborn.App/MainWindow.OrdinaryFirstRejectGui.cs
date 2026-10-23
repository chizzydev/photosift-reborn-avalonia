using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

/// <summary>
/// One FIRST ordinary local-file Reject only, separated from the historical
/// P5C and P6F2C/D2 generated-fixture lanes. Review mark != Stage != Approval
/// != Native Execute. This lane NEVER deletes to Trash and NEVER auto retries.
/// Further epochs and mixed journals are explicitly denied, not reset.
/// </summary>
public sealed partial class MainWindow
{
    private DurableRejectCoordinator? _ordinaryFirstReject;
    private RejectRecoveryObservation? _ordinaryFirstRejectObservation;
    private string _ordinaryFirstRejectError="";
    private readonly TextBlock _ordinaryFirstRejectStatus=Muted(
        "Reject requires one reviewed, durably saved mark in a local flat collection. No deletion.");
    private readonly Button _ordinaryFirstRejectStage=new(){Content="1 · Stage ONE marked Reject (NO file change)",MinHeight=40};
    private readonly Button _ordinaryFirstRejectApprove=new(){Content="2 · Review exact original and recovery / approve",MinHeight=40};
    private readonly Button _ordinaryFirstRejectExecute=new(){Content="3 · RELOCATE approved original to owned recovery",MinHeight=43};
    private readonly Button _ordinaryFirstRejectUndo=new(){Content="4 · Undo verified Reject — restore original",MinHeight=43};

    private readonly Expander _ordinaryFirstRejectExpander = new()
    {
        Header = "Reject to recovery · local first operation", IsExpanded = false
    };

    private void AddOrdinaryFirstRejectSection(StackPanel side)
    {
        var panel=new StackPanel {Spacing=8,Margin=new Thickness(0,8,0,8)};
        panel.Children.Add(new TextBlock {Text="Reject to recoverable archive — first local operation",
            FontWeight=FontWeight.SemiBold,FontSize=17,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(Muted("Mark exactly ONE photo for Reject review first. Stage only records a proposal; " +
            "Approve checks exact paths; Execute relocates the ORIGINAL into an owned same-volume recovery archive. " +
            "Restart and independently verified Undo restore it. No Trash, permanent deletion, " +
            "mixed operation history, network/cloud drives, automatic retry or replay. " +
            "Do not use personal photographs until owner-generated GUI acceptance is complete."));
        _ordinaryFirstRejectStatus.TextWrapping=TextWrapping.Wrap;
        panel.Children.Add(_ordinaryFirstRejectStatus);
        foreach(var b in new[]{_ordinaryFirstRejectStage,_ordinaryFirstRejectApprove,
            _ordinaryFirstRejectExecute,_ordinaryFirstRejectUndo})
        {b.HorizontalAlignment=HorizontalAlignment.Stretch;panel.Children.Add(b);}
        _ordinaryFirstRejectExecute.Background=new SolidColorBrush(Color.Parse("#983B2F"));
        _ordinaryFirstRejectExecute.Foreground=Brushes.White;
        _ordinaryFirstRejectStage.Click+=async (_,_)=>await StageOrdinaryFirstRejectAsync();
        _ordinaryFirstRejectApprove.Click+=async (_,_)=>await ApproveOrdinaryFirstRejectAsync();
        _ordinaryFirstRejectExecute.Click+=async (_,_)=>await ExecuteOrdinaryFirstRejectAsync();
        _ordinaryFirstRejectUndo.Click+=async (_,_)=>await UndoOrdinaryFirstRejectAsync();
        _ordinaryFirstRejectExpander.Content = panel;
        side.Children.Add(_ordinaryFirstRejectExpander);
    }
    private bool FirstOrdinaryRejectLane => NativeFileCapability.IsSupported &&
        !_moveFixtureEnabled && !OrdinaryRejectGuiProbePolicy.IsRequested;
    private void TryBindOrdinaryFirstReject()
    {
        _ordinaryFirstReject=null;_ordinaryFirstRejectObservation=null;
        if(!FirstOrdinaryRejectLane || _sessionStore is null || !_storeHealthy)return;
        try
        {
            // A fresh saved session has no Reject mark yet; binding is legal
            // only after its single-source and saved-workspace policy passes.
            if(!OrdinaryFirstRejectPolicy.CanUseWorkspace(_sessionStore,_work))return;
            _ordinaryFirstReject=DurableRejectCoordinator.ForOrdinaryWorkspace(_sessionStore,_work);
            InspectOrdinaryFirstReject();
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException)
        {_ordinaryFirstRejectError="Ordinary Reject binding refused: "+ex.Message;}
    }
    private void InspectOrdinaryFirstReject()
    {
        _ordinaryFirstRejectObservation=null;
        if(_ordinaryFirstReject is null)return;
        try
        {
            _ordinaryFirstRejectObservation=_ordinaryFirstReject.Inspect();
            _ordinaryFirstRejectError="";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException)
        {_ordinaryFirstRejectError="Reject evidence refused: "+ex.Message;}
    }
    private DurableRejectItem? OrdinaryFirstRejectRow(string state,string evidence)
    {
        if((_busy && !(_studioRejectLease.Active && _studioRejectLease.DecisionPending)) ||
           !_storeHealthy || _sessionStore is null || !FirstOrdinaryRejectLane ||
           !_anyRejectHistory || !OrdinaryFirstRejectPolicy.CanInspectHistory(_sessionStore,_work))return null;
        try
        {
            TryBindOrdinaryFirstReject();
            if(_ordinaryFirstRejectObservation is not { } observation ||
                observation.State!=state || observation.Evidence!=evidence)return null;
            var rows=_sessionStore.RejectItems();
            if(rows.Count!=1)return null;
            var row=rows[0];
            return row.Id==observation.Operation && row.SessionId==_work.SessionId &&
                row.State==state && row.PhotoId!=Guid.Empty &&
                _work.Photos.Any(p=>p.Id==row.PhotoId &&
                    SortingWorkspace.PathComparer.Equals(p.Path,row.Source)) ? row : null;
        }
        catch(Exception){return null;}
    }
    private void UpdateOrdinaryFirstRejectButtons()
    {
        if(!FirstOrdinaryRejectLane)
        {
            foreach(var button in new[]{_ordinaryFirstRejectStage,_ordinaryFirstRejectApprove,
                _ordinaryFirstRejectExecute,_ordinaryFirstRejectUndo})button.IsEnabled=false;
            return;
        }
        if(!_busy && _sessionStore is not null)TryBindOrdinaryFirstReject();
        _ordinaryFirstRejectStage.IsEnabled=!_busy && _storeHealthy && !_copyBarrier &&
            !_anyMoveHistory && !_anyRenameHistory && !_anyRejectHistory && !JournalNeedsAttention &&
            _sessionStore is not null && OrdinaryFirstRejectPolicy.CanStage(_sessionStore,_work) &&
            _ordinaryFirstReject is not null;
        _ordinaryFirstRejectApprove.IsEnabled=OrdinaryFirstRejectRow("Staged","UNTOUCHED_SOURCE_VERIFIED") is not null;
        _ordinaryFirstRejectExecute.IsEnabled=OrdinaryFirstRejectRow("Approved","UNTOUCHED_SOURCE_VERIFIED") is not null;
        _ordinaryFirstRejectUndo.IsEnabled=OrdinaryFirstRejectRow("Committed","OWNED_ARCHIVE_VERIFIED") is not null;
        _ordinaryFirstRejectStatus.Text=_ordinaryFirstRejectObservation is {State:not "None"} o
            ? $"Native Reject: {o.State} · {o.Evidence}. Historical journal stays locked. No retry."
            : _ordinaryFirstRejectError.Length>0 ? _ordinaryFirstRejectError
            : "Mark exactly ONE unassigned photo; native approval is separate. First fresh local operation only.";
    }
    private async Task StageOrdinaryFirstRejectAsync()
    {
        if(_sessionStore is null || _ordinaryFirstReject is null ||
           !OrdinaryFirstRejectPolicy.CanStage(_sessionStore,_work))return;
        var photo=_work.PendingRejectReviewPhotos.Single();
        if(!await ConfirmNativeRejectAsync("Stage ONE ordinary Reject — no file change",
            "SAVED REJECT MARK — proposal only. No file move.\n\nORIGINAL:\n"+photo.Path+
            "\n\nNext: inspect exact recovery location and approve separately.",
            "STAGE EXACT PROPOSAL (NO FILE CHANGE)"))return;
        try
        {
            if(!OrdinaryFirstRejectPolicy.CanStage(_sessionStore,_work))
                throw new IOException("Ordinary Reject saved mark or scope changed during consent.");
            _busy=true;Refresh();
            var row=await Task.Run(()=>_ordinaryFirstReject.Stage(_work,photo.Id));
            _anyRejectHistory=true;InspectOrdinaryFirstReject();
            Message("ONE ORDINARY REJECT STAGED. Original still present. Recovery proposal: "+row.Archive);
        }
        catch(Exception ex){Message("REJECT STAGE REFUSED / UNCERTAIN; NO RETRY: "+ex.Message);}
        finally{_busy=false;SyncOrdinaryRejectHistory();Refresh();}
    }
    private async Task ApproveOrdinaryFirstRejectAsync()
    {
        var row=OrdinaryFirstRejectRow("Staged","UNTOUCHED_SOURCE_VERIFIED");
        if(row is null || _ordinaryFirstReject is null)return;
        try
        {
        if(!await ConfirmOrdinaryRejectPhaseAsync(row,StudioRejectPhase.ApproveStaged,
            "Review and approve ONE ordinary Reject — no relocation",
            "ORIGINAL (will become absent only after later Execute):\n"+row.Source+
            "\n\nOWNED RECOVERY ARCHIVE:\n"+row.Archive+
            "\n\nORIGINAL SHA-256:\n"+row.SourceHash+
            "\n\nNative identity: "+row.SourceIdentity+
            "\n\nApproval alone performs NO filesystem change. No Trash or permanent delete.",
            "APPROVE THESE EXACT PATHS (NO MOVE)")) return;

            if(OrdinaryFirstRejectRow("Staged","UNTOUCHED_SOURCE_VERIFIED")!=row)
                throw new IOException("Native stage changed during exact-path approval.");
            _busy=true;Refresh();
            await Task.Run(()=>_ordinaryFirstReject.Approve(row,explicitApproval:true));
            Message("ONE ORDINARY REJECT APPROVED. Original unchanged; separate Execute required.");
        }
        catch(Exception ex){Message("REJECT APPROVAL REFUSED; NO RETRY: "+ex.Message);}
        finally{EndStudioRejectReview();_busy=false;InspectOrdinaryFirstReject();Refresh();}
    }
    private async Task ExecuteOrdinaryFirstRejectAsync()
    {
        var row=OrdinaryFirstRejectRow("Approved","UNTOUCHED_SOURCE_VERIFIED");
        if(row is null || _ordinaryFirstReject is null)return;
        try
        {
        if(!await ConfirmOrdinaryRejectPhaseAsync(row,StudioRejectPhase.ExecuteApproved,
            "EXECUTE ONE approved ordinary Reject — original relocates",
            "THIS CHANGES THE ORIGINAL FILE LOCATION (NOT DELETION).\n\nORIGINAL:\n"+row.Source+
            "\n\nOWNED SAME-VOLUME RECOVERY:\n"+row.Archive+
            "\n\nDurable intent is recorded BEFORE relocation. No automatic retry. " +
            "Restart, inspect, then separately Undo from verified owned recovery.",
            "RELOCATE ONE APPROVED ORIGINAL TO RECOVERY")) return;

            if(OrdinaryFirstRejectRow("Approved","UNTOUCHED_SOURCE_VERIFIED")!=row)
                throw new IOException("Approved native Reject evidence changed.");
            _busy=true;Refresh();
            await Task.Run(()=>_ordinaryFirstReject.Execute(row));
            InspectOrdinaryFirstReject();
            if(_ordinaryFirstRejectObservation is not {State:"Committed",Evidence:"OWNED_ARCHIVE_VERIFIED"})
                throw new IOException("Owned archive proof unavailable; preserve recovery / manual inspection.");
            Message("ORDINARY REJECT COMMITTED: original retained in verified owned recovery. " +
                "Close/restart before separately verified Undo. No permanent deletion.");
        }
        catch(Exception ex){Message("REJECT EXECUTE REFUSED / UNCERTAIN; NO RETRY. Preserve archives and SQLite: "+ex.Message);}
        finally{EndStudioRejectReview();_busy=false;InspectOrdinaryFirstReject();Refresh();}
    }
    private async Task UndoOrdinaryFirstRejectAsync()
    {
        var row=OrdinaryFirstRejectRow("Committed","OWNED_ARCHIVE_VERIFIED");
        if(row is null || _ordinaryFirstReject is null)return;
        try
        {
        if(!await ConfirmOrdinaryRejectPhaseAsync(row,StudioRejectPhase.RestoreCommitted,
            "Undo ONE verified ordinary Reject",
            "PROVEN OWNED ARCHIVE:\n"+row.Archive+
            "\n\nRESTORE ORIGINAL (must be vacant):\n"+row.Source+
            "\n\nNative no-replace restoration; identity + hash checked. No automatic retry.",
            "RESTORE ONE VERIFIED ORIGINAL")) return;

            if(OrdinaryFirstRejectRow("Committed","OWNED_ARCHIVE_VERIFIED")!=row)
                throw new IOException("Reject archive ownership changed during Undo consent.");
            _busy=true;Refresh();
            await Task.Run(()=>_ordinaryFirstReject.Undo(row.SessionId));
            InspectOrdinaryFirstReject();
            if(_ordinaryFirstRejectObservation is not {State:"Undone",Evidence:"RESTORED_SOURCE_VERIFIED"})
                throw new IOException("Restored native original proof unavailable.");
            Message("ORDINARY REJECT UNDONE: original native bytes and identity restored. " +
                "History/owned markers retained; sorting remains locked pending successor authorization.");
        }
        catch(Exception ex){Message("REJECT UNDO REFUSED / UNCERTAIN. No retry. Preserve SQLite and recovery: "+ex.Message);}
        finally{EndStudioRejectReview();_busy=false;InspectOrdinaryFirstReject();Refresh();}
    }
    private string OrdinaryFirstRejectRecoveryReport()
    {
        InspectOrdinaryFirstReject();
        var o=_ordinaryFirstRejectObservation;
        return "\nORDINARY FIRST REJECT — independent owned recovery\n"+
            (o is null ? "EVIDENCE NOT PROVED: "+_ordinaryFirstRejectError+"\n" :
                $"STATE: {o.State}\nEVIDENCE: {o.Evidence}\nSOURCE: {o.Source}\nARCHIVE: {o.Archive}\n")+
            "No Trash, automatic replay, history reset or manual archive deletion.\n";
    }
}
