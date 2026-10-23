using Avalonia.Controls;
using Avalonia.Media;

namespace PhotoSiftReborn;

/// <summary>Exact THIRD generated-photo GUI lane. Does not alter ordinary
/// Copy, legacy Move or second-Move authorization gates.</summary>
public sealed partial class MainWindow
{
    private bool _boundedThirdGui;
    private DurableMoveItem? _approvedThirdTicket;
    private PreparedCopy? _approvedThirdPreparation;
    private readonly Button _beginThirdGui = new() {
        Content="Finish SECOND verified Undo → begin THIRD TEST Move",MinHeight=44};
    private readonly Button _stageThirdGui = new() {
        Content="Stage photo_03.png → Family (fixture only)",MinHeight=42};
    private readonly Button _reviewThirdGui = new() {
        Content="Review & authorize THIRD test Move",MinHeight=42};
    private readonly Button _executeThirdGui = new() {
        Content="EXECUTE APPROVED THIRD TEST MOVE — relocates original",MinHeight=45,
        Background=new SolidColorBrush(Color.Parse("#983B2F")),
        Foreground=Brushes.White,FontWeight=FontWeight.Bold};

    private bool ThirdReadyNoMoveRows()
    {
        if(!_boundedThirdGui || !_storeHealthy || !_moveFixtureEnabled ||
            _fixtureMover is null || _sessionStore is null || _copyBarrier) return false;
        try
        {
            return _sessionStore.AllMoveHistoryCount()==2 &&
                _sessionStore.MoveItems(_work.SessionId).Count==0 &&
                _sessionStore.InspectThirdGuiSession(_work.CaptureSession()).Verified;
        }
        catch(Exception) {return false;}
    }

    private async Task BeginThirdFixtureGuiAsync()
    {
        if(_busy || !_storeHealthy || !_boundedSuccessorGui || _boundedThirdGui ||
            _sessionStore is null || !_moveFixtureEnabled || _copyBarrier) return;
        try
        {
            _busy=true;
            var db=_sessionStore;
            P5CGuiFixturePolicy.VerifyExactWorkspace(db,_work);
            var prev=db.MoveItems(_work.SessionId);
            if(db.AllMoveHistoryCount()!=2 || prev.Count!=1 ||
                prev[0].State!="Undone" || !db.InspectGuiSuccessor(_work.CaptureSession()).Verified)
                throw new IOException("The second Move has not completed verified Undo.");
            var accepted=await ShowBoundedNextMoveConfirmAsync(
                "First and second Move journals and their native Undo archives must stay intact. " +
                "An atomic NEW saved session will contain only generated photo_03.png. " +
                "This session change DOES NOT approve or execute any file Move. " +
                "You must separately review source, target and archives and approve a single-use third grant.");
            if(!accepted) {Message("Third fixture session canceled; all history preserved.");return;}
            var next=await Task.Run(()=>db.BeginThirdGuiSession(_work.CaptureSession()));
            _work.RestoreSession(next);
            _boundedSuccessorGui=false;
            _boundedThirdGui=db.InspectThirdGuiSession(_work.CaptureSession()).Verified;
            if(!_boundedThirdGui) throw new IOException("Third transition committed but native inspection uncertain; restart read-only.");
            _approvedThirdTicket=null;_approvedThirdPreparation=null;
            _approvedSuccessorTicket=null;_approvedSuccessorPreparation=null;
            _modeGate.Revoke();_work.RevokeApproval();_prepared=null;
            _hasReviewed=false;_reviewedMoveId=null;_requestedPhotoId=null;
            Message("THIRD TEST SESSION SAVED. Earlier journals and archives preserved. Stage generated photo_03.png, then independently review and authorize.");
        }
        catch(Exception ex)
        {
            // Atomic SQLite commit could precede an observed UI error. Never
            // guess which session owns the checkpoint; restart/read-only.
            _storeHealthy=false;
            Message("THIRD SESSION TRANSITION FAILED OR UNCERTAIN. Preserve files; restart and inspect. "+ex.Message);
        }
        finally {_busy=false;Refresh();}
    }

    private async Task StageThirdFixtureGuiAsync()
    {
        if(_busy || !ThirdReadyNoMoveRows() || _work.AssignmentCount!=0 ||
            _work.Current is not { } photo || Path.GetFileName(photo.Path)!="photo_03.png") return;
        try
        {
            _busy=true;
            _work.Assign('F');
            await SaveSessionAsync();
            Message("STAGED: generated photo_03.png → Family. No files written. Review and authorize separately.");
        }
        catch(Exception ex) {Message("Third fixture staging refused: "+ex.Message);}
        finally {_busy=false;Refresh();}
    }

    private async Task ReviewThirdFixtureGuiAsync()
    {
        if(_busy || !ThirdReadyNoMoveRows() || _work.AssignmentCount!=1 ||
            _sessionStore is null || _fixtureMover is null) return;
        try
        {
            _busy=true;
            var db=_sessionStore;var mover=_fixtureMover;
            P5CGuiFixturePolicy.VerifyExactWorkspace(db,_work);
            _work.RevokeApproval();
            if(_work.CollisionMode!=CollisionChoice.Refuse)
                throw new IOException("Third Move requires Refuse collision policy.");
            var entries=_work.ReviewPlan();
            if(entries.Count!=1 || _work.SkippedReview.Count!=0 ||
                Path.GetFileName(entries[0].Source)!="photo_03.png" ||
                entries[0].Key!='F' ||
                !SortingWorkspace.PathComparer.Equals(entries[0].Destination,
                    Path.Combine(_work.DestinationRoot!,"Family","photo_03.png")))
                throw new IOException("Only generated photo_03.png → output/Family may enter the third epoch.");
            var id=_work.CurrentPlanId ?? throw new IOException("Third exact review plan missing.");
            var revision=_work.Revision;
            var preparation=(await DurableCopyCoordinator.PrepareAsync(_work,entries,id,revision)).Single();
            var ticket=MakeGuiSuccessorTicket(preparation,Guid.NewGuid());
            bool reviewed=await ShowExactPathsDialogAsync(entries,true,ticket.Id);
            if(!reviewed) {Message("Third exact-path review canceled; no authority granted.");return;}
            bool acknowledged=await ShowBoundedNextMoveConfirmAsync(
                "THIRD GENERATED FIXTURE MOVE — ORIGINAL RELOCATED\n"+
                "SOURCE: "+ticket.Source+"\nDESTINATION: "+ticket.Destination+
                "\nOPERATION: "+ticket.Id+"\n"+db.ThirdGuiPredecessorSummary()+
                "\nOriginal recovery: "+ticket.OriginalArchive+
                "\nInstalled-output Undo archive: "+ticket.OutputArchive+
                "\nONE exact Move only; prior history immutable. NO personal photos. NO automatic retry.");
            if(!acknowledged) {Message("Third explicit authorization canceled; nothing moved.");return;}
            if(!ThirdReadyNoMoveRows() || _work.Revision!=revision)
                throw new IOException("Third session or plan changed during exact review.");
            _work.ApprovePlan(id,revision);
            var stage=await Task.Run(()=>db.StageAdditionalEpochAuthorization(ticket));
            if(!stage.Verified || stage.Approved || stage.Consumed || stage.Epoch!=3)
                throw new IOException("Third immutable ticket failed native staged inspection.");
            var prepared=await mover.PrepareApprovedAdditionalEpochAsync(_work,entries,ticket);
            mover.ApproveAdditionalEpoch(_work,prepared,ticket,true);
            if(!db.InspectAdditionalEpochAuthorization(ticket).Approved)
                throw new IOException("The exact third single-use authorization did not persist.");
            _approvedThirdTicket=ticket;
            _approvedThirdPreparation=prepared;
            Message("THIRD ONE-USE MOVE GRANTED. Exact photo_03 original, destination and two terminal predecessor receipts bound in SQLite. Execute only once.");
        }
        catch(Exception ex)
        {
            _approvedThirdTicket=null;_approvedThirdPreparation=null;
            _work.RevokeApproval();
            Message("Third Move review REFUSED. Do not reset an approved or staged ticket; inspect history. "+ex.Message);
        }
        finally {_busy=false;Refresh();}
    }

    private async Task ExecuteThirdFixtureGuiAsync()
    {
        if(_busy || !ThirdReadyNoMoveRows() || _fixtureMover is null || _sessionStore is null ||
            _approvedThirdTicket is null || _approvedThirdPreparation is null ||
            !_work.IsCurrentPlanApproved) return;
        try
        {
            _busy=true;
            var ticket=_approvedThirdTicket;
            var prepared=_approvedThirdPreparation;
            P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);
            var evidence=_sessionStore.InspectAdditionalEpochAuthorization(ticket);
            if(!evidence.Verified || !evidence.Approved || evidence.Consumed || evidence.Epoch!=3 ||
                ticket.SessionId!=_work.SessionId || prepared.PhotoId!=ticket.PhotoId)
                throw new IOException("Approved third Move grant is stale or missing.");
            Message("MOVING VERIFIED GENERATED photo_03 ORIGINAL — atomic third grant/Intent, no automatic retry.");
            Refresh();
            var moved=await _fixtureMover.ExecuteApprovedAdditionalEpochAsync(_work,prepared,ticket);
            _work.RetireCommitted(new[]{moved.PhotoId});
            _approvedThirdTicket=null;_approvedThirdPreparation=null;
            _work.RevokeApproval();_hasReviewed=false;
            await SaveSessionAsync();
            _requestedPhotoId=null;
            Message("THIRD TEST FIXTURE MOVE COMMITTED. Native original retained in owned recovery: "+
                moved.OriginalArchive+". Close/restart and Undo verified Move. No personal-photo Move.");
        }
        catch(Exception ex)
        {
            _approvedThirdTicket=null;_approvedThirdPreparation=null;
            _work.RevokeApproval();
            Message("THIRD MOVE UNCERTAIN OR REFUSED. NO RETRY. Preserve all sources, outputs and recovery archives: "+ex.Message);
        }
        finally {_busy=false;Refresh();}
    }
}
