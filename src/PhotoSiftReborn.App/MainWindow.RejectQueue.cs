using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

/// <summary>
/// P6E1: real multi-photo triage, still CHOICE ONLY. Must never create a native
/// Reject journal, run filesystem relocation, or bypass history/copy barriers.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record RejectQueueSelection(Guid PhotoId,bool RemoveMark);

    private async Task ReviewRejectQueueAsync()
    {
        if(!CanStageRejectReview() || _work.PendingRejectCount==0) return;
        var queued=_work.PendingRejectReviewPhotos.ToArray();
        var ids=queued.Select(p=>p.Id).ToArray();
        var session=_work.SessionId;
        var entries=queued.Select((p,i)=>
            $"{i+1}. {System.IO.Path.GetFileName(p.Path)}  —  {p.Path}").ToArray();
        var dlg=new Window
        {
            Title="Review photographs marked for Reject — no file changes",
            Width=790,Height=480,MinWidth=540,MinHeight=330,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")),
            Foreground=Brushes.White
        };
        var panel=new Grid {Margin=new Thickness(19),RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto")};
        panel.Children.Add(new TextBlock {Text=$"Reject-review queue · {queued.Length} photograph(s)",
            FontSize=21,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,10)});
        var expl=Muted("Select a marked photograph. Show it in the viewer, or clear ONLY its reversible review mark. Originals are not moved or deleted. Native Reject approval is separate.");
        Grid.SetRow(expl,1);panel.Children.Add(expl);
        var list=new ListBox {ItemsSource=entries,SelectedIndex=0,
            HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,
            Margin=new Thickness(0,10,0,10)};
        Grid.SetRow(list,2);panel.Children.Add(list);
        var actions=new WrapPanel {Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=Button("Close",()=>dlg.Close((RejectQueueSelection?)null));
        var show=Button("Show selected photograph",()=>
        {
            if(list.SelectedIndex is int index && index>=0 && index<ids.Length)
                dlg.Close(new RejectQueueSelection(ids[index],false));
        });
        var remove=Button("Unmark selected (no file move)",()=>
        {
            if(list.SelectedIndex is int index && index>=0 && index<ids.Length)
                dlg.Close(new RejectQueueSelection(ids[index],true));
        });
        foreach(var button in new[]{cancel,show,remove})
        {
            button.Margin=new Thickness(5,0,0,0);actions.Children.Add(button);
        }
        Grid.SetRow(actions,3);panel.Children.Add(actions);
        dlg.Content=panel;
        var selected=await dlg.ShowDialog<RejectQueueSelection?>(this);
        if(selected is null) return;
        // Never act on stale dialogs after native history/session/queue changes.
        if(!CanStageRejectReview() || _work.SessionId!=session ||
           !_work.PendingRejectReviewPhotos.Select(p=>p.Id).SequenceEqual(ids) ||
           !ids.Contains(selected.PhotoId))
        {
            Message("Reject-review selection became stale; no changes made.");return;
        }
        if(selected.RemoveMark)
        {
            var original=queued.Single(p=>p.Id==selected.PhotoId);
            var confirm=new Window {Title="Unmark selected Reject-review photograph — no file move",
                Width=610,Height=240,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White};
            var body=new StackPanel {Margin=new Thickness(20),Spacing=14};
            body.Children.Add(new TextBlock {Text="Remove only this Reject-review mark?",
                FontSize=19,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap});
            body.Children.Add(Muted(original.Path+"\n\nThe original photograph stays unchanged. Other marked photographs stay marked."));
            var confirmActions=new WrapPanel {Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right};
            foreach(var button in new[]{Button("Keep mark",()=>confirm.Close(false)),
                    Button("Unmark selected photograph ONLY",()=>confirm.Close(true))})
            {button.Margin=new Thickness(5,0,0,0);confirmActions.Children.Add(button);}
            body.Children.Add(confirmActions);confirm.Content=body;
            if(!await confirm.ShowDialog<bool>(this)) return;
        }
        try
        {
            if(!CanStageRejectReview() || _work.SessionId!=session ||
               !_work.PendingRejectReviewPhotos.Select(p=>p.Id).SequenceEqual(ids))
                throw new IOException("Queue/session changed while reviewing. No selection applied.");
            _busy=true;
            if(selected.RemoveMark)
            {
                _work.UnmarkRejectReview(selected.PhotoId);
                _prepared=null;_hasReviewed=false;
            }
            else
            {
                var photoIndex=_work.Photos.ToList().FindIndex(p=>p.Id==selected.PhotoId);
                if(photoIndex<0) throw new IOException("Selected photograph disappeared from session.");
                _work.JumpToPhoto(photoIndex+1);
            }
            await SaveSessionAsync();
            Message(selected.RemoveMark
                ? "Selected Reject-review mark cleared and saved; other marks remain. No files moved."
                : "Showing selected Reject-review photograph. Original file unchanged.");
        }
        catch(Exception ex){Message("Reject-review queue action refused: "+ex.Message);}
        finally{_busy=false;Refresh();}
    }
}
