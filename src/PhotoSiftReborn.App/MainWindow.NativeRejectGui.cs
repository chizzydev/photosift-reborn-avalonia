using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

// P6D2B: bounded, generated-fixture native Reject GUI. This is NOT an
// unrestricted user photograph operation or permanent deletion/Trash UI.
public sealed partial class MainWindow
{
    private DurableRejectCoordinator? _nativeReject;
    private RejectRecoveryObservation? _nativeRejectObservation;
    private string _nativeRejectInspectionError = "";
    private readonly TextBlock _nativeRejectStatus = Muted("Native Reject: generate a disposable fixture, then mark ONE photo for review.");
    private readonly Button _beginSecondNativeReject = new()
        {Content="Begin SECOND generated Reject review (after verified Undo)",MinHeight=43};
    private readonly Button _stageSecondNativeReject = new()
        {Content="Stage SECOND generated Reject (no file move)",MinHeight=43};
    private readonly Button _nativeRejectStage = new()
        { Content="1 · Stage native Reject (no file move)", MinHeight=40 };
    private readonly Button _nativeRejectApprove = new()
        { Content="2 · Review & authorize exact paths", MinHeight=40 };
    private readonly Button _nativeRejectExecute = new()
        { Content="3 · Execute approved Reject (test only)", MinHeight=43 };
    private readonly Button _nativeRejectUndo = new()
        { Content="Undo native Reject · restore original", MinHeight=43 };

    private void AddNativeRejectGuiSection(StackPanel side)
    {
        side.Children.Add(new Border { Height=1, Background=Brushes.Gray, Margin=new Thickness(0,8,0,3) });
        side.Children.Add(new TextBlock { Text="Native Reject recovery · TEST FIXTURE ONLY",
            FontSize=17, FontWeight=FontWeight.SemiBold, TextWrapping=TextWrapping.Wrap });
        side.Children.Add(Muted("FIRST: mark one generated photo, stage, approve, execute and Undo. SECOND: only after independently proven native Undo, explicitly begin a new saved photo_02 review session, then stage, approve, execute and Undo again. No Trash, personal photos, automatic recovery or permanent deletion."));
        _nativeRejectStatus.TextWrapping=TextWrapping.Wrap;
        side.Children.Add(_nativeRejectStatus);
        foreach(var button in new[]{_beginSecondNativeReject,_stageSecondNativeReject,_nativeRejectStage,_nativeRejectApprove,_nativeRejectExecute,_nativeRejectUndo})
        {
            button.HorizontalAlignment=HorizontalAlignment.Stretch;
            side.Children.Add(button);
        }
        _nativeRejectExecute.Background=new SolidColorBrush(Color.Parse("#983B2F"));
        _nativeRejectExecute.Foreground=Brushes.White;
        _nativeRejectExecute.FontWeight=FontWeight.Bold;
        _beginSecondNativeReject.Click+=async (_,_)=>await BeginSecondNativeRejectGuiAsync();
        _stageSecondNativeReject.Click+=async (_,_)=>await StageSecondNativeRejectGuiAsync();
        _nativeRejectStage.Click+=async (_,_)=>await StageNativeRejectGuiAsync();
        _nativeRejectApprove.Click+=async (_,_)=>await ApproveNativeRejectGuiAsync();
        _nativeRejectExecute.Click+=async (_,_)=>await ExecuteNativeRejectGuiAsync();
        _nativeRejectUndo.Click+=async (_,_)=>await UndoNativeRejectGuiAsync();
        side.Children.Add(new Border { Height=1, Background=Brushes.Gray, Margin=new Thickness(0,3,0,8) });
    }

    private void InitializeNativeRejectGui()
    {
        _nativeReject=null;_nativeRejectObservation=null;_nativeRejectInspectionError="";
        if(_sessionStore is null || !_moveFixtureEnabled || !NativeFileCapability.IsSupported)
        {
            if(_anyRejectHistory) _nativeRejectInspectionError =
                "Native Reject history requires its original marked generated fixture and a supported local filesystem. No replay, no Undo.";
            return;
        }
        try
        {
            _nativeReject=new DurableRejectCoordinator(_sessionStore);
            InspectNativeRejectGuiEvidence();
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _nativeReject=null;
            _nativeRejectInspectionError="Native Reject could not be safely inspected: "+ex.Message;
        }
    }

    private void InspectNativeRejectGuiEvidence()
    {
        _nativeRejectObservation=null;
        if(_nativeReject is null) return;
        try
        {
            _nativeRejectObservation=_nativeReject.Inspect();
            _nativeRejectInspectionError="";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _nativeRejectInspectionError="Native Reject evidence REFUSED: "+ex.Message;
        }
    }

    private string NativeRejectStartupSummary() =>
        _nativeRejectObservation is { } evidence
            ? $"Native Reject history: {evidence.State}; {evidence.Evidence}. " +
              (evidence.State=="Committed" && evidence.Evidence=="OWNED_ARCHIVE_VERIFIED"
                  ? "Verified fixture Undo is available. No automatic restore or replay."
                  : "Normal sorting locked. Inspect recovery journal; no automatic replay or cleanup.")
            : "Native Reject history requires manual preservation. " + _nativeRejectInspectionError;

    private string NativeRejectInspectionReport()
    {
        if(!_anyRejectHistory) return "";
        if (_ordinaryRejectProbe is not null) return OrdinaryRejectGuiProbeInspectionReport();
        if (_ordinaryFirstReject is not null) return OrdinaryFirstRejectRecoveryReport();
        var o=_nativeRejectObservation;
        return "\nNATIVE REJECT RECOVERY (separate SQLite journal, read-only; NO automatic replay)\n" +
            (o is null ? "EVIDENCE REFUSED: "+_nativeRejectInspectionError+"\n" :
            $"STATE: {o.State}\nEVIDENCE: {o.Evidence}\nORIGINAL: {o.Source}\nOWNED ARCHIVE: {o.Archive}\n") +
            "Preserve the original/source path, owned archive and workspace database. Do not manually move or delete files.\n";
    }

    // A stage is allowed only in the same marked generated fixture as P6D2A,
    // with one durable Reject-review mark and no other historical operation.
    private bool CanStageNativeRejectGui()
    {
        if(_busy || !_storeHealthy || _nativeReject is null || _sessionStore is null ||
           !_moveFixtureEnabled || _anyRejectHistory || _copyBarrier || _anyMoveHistory ||
           _anyRenameHistory || RenameLocksWrites || JournalNeedsAttention ||
           _work.PendingRejectCount!=1 || _work.AssignmentCount!=0 ||
           _sessionStore.AllRejectHistoryCount()!=0 || _sessionStore.AllMoveHistoryCount()!=0 ||
           _sessionStore.AllRenameHistoryCount()!=0 ||
           _sessionStore.CopyItems(_work.SessionId).Count!=0 || _work.HasAppliedCopies)
            return false;
        return true;
    }

    private DurableRejectItem? CurrentNativeRejectRow(string expectedState,string evidence)
    {
        if(_busy || !_storeHealthy || !_moveFixtureEnabled || _nativeReject is null ||
           _sessionStore is null || !_anyRejectHistory ||
           _nativeRejectObservation is not { } observed ||
           observed.State!=expectedState || observed.Evidence!=evidence) return null;
        try
        {
            var rows=_sessionStore.RejectItems();
            if(rows.Count is <1 or >2)return null;
            if(rows.Count==2 && (_sessionStore.InspectSecondRejectGuiSession(_work.CaptureSession()).Verified==false ||
                !_sessionStore.InspectRejectTerminalReceipts(1).Verified))return null;
            var current=rows[^1];
            if(current.SessionId!=_work.SessionId || current.State!=expectedState ||
               current.Id!=observed.Operation || current.PhotoId==Guid.Empty)return null;
            return current;
        }
        catch(Exception) {return null;}
    }

    private bool CanBeginSecondNativeRejectGui()
    {
        if(_busy || !_storeHealthy || !_moveFixtureEnabled || !_anyRejectHistory ||
           _nativeReject is null || _sessionStore is null || _copyBarrier ||
           _anyMoveHistory || _anyRenameHistory || JournalNeedsAttention || RenameLocksWrites ||
           _nativeRejectObservation is not {State:"Undone",Evidence:"RESTORED_SOURCE_VERIFIED"})
            return false;
        try
        {
            var rows=_sessionStore.RejectItems();
            return rows.Count==1 && rows[0].SessionId==_work.SessionId &&
                _sessionStore.InspectSecondRejectGuiSession(_work.CaptureSession()).Present==false;
        }
        catch(Exception){return false;}
    }

    private bool CanStageSecondNativeRejectGui()
    {
        if(_busy || !_storeHealthy || !_moveFixtureEnabled || !_anyRejectHistory ||
           _nativeReject is null || _sessionStore is null || _copyBarrier ||
           _anyMoveHistory || _anyRenameHistory || JournalNeedsAttention || RenameLocksWrites ||
           _work.PendingRejectCount!=1 || _work.AssignmentCount!=0 || _work.HasAppliedCopies)
            return false;
        try
        {
            var rows=_sessionStore.RejectItems();
            return rows.Count==1 && rows[0].State=="Undone" &&
                _sessionStore.InspectSecondRejectGuiSession(_work.CaptureSession()).Verified &&
                _sessionStore.InspectRejectTerminalReceipts(1).Verified &&
                _work.Photos.Count==1 && Path.GetFileName(_work.Photos[0].Path)=="photo_02.png" &&
                _work.IsPendingReject(_work.Photos[0].Id);
        }
        catch(Exception){return false;}
    }

    private void UpdateNativeRejectGuiButtons()
    {
        _beginSecondNativeReject.IsEnabled=CanBeginSecondNativeRejectGui();
        _stageSecondNativeReject.IsEnabled=CanStageSecondNativeRejectGui();
        _nativeRejectStage.IsEnabled=CanStageNativeRejectGui();
        _nativeRejectApprove.IsEnabled=CurrentNativeRejectRow("Staged","UNTOUCHED_SOURCE_VERIFIED") is not null;
        _nativeRejectExecute.IsEnabled=CurrentNativeRejectRow("Approved","UNTOUCHED_SOURCE_VERIFIED") is not null;
        _nativeRejectUndo.IsEnabled=CurrentNativeRejectRow("Committed","OWNED_ARCHIVE_VERIFIED") is not null;
        _nativeRejectStatus.Text=_nativeRejectObservation is { } item && item.State!="None"
            ? $"Native Reject: {item.State} · {item.Evidence}. No automatic replay. Regular file operations locked."
            : !string.IsNullOrEmpty(_nativeRejectInspectionError) ? _nativeRejectInspectionError
            : _nativeReject is null ? "Native Reject is disabled outside its marked, disposable fixture. No personal-photo relocation."
            : "Native Reject: no journal. Mark exactly one generated photo for Reject review before staging.";
    }

    private async Task<bool> ConfirmNativeRejectAsync(string title,string explanation,string acceptText)
    {
        var dlg=new Window { Title=title, Width=790,Height=430,MinWidth=600,MinHeight=330,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White };
        var shell=new Grid { RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(18) };
        shell.Children.Add(new TextBlock { Text=title,FontSize=19,FontWeight=FontWeight.SemiBold,
            TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,12) });
        var text=new TextBox { Text=explanation,IsReadOnly=true,AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch,MinHeight=180,
            Background=new SolidColorBrush(Color.Parse("#172839")),Foreground=Brushes.White };
        Grid.SetRow(text,1);shell.Children.Add(text);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Spacing=10,
            HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        buttons.Children.Add(Button("Cancel — preserve current state",()=>dlg.Close(false)));
        buttons.Children.Add(Button(acceptText,()=>dlg.Close(true)));
        Grid.SetRow(buttons,2);shell.Children.Add(buttons);dlg.Content=shell;
        return await dlg.ShowDialog<bool>(this);
    }

    // One explicit session transition, then a distinct separately acknowledged
    // stage. They never combine a checkpoint update with filesystem mutation.
    private async Task BeginSecondNativeRejectGuiAsync()
    {
        if(!CanBeginSecondNativeRejectGui() || _sessionStore is null)return;
        string text="TEST FIXTURE ONLY. First generated photo_01 has been independently " +
            "restored through native no-replace Undo.\n\nThis action will natively verify and SEAL " +
            "the first historical Reject, then atomically save a new session with ONLY " +
            "generated photo_02 marked for review. It NEVER moves a file, creates " +
            "a new native Reject Intent, approves a path, or authorizes execution.\n\n"+
            "All existing SQLite receipts and owned recovery markers remain intact. " +
            "Afterward, separately Stage → Review → Approve → Execute → Undo.";
        if(!await ConfirmNativeRejectAsync("Begin SECOND generated Reject review — no move",text,
            "BEGIN SECOND REVIEW SESSION (NO FILE MOVE)"))return;
        try
        {
            if(!CanBeginSecondNativeRejectGui())
                throw new IOException("First terminal Reject evidence changed during dialog.");
            _busy=true;Refresh();
            var next=await Task.Run(()=>_sessionStore.BeginSecondRejectGuiSession(
                _work.CaptureSession(),explicitlyConfirmed:true));
            // SQLite may already have committed if any GUI work fails after this.
            // On uncertainty force a restart, never silently retry the transition.
            _work.RestoreSession(next);
            if(!_sessionStore.InspectSecondRejectGuiSession(_work.CaptureSession()).Verified)
                throw new IOException("Second saved GUI checkpoint is not independently verified.");
            _work.RevokeApproval();_modeGate.Revoke();_prepared=null;
            _hasReviewed=false;_reviewedMoveId=null;_requestedPhotoId=null;
            InspectNativeRejectGuiEvidence();
            Message("SECOND GENERATED REJECT REVIEW SESSION SAVED. " +
                "photo_02 marked for review; original unchanged, prior Reject history sealed. " +
                "Stage separately. No file operation authorized.");
        }
        catch(Exception ex)
        {
            _storeHealthy=false;
            Message("SECOND REJECT SESSION TRANSITION REFUSED / UNCERTAIN. " +
                "NO RETRY. Restart/read-only; preserve SQLite and files. "+ex.Message);
        }
        finally{_busy=false;Refresh();}
    }

    private async Task StageSecondNativeRejectGuiAsync()
    {
        if(!CanStageSecondNativeRejectGui() || _nativeReject is null || _sessionStore is null)return;
        var photo=_work.Photos.Single();
        string text="TEST FIXTURE ONLY. SEPARATE successor stage.\n\n"+
            "GENERATED ORIGINAL: "+photo.Path+"\n\n"+
            "The previous Reject is natively restored and terminal-sealed. " +
            "This independent consent creates ONE new journal proposal using a single-use " +
            "receipt-bound grant. No file moves. Next steps require exact-path approval " +
            "and a SECOND confirmation to execute.";
        if(!await ConfirmNativeRejectAsync("Stage SECOND native Reject (NO FILE MOVE)",text,
            "ACKNOWLEDGE SECOND STAGE (NO MOVE)"))return;
        try
        {
            if(!CanStageSecondNativeRejectGui())
                throw new IOException("Second fixture/receipt changed during dialog.");
            _busy=true;Refresh();
            var staged=await Task.Run(()=>_nativeReject.StageSuccessor(
                _work,photo.Id,acknowledgeNewEpoch:true));
            _anyRejectHistory=true;InspectNativeRejectGuiEvidence();
            if(_nativeRejectObservation is not {State:"Staged",Evidence:"UNTOUCHED_SOURCE_VERIFIED"} ||
               !_sessionStore.InspectSecondRejectGuiSession(_work.CaptureSession()).Verified)
                throw new IOException("Second native staged evidence is uncertain.");
            Message("SECOND NATIVE REJECT STAGED: photo_02 ORIGINAL UNTOUCHED. " +
                "Explicit exact-path approval and Execute remain separate. Owned path: "+staged.Archive);
        }
        catch(Exception ex)
        {
            Message("SECOND REJECT STAGING REFUSED / UNCERTAIN. NO RETRY. " +
                "Preserve SQLite journal and fixture. "+ex.Message);
        }
        finally{InspectNativeRejectGuiEvidence();_busy=false;Refresh();}
    }

    private async Task StageNativeRejectGuiAsync()
    {
        if(!CanStageNativeRejectGui() || _nativeReject is null || _sessionStore is null) return;
        var candidates=_work.Photos.Where(photo=>_work.IsPendingReject(photo.Id)).ToArray();
        if(candidates.Length!=1) { Message("Native Reject requires precisely one marked generated photo.");return; }
        var photo=candidates[0];
        try {P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);}
        catch(Exception ex) {Message("Fixture verification refused: "+ex.Message);return;}
        if(!await ConfirmNativeRejectAsync("Stage ONE generated photo for native Reject review",
            "TEST FIXTURE ONLY — no personal photographs.\n\n"+
            "ORIGINAL: "+photo.Path+"\n\n"+
            "Staging creates a permanent SQLite proposal, but does NOT relocate or delete a file. " +
            "The next screen will reveal an exact uniquely owned recovery path. " +
            "After staging, all ordinary sorting actions are locked for this fixture.\n"+
            "No automatic retries or cleanup are permitted.","Stage proposal (no file move)")) return;
        try
        {
            _busy=true;Refresh();
            var staged=await Task.Run(()=>_nativeReject.Stage(_work,photo.Id));
            _anyRejectHistory=true;InspectNativeRejectGuiEvidence();
            Message("NATIVE REJECT STAGED: original unchanged. Open 'Review & authorize exact paths'; no file move permitted yet. Recovery path: "+staged.Archive);
        }
        catch(Exception ex) {Message("Native Reject staging REFUSED: "+ex.Message+" Preserve the fixture and SQLite journal.");}
        finally
        {
            if(_sessionStore is not null) {try{_anyRejectHistory=_sessionStore.AllRejectHistoryCount()!=0;}catch(Exception){_storeHealthy=false;}}
            InspectNativeRejectGuiEvidence();_busy=false;Refresh();
        }
    }

    private async Task ApproveNativeRejectGuiAsync()
    {
        var staged=CurrentNativeRejectRow("Staged","UNTOUCHED_SOURCE_VERIFIED");
        if(staged is null || _nativeReject is null) return;
        string details="NO FILE MOVE DURING APPROVAL.\n\nGENERATED ORIGINAL:\n"+staged.Source+
            "\n\nEXACT OWNED RECOVERY DESTINATION:\n"+staged.Archive+
            "\n\nEXPECTED SHA-256:\n"+staged.SourceHash+
            "\n\nThis is one single-use grant tied to the exact original native identity, file bytes, session and destinations. " +
            "Copy/Move/Rename approvals do not authorize Reject. Next, a SECOND deliberate Execute action will be required.";
        if(!await ConfirmNativeRejectAsync("Review exact Reject paths and authorize",details,
            "AUTHORIZE these exact paths (no move)"))return;
        try
        {
            if(CurrentNativeRejectRow("Staged","UNTOUCHED_SOURCE_VERIFIED")!=staged)
                throw new IOException("Reject staging changed during approval dialog.");
            _busy=true;Refresh();
            await Task.Run(()=>_nativeReject.Approve(staged,true));
            InspectNativeRejectGuiEvidence();
            Message("REJECT APPROVED: exact source and owned archive bound, no file moved. Use Execute only if you intend a native relocation.");
        }
        catch(Exception ex){Message("Reject approval REFUSED: "+ex.Message+" Preserve existing records.");}
        finally{InspectNativeRejectGuiEvidence();_busy=false;Refresh();}
    }

    private async Task ExecuteNativeRejectGuiAsync()
    {
        var approved=CurrentNativeRejectRow("Approved","UNTOUCHED_SOURCE_VERIFIED");
        if(approved is null || _nativeReject is null) return;
        var details="THIS ACTION MOVES A GENERATED TEST ORIGINAL INTO OWNED RECOVERY (NO DELETE/TRASH).\n\n"+
            "ORIGINAL PATH (will be empty after success):\n"+approved.Source+
            "\n\nRECOVERY ARCHIVE (retains exact original):\n"+approved.Archive+
            "\n\nSHA-256:\n"+approved.SourceHash+
            "\n\nThe operation journal is committed before any native relocation. " +
            "On any uncertain outcome, the app will STOP, not replay or auto-restore. " +
            "After verified commit, restart and use the separate native Reject Undo button.";
        if(!await ConfirmNativeRejectAsync("EXECUTE native Reject — generated fixture ONLY",details,
            "RELOCATE THIS GENERATED PHOTO"))return;
        try
        {
            if(CurrentNativeRejectRow("Approved","UNTOUCHED_SOURCE_VERIFIED")!=approved)
                throw new IOException("Approved Reject changed during confirmation dialog.");
            _busy=true;Refresh();
            await Task.Run(()=>_nativeReject.Execute(approved));
            InspectNativeRejectGuiEvidence();
            if(_nativeRejectObservation is not {State:"Committed",Evidence:"OWNED_ARCHIVE_VERIFIED"})
                throw new IOException("Native Reject returned without independently proven committed recovery evidence.");
            Message("NATIVE REJECT COMMITTED: original retained in verified owned archive. Close and restart to verify durable Undo. Normal sorting stays locked.");
        }
        catch(Exception ex) {Message("NATIVE REJECT REFUSED / UNCERTAIN. NO RETRY. Preserve original, owned archive and SQLite journal: "+ex.Message);}
        finally{InspectNativeRejectGuiEvidence();_busy=false;Refresh();}
    }

    private async Task UndoNativeRejectGuiAsync()
    {
        var committed=CurrentNativeRejectRow("Committed","OWNED_ARCHIVE_VERIFIED");
        if(committed is null || _nativeReject is null) return;
        var details="RESTORE EXACT GENERATED ORIGINAL. No overwrite is permitted.\n\n"+
            "RECOVERY ARCHIVE:\n"+committed.Archive+"\n\nRESTORE TO ORIGINAL PATH:\n"+committed.Source+
            "\n\nThe app re-verifies the owned archive/native identity and requires the original path to be unoccupied. " +
            "The operation stays in the SQLite history forever. Undo cannot be repeated.";
        if(!await ConfirmNativeRejectAsync("Undo native Reject — restore generated original",details,
            "RESTORE VERIFIED ORIGINAL (once)"))return;
        try
        {
            if(CurrentNativeRejectRow("Committed","OWNED_ARCHIVE_VERIFIED")!=committed)
                throw new IOException("Reject archive evidence changed during Undo confirmation dialog.");
            _busy=true;Refresh();
            await Task.Run(()=>_nativeReject.Undo(committed.SessionId));
            InspectNativeRejectGuiEvidence();
            if(_nativeRejectObservation is not {State:"Undone",Evidence:"RESTORED_SOURCE_VERIFIED"})
                throw new IOException("Undo did not produce verified restored native original evidence.");
            Message("NATIVE REJECT UNDONE: original restored without replacement. Separate SQLite history and owned markers retained. Fixture remains closed to additional file operations.");
        }
        catch(Exception ex) {Message("NATIVE REJECT UNDO REFUSED / UNCERTAIN. NO RETRY. Preserve paths, owned archive and journal: "+ex.Message);}
        finally{InspectNativeRejectGuiEvidence();_busy=false;Refresh();}
    }
}
