using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

/// <summary>
/// P6F2C first ordinary GUI plus P6F2D2 isolated TWO-generated-photo continuation.
/// No personal photographs are enabled for native Reject.
/// All action gates recheck the isolated provenance; never auto replay.
/// </summary>
public sealed partial class MainWindow
{
    private DurableRejectCoordinator? _ordinaryRejectProbe;
    private RejectRecoveryObservation? _ordinaryRejectProbeObservation;
    private string _ordinaryRejectProbeError="";
    private readonly TextBlock _ordinaryRejectProbeStatus=Muted("Ordinary Reject GUI: generated probe only; no user photos.");
    private readonly Button _ordinaryRejectBeginSecond=new(){Content="5 · Begin SECOND generated photo review (no move)",MinHeight=43};
    private readonly Button _ordinaryRejectStageSecond=new(){Content="6 · Stage SECOND generated Reject (no move)",MinHeight=43};
    private readonly Button _ordinaryRejectProbeStage=new(){Content="1 · Stage generated ordinary photo (no move)",MinHeight=40};
    private readonly Button _ordinaryRejectProbeApprove=new(){Content="2 · Review exact paths and approve (no move)",MinHeight=40};
    private readonly Button _ordinaryRejectProbeExecute=new(){Content="3 · Relocate generated photo to verified recovery",MinHeight=43};
    private readonly Button _ordinaryRejectProbeUndo=new(){Content="4 · Undo verified ordinary Reject",MinHeight=43};

    private void AddOrdinaryRejectGuiProbeSection(StackPanel side)
    {
        var exp=new Expander { Header="Ordinary-folder Reject · GENERATED PROBE ONLY",IsExpanded=true };
        var body=new StackPanel { Spacing=8,Margin=new Thickness(0,8,0,8) };
        body.Children.Add(new TextBlock { Text="P6F2C first-epoch / P6F2D2 two-photo owner probes only. Separate GENERATED ordinary source, output and isolated app state. No personal photographs; no permanent delete or Trash. Restart before each Undo. Second operation requires explicit session transition and separate stage/approval.",TextWrapping=TextWrapping.Wrap });
        _ordinaryRejectProbeStatus.TextWrapping=TextWrapping.Wrap;
        body.Children.Add(_ordinaryRejectProbeStatus);
        foreach(var button in new[]{_ordinaryRejectProbeStage,_ordinaryRejectProbeApprove,_ordinaryRejectProbeExecute,_ordinaryRejectProbeUndo,
            _ordinaryRejectBeginSecond,_ordinaryRejectStageSecond})
        {
            button.HorizontalAlignment=HorizontalAlignment.Stretch;
            body.Children.Add(button);
        }
        _ordinaryRejectProbeExecute.Background=new SolidColorBrush(Color.Parse("#983B2F"));
        _ordinaryRejectProbeExecute.Foreground=Brushes.White;
        _ordinaryRejectProbeStage.Click+=async (_,_)=>await StageOrdinaryRejectProbeAsync();
        _ordinaryRejectProbeApprove.Click+=async (_,_)=>await ApproveOrdinaryRejectProbeAsync();
        _ordinaryRejectProbeExecute.Click+=async (_,_)=>await ExecuteOrdinaryRejectProbeAsync();
        _ordinaryRejectProbeUndo.Click+=async (_,_)=>await UndoOrdinaryRejectProbeAsync();
        _ordinaryRejectBeginSecond.Click+=async (_,_)=>await BeginSecondOrdinaryRejectGuiAsync();
        _ordinaryRejectStageSecond.Click+=async (_,_)=>await StageSecondOrdinaryRejectGuiAsync();
        _ordinaryRejectBeginSecond.IsVisible=OrdinaryRejectGuiProbePolicy.IsSecondEpochProbe;
        _ordinaryRejectStageSecond.IsVisible=OrdinaryRejectGuiProbePolicy.IsSecondEpochProbe;
        exp.Content=body;side.Children.Add(exp);
    }
    private void InitializeOrdinaryRejectGuiProbe()
    {
        _ordinaryRejectProbe=null;_ordinaryRejectProbeObservation=null;
        if(!OrdinaryRejectGuiProbePolicy.IsRequested || _sessionStore is null ||
           _moveFixtureEnabled || !NativeFileCapability.IsSupported) return;
        TryBindOrdinaryRejectGuiProbe();
    }
    private void TryBindOrdinaryRejectGuiProbe()
    {
        if(_sessionStore is null || !_storeHealthy || _moveFixtureEnabled ||
           !OrdinaryRejectGuiProbePolicy.IsRequested)return;
        try
        {
            OrdinaryRejectGuiProbePolicy.Require(_sessionStore,_work);
            _ordinaryRejectProbe=DurableRejectCoordinator.ForOrdinaryWorkspace(_sessionStore,_work);
            InspectOrdinaryRejectGuiProbe();
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException)
        {
            _ordinaryRejectProbe=null;_ordinaryRejectProbeObservation=null;
            _ordinaryRejectProbeError="Probe not ready / refused: "+ex.Message;
        }
    }
    private void InspectOrdinaryRejectGuiProbe()
    {
        _ordinaryRejectProbeObservation=null;
        if(_ordinaryRejectProbe is null || _sessionStore is null)return;
        try
        {
            OrdinaryRejectGuiProbePolicy.Require(_sessionStore,_work);
            _ordinaryRejectProbeObservation=_ordinaryRejectProbe.Inspect();
            _ordinaryRejectProbeError="";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException)
        { _ordinaryRejectProbeError="Recovery evidence refused: "+ex.Message; }
    }
    private DurableRejectItem? OrdinaryProbeRow(string requiredState,string requiredEvidence)
    {
        if(_busy || !_storeHealthy || _ordinaryRejectProbe is null || _sessionStore is null ||
           !_anyRejectHistory)return null;
        try
        {
            OrdinaryRejectGuiProbePolicy.Require(_sessionStore,_work);
            InspectOrdinaryRejectGuiProbe();
            if(_ordinaryRejectProbeObservation is not { } observed ||
               observed.State!=requiredState || observed.Evidence!=requiredEvidence)return null;
            var rows=_sessionStore.RejectItems();
            bool second=OrdinaryRejectGuiProbePolicy.IsSecondEpochProbe && rows.Count==2;
            if((!second && rows.Count!=1) ||
               (second && (!_sessionStore.InspectSecondOrdinaryRejectGuiSession(
                    _work.CaptureSession()).Verified ||
                    !_sessionStore.InspectOrdinaryRejectTerminalReceipts(1).Verified)))return null;
            var row=rows[^1];
            return row.SessionId==_work.SessionId && row.Id==observed.Operation &&
                row.PhotoId==_work.Photos[0].Id && row.State==requiredState ? row : null;
        }
        catch(Exception) {return null;}
    }
    private bool CanStageOrdinaryProbe()
    {
        if(_busy || !_storeHealthy || _sessionStore is null || _copyBarrier ||
           _anyRejectHistory || _anyMoveHistory || _anyRenameHistory || JournalNeedsAttention ||
           _work.PendingRejectCount!=1 || _work.AssignmentCount!=0 || _work.HasAppliedCopies)
            return false;
        try
        {
            OrdinaryRejectGuiProbePolicy.Require(_sessionStore,_work);
            if(_sessionStore.AllRejectHistoryCount()!=0)return false;
            return _work.Photos.Count==(OrdinaryRejectGuiProbePolicy.IsSecondEpochProbe ? 2 : 1) &&
                _work.IsPendingReject(_work.Photos[0].Id);
        }
        catch(Exception) {return false;}
    }
    private void UpdateOrdinaryRejectGuiProbeButtons()
    {
        if(!OrdinaryRejectGuiProbePolicy.IsRequested)return;
        if(_ordinaryRejectProbe is null && !_busy && _sessionStore is not null)
            TryBindOrdinaryRejectGuiProbe();
        _ordinaryRejectProbeStage.IsEnabled=CanStageOrdinaryProbe();
        _ordinaryRejectBeginSecond.IsEnabled=CanBeginSecondOrdinaryRejectGui();
        _ordinaryRejectStageSecond.IsEnabled=CanStageSecondOrdinaryRejectGui();
        _ordinaryRejectProbeApprove.IsEnabled=OrdinaryProbeRow("Staged","UNTOUCHED_SOURCE_VERIFIED") is not null;
        _ordinaryRejectProbeExecute.IsEnabled=OrdinaryProbeRow("Approved","UNTOUCHED_SOURCE_VERIFIED") is not null;
        _ordinaryRejectProbeUndo.IsEnabled=OrdinaryProbeRow("Committed","OWNED_ARCHIVE_VERIFIED") is not null;
        _ordinaryRejectProbeStatus.Text=_ordinaryRejectProbeObservation is {State: not "None"} status
            ? $"GENERATED ORDINARY PROBE · {status.State} · {status.Evidence}. No auto replay."
            : _ordinaryRejectProbeError.Length!=0 ? _ordinaryRejectProbeError
            : "Open the probe source/output and mark exactly one generated photo for Reject review.";
    }
    private string OrdinaryRejectGuiProbeStartupSummary() =>
        _ordinaryRejectProbeObservation is { } o
            ? $"Ordinary synthetic GUI probe Reject: {o.State} · {o.Evidence}. " +
              "Preserve original, owned archive, isolated state; use probe recovery controls."
            : "Ordinary synthetic Reject recovery requires manual inspection. " + _ordinaryRejectProbeError;
    private string OrdinaryRejectGuiProbeInspectionReport()
    {
        InspectOrdinaryRejectGuiProbe();
        var o=_ordinaryRejectProbeObservation;
        return "\nGENERATED ORDINARY REJECT PROBE — separate owned journal (read only)\n"+
            (o is null ? "EVIDENCE REFUSED: "+_ordinaryRejectProbeError+"\n" :
            $"STATE: {o.State}\nEVIDENCE: {o.Evidence}\nSOURCE: {o.Source}\nOWNED ARCHIVE: {o.Archive}\n")+
            "Never manually delete/move an owned recovery archive. No automatic recovery.\n";
    }
    private async Task StageOrdinaryRejectProbeAsync()
    {
        if(!CanStageOrdinaryProbe() || _sessionStore is null || _ordinaryRejectProbe is null)return;
        var photo=_work.Photos[0];
        if(!await ConfirmNativeRejectAsync("Stage generated ordinary-folder Reject — NO MOVE",
           "SYNTHETIC GENERATED FILE ONLY.\n\nORIGINAL: "+photo.Path+
           "\n\nCreates a durable journal proposal only. No file moves.\nNext actions require separate exact-path approval and execution.",
           "STAGE PROPOSAL (NO FILE MOVE)"))return;
        try
        {
            if(!CanStageOrdinaryProbe())throw new IOException("Probe changed while dialog open.");
            _busy=true;Refresh();
            var row=await Task.Run(()=>_ordinaryRejectProbe.Stage(_work,photo.Id));
            _anyRejectHistory=true;InspectOrdinaryRejectGuiProbe();
            Message("GENERATED ORDINARY REJECT STAGED; original untouched. Owned target: "+row.Archive);
        }
        catch(Exception ex){Message("ORDINARY PROBE STAGE REFUSED / UNCERTAIN. NO RETRY. Preserve state: "+ex.Message);}
        finally{_busy=false;SyncOrdinaryRejectHistory();Refresh();}
    }
    private async Task ApproveOrdinaryRejectProbeAsync()
    {
        var staged=OrdinaryProbeRow("Staged","UNTOUCHED_SOURCE_VERIFIED");
        if(staged is null || _ordinaryRejectProbe is null)return;
        if(!await ConfirmNativeRejectAsync("Authorize exact synthetic Reject paths — NO MOVE",
            "ORIGINAL:\n"+staged.Source+"\n\nOWNED RECOVERY:\n"+staged.Archive+
            "\n\nSHA-256:\n"+staged.SourceHash+
            "\n\nNo file moves during approval. Execute requires separate confirmation.",
            "AUTHORIZE THESE EXACT PATHS (NO MOVE)"))return;
        try
        {
            if(OrdinaryProbeRow("Staged","UNTOUCHED_SOURCE_VERIFIED")!=staged)
                throw new IOException("Staged evidence changed during consent.");
            _busy=true;Refresh();
            await Task.Run(()=>_ordinaryRejectProbe.Approve(staged,true));
            Message("SYNTHETIC ORDINARY REJECT APPROVED — no file moved.");
        }
        catch(Exception ex){Message("PROBE APPROVAL REFUSED. No retry: "+ex.Message);}
        finally{_busy=false;InspectOrdinaryRejectGuiProbe();Refresh();}
    }
    private async Task ExecuteOrdinaryRejectProbeAsync()
    {
        var approved=OrdinaryProbeRow("Approved","UNTOUCHED_SOURCE_VERIFIED");
        if(approved is null || _ordinaryRejectProbe is null)return;
        if(!await ConfirmNativeRejectAsync("RELOCATE generated ordinary photo to owned recovery",
            "THIS IS A NATIVE FILE RELOCATION (NOT DELETE/TRASH), ON A GENERATED SYNTHETIC PHOTO ONLY.\n\n"+
            "ORIGINAL (becomes absent):\n"+approved.Source+"\n\n"+
            "OWNED ARCHIVE (must remain intact):\n"+approved.Archive+
            "\n\nNo automatic retry. Restart and separately Undo only after verified recovery.",
            "RELOCATE VERIFIED GENERATED PHOTO"))return;
        try
        {
            if(OrdinaryProbeRow("Approved","UNTOUCHED_SOURCE_VERIFIED")!=approved)
                throw new IOException("Approved evidence changed during confirmation.");
            _busy=true;Refresh();
            await Task.Run(()=>_ordinaryRejectProbe.Execute(approved));
            InspectOrdinaryRejectGuiProbe();
            if(_ordinaryRejectProbeObservation is not {State:"Committed",Evidence:"OWNED_ARCHIVE_VERIFIED"})
                throw new IOException("Committed recovery identity not proven.");
            Message("GENERATED ORIGINAL HELD IN VERIFIED RECOVERY. Restart; Undo separately. Normal sorting locked.");
        }
        catch(Exception ex){Message("NATIVE PROBE EXECUTE REFUSED/UNCERTAIN; NO RETRY. Preserve source, owned archive and journal: "+ex.Message);}
        finally{_busy=false;InspectOrdinaryRejectGuiProbe();Refresh();}
    }
    private async Task UndoOrdinaryRejectProbeAsync()
    {
        var committed=OrdinaryProbeRow("Committed","OWNED_ARCHIVE_VERIFIED");
        if(committed is null || _ordinaryRejectProbe is null)return;
        if(!await ConfirmNativeRejectAsync("Undo generated ordinary Reject — no replacement",
            "OWNED RECOVERY ARCHIVE:\n"+committed.Archive+
            "\n\nRESTORE TO ORIGINAL:\n"+committed.Source+
            "\n\nNative identity and hash must match; occupied original refuses Undo. No auto-replay.",
            "RESTORE VERIFIED GENERATED ORIGINAL ONCE"))return;
        try
        {
            if(OrdinaryProbeRow("Committed","OWNED_ARCHIVE_VERIFIED")!=committed)
                throw new IOException("Archive changed during Undo dialog.");
            _busy=true;Refresh();
            await Task.Run(()=>_ordinaryRejectProbe.Undo(committed.SessionId));
            InspectOrdinaryRejectGuiProbe();
            if(_ordinaryRejectProbeObservation is not {State:"Undone",Evidence:"RESTORED_SOURCE_VERIFIED"})
                throw new IOException("Native restore verification uncertain.");
            Message("GENERATED ORIGINAL RESTORED. Journal/owned markers retained, normal sorting remains locked.");
        }
        catch(Exception ex){Message("NATIVE PROBE UNDO REFUSED/UNCERTAIN; NO RETRY. Preserve original/archive/state: "+ex.Message);}
        finally{_busy=false;InspectOrdinaryRejectGuiProbe();Refresh();}
    }
    // P6F2D2: A NEW explicit step, after first native Undo. No file action,
    // no automatic successor staging, and no mutation of P6F2C evidence.
    private bool CanBeginSecondOrdinaryRejectGui()
    {
        if(!OrdinaryRejectGuiProbePolicy.IsSecondEpochProbe || _busy || !_storeHealthy ||
           _sessionStore is null || _ordinaryRejectProbe is null || !_anyRejectHistory ||
           _copyBarrier || _anyMoveHistory || _anyRenameHistory || JournalNeedsAttention ||
           _ordinaryRejectProbeObservation is not {State:"Undone",Evidence:"RESTORED_SOURCE_VERIFIED"})
            return false;
        try
        {
            OrdinaryRejectGuiProbePolicy.Require(_sessionStore,_work);
            var rows=_sessionStore.RejectItems();
            return rows.Count==1 && rows[0].SessionId==_work.SessionId &&
                _work.Photos.Count==2 &&
                !_sessionStore.InspectSecondOrdinaryRejectGuiSession(_work.CaptureSession()).Present;
        }
        catch(Exception){return false;}
    }
    private bool CanStageSecondOrdinaryRejectGui()
    {
        if(!OrdinaryRejectGuiProbePolicy.IsSecondEpochProbe || _busy || !_storeHealthy ||
           _sessionStore is null || _ordinaryRejectProbe is null || !_anyRejectHistory ||
           _copyBarrier || _anyMoveHistory || _anyRenameHistory || JournalNeedsAttention ||
           _work.PendingRejectCount!=1 || _work.AssignmentCount!=0 || _work.HasAppliedCopies)
            return false;
        try
        {
            OrdinaryRejectGuiProbePolicy.Require(_sessionStore,_work);
            var rows=_sessionStore.RejectItems();
            return rows.Count==1 && rows[0].State=="Undone" &&
                _sessionStore.InspectSecondOrdinaryRejectGuiSession(_work.CaptureSession()).Verified &&
                _sessionStore.InspectOrdinaryRejectTerminalReceipts(1).Verified &&
                _work.Photos.Count==1 &&
                Path.GetFileName(_work.Photos[0].Path)=="photo_02.png" &&
                _work.IsPendingReject(_work.Photos[0].Id);
        }
        catch(Exception){return false;}
    }
    private async Task BeginSecondOrdinaryRejectGuiAsync()
    {
        if(!CanBeginSecondOrdinaryRejectGui() || _sessionStore is null)return;
        if(!await ConfirmNativeRejectAsync("Begin SECOND generated ordinary review — NO MOVE",
            "P6F2D2 GENERATED PROBE ONLY. First photo_01 was restored through native Undo.\n\n"+
            "This action verifies and seals its immutable terminal receipt, then atomically " +
            "saves a new one-photo session containing ONLY generated photo_02 marked for review.\n\n"+
            "NO file is moved. NO native operation is staged. After transition, explicitly " +
            "Stage, Review, Approve, Execute and Undo the distinct second photo.",
            "BEGIN SECOND GENERATED SESSION (NO MOVE)"))return;
        try
        {
            if(!CanBeginSecondOrdinaryRejectGui())
                throw new IOException("Prior terminal evidence changed during second-session consent.");
            _busy=true;Refresh();
            var next=await Task.Run(()=>_sessionStore.BeginSecondOrdinaryRejectGuiSession(
                _work.CaptureSession(),explicitlyConfirmed:true));
            _work.RestoreSession(next);
            if(!_sessionStore.InspectSecondOrdinaryRejectGuiSession(_work.CaptureSession()).Verified)
                throw new IOException("Second ordinary GUI checkpoint could not be reverified.");
            _work.RevokeApproval();_modeGate.Revoke();_prepared=null;
            _hasReviewed=false;_reviewedMoveId=null;_requestedPhotoId=null;
            InspectOrdinaryRejectGuiProbe();
            Message("SECOND GENERATED ORDINARY SESSION SAVED, WITHOUT FILE MOVE. " +
                "First owned receipt preserved. Now separately Stage photo_02.");
        }
        catch(Exception ex)
        {
            _storeHealthy=false;
            Message("SECOND ORDINARY GUI SESSION REFUSED / UNCERTAIN; NO RETRY. " +
                "Restart read-only, preserve SQLite and originals. "+ex.Message);
        }
        finally{_busy=false;Refresh();}
    }
    private async Task StageSecondOrdinaryRejectGuiAsync()
    {
        if(!CanStageSecondOrdinaryRejectGui() || _sessionStore is null ||
           _ordinaryRejectProbe is null)return;
        var photo=_work.Photos[0];
        if(!await ConfirmNativeRejectAsync("Stage SECOND generated ordinary Reject — NO MOVE",
            "SEPARATE EXPLICIT STAGE. Generated photo_02 ONLY:\n"+photo.Path+
            "\n\nEarlier native Reject has been restored and terminal-sealed. " +
            "This action creates ONE fresh receipt-bound journal proposal; NO file moves. " +
            "Exact-path approval and execution each require additional consent.",
            "ACKNOWLEDGE SECOND STAGE (NO MOVE)"))return;
        try
        {
            if(!CanStageSecondOrdinaryRejectGui())
                throw new IOException("Second session or receipt changed during consent.");
            _busy=true;Refresh();
            var staged=await Task.Run(()=>_ordinaryRejectProbe.StageSuccessor(
                _work,photo.Id,acknowledgeNewEpoch:true));
            _anyRejectHistory=true;InspectOrdinaryRejectGuiProbe();
            if(_ordinaryRejectProbeObservation is not
                {State:"Staged",Evidence:"UNTOUCHED_SOURCE_VERIFIED"} ||
                !_sessionStore.InspectSecondOrdinaryRejectGuiSession(
                    _work.CaptureSession()).Verified)
                throw new IOException("Second native proposal needs manual inspection.");
            Message("SECOND ORDINARY REJECT STAGED. Original remains intact; " +
                "independent exact-path approval required. Owned archive: "+staged.Archive);
        }
        catch(Exception ex)
        {
            Message("SECOND ORDINARY STAGE REFUSED / UNCERTAIN. NO RETRY. " +
                "Preserve SQLite and original files. "+ex.Message);
        }
        finally{_busy=false;InspectOrdinaryRejectGuiProbe();Refresh();}
    }
    private void SyncOrdinaryRejectHistory()
    {
        if(_sessionStore is null)return;
        try {_anyRejectHistory=_sessionStore.AllRejectHistoryCount()!=0;}
        catch(Exception) {_storeHealthy=false;}
    }
}
