using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

public sealed partial class MainWindow
{
    private async Task ResumeAfterVerifiedCopyUndoAsync()
    {
        var store=_sessionStore;
        if (_busy || !_storeHealthy || _copyBarrier || store is null ||
            _journal is null || _anyMoveHistory || _anyRejectHistory ||
            _anyRenameHistory || JournalNeedsAttention || _work.HasAppliedCopies) return;
        var captured=_work.CaptureSession();
        var consent=new Window { Title="Resume after verified Copy Undo — no file changes",
            Width=760,Height=305,MinWidth=570,MinHeight=280,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White };
        var body=new StackPanel { Margin=new Avalonia.Thickness(19),Spacing=14 };
        body.Children.Add(new TextBlock { Text="Verify completed Copy history before continuing?",
            FontSize=20,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap });
        body.Children.Add(Muted("Read-only: checks EVERY historical Copy row, original and archived native file identity, SHA-256, recovery marker, empty destinations and saved session. " +
            "No old journal row or archive will be deleted. This does NOT authorize Move, Rename or Reject, and never reuses a previous Copy approval."));
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8,
            HorizontalAlignment=HorizontalAlignment.Right };
        actions.Children.Add(Button("Cancel — remain locked",()=>consent.Close(false)));
        actions.Children.Add(Button("VERIFY & RESUME SAVED CHOICES (NO FILE MOVE)",()=>consent.Close(true)));
        body.Children.Add(actions);consent.Content=body;
        if (!await consent.ShowDialog<bool>(this)) return;
        try
        {
            _busy=true;Refresh();
            var proof=await Task.Run(()=>CopyUndoContinuation.Inspect(store,_work));
            if (!proof.Verified || _work.SessionId!=captured.SessionId ||
                _work.Revision!=captured.Revision)
                throw new IOException("Fresh Copy terminal proof refused or session changed. "+proof.Reason);
            _prepared=null;_hasReviewed=false;_reviewedMoveId=null;
            _work.RevokeApproval();
            _copyContinuationApproved=true; // ephemeral, never persisted
            Message($"Verified {proof.HistoricalRows} fully undone copies and preserved recovery history. Saved choices can be revised. New Copy requires fresh exact-path approval. No file operations performed.");
        }
        catch(Exception ex)
        { _copyContinuationApproved=false; Message("Resume refused: "+ex.Message); }
        finally { _busy=false;Refresh(); }
    }
}
