using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

// Selectable, phase-specific durable Reject details only; NEVER touches files/SQLite.
internal sealed class StudioRejectExactView : UserControl
{
    internal event EventHandler? ApproveRequested;
    internal event EventHandler? DeclineRequested;
    private readonly Button _cancel=new(){Content="Cancel · preserve current state",MinHeight=42};
    private readonly TextBlock _copyStatus=new(){Foreground=StudioPalette.Secondary,TextWrapping=TextWrapping.Wrap};
    private readonly string _exact;

    internal StudioRejectExactView(DurableRejectItem row, StudioRejectPhase phase)
    {
        Background=StudioPalette.Shell;
        var (title,description,acknowledgment,yesText) = phase switch
        {
            StudioRejectPhase.ApproveStaged => (
                "Review the durable Reject proposal",
                "STAGED — original untouched. This grants approval ONLY; Execute remains separate.",
                "I checked this exact original and the owned recovery location. Approval alone changes no file.",
                "Approve this staged Reject · no relocation"),
            StudioRejectPhase.ExecuteApproved => (
                "Relocate the approved original",
                "THIS CHANGES THE ORIGINAL FILE LOCATION. The original moves into owned same-volume recovery; never Trash or deletion. No automatic retry.",
                "I understand Execute relocates this ONE approved original and preserves the recovery archive.",
                "EXECUTE · relocate original to recovery"),
            StudioRejectPhase.RestoreCommitted => (
                "Restore from verified owned recovery",
                "UNDO restores the original by native no-replace operation only if the original path is vacant and owned evidence remains valid. No retry.",
                "I checked the verified owned archive and exact original restoration path.",
                "UNDO · restore verified original"),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        _exact="REJECT PHASE: "+phase+"\nDURABLE STATE: "+row.State+
            "\n\nORIGINAL PATH:\n"+row.Source+"\n\nOWNED RECOVERY PATH:\n"+row.Archive+
            "\n\nORIGINAL SHA-256:\n"+row.SourceHash+
            "\n\nNATIVE IDENTITY: "+row.SourceIdentity+
            "\nSTABLE PHOTO ID: "+row.PhotoId+"\nSESSION ID: "+row.SessionId+
            "\nDURABLE OPERATION ID: "+row.Id+
            "\nORIGINAL BYTES: "+row.SourceLength+"\nWRITE TICKS: "+row.SourceWriteTicks;
        var body=new Grid{Margin=new Thickness(18),RowDefinitions=new RowDefinitions("Auto,*,Auto")};
        var heading=new StackPanel{Spacing=8,Margin=new Thickness(0,0,0,12)};
        heading.Children.Add(new TextBlock{Text=title,FontSize=24,FontWeight=FontWeight.SemiBold,
            Foreground=StudioPalette.Text,TextWrapping=TextWrapping.Wrap});
        heading.Children.Add(new TextBlock{Text=description,Foreground=StudioPalette.Secondary,
            TextWrapping=TextWrapping.Wrap});
        body.Children.Add(heading);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var details=new StackPanel{Spacing=9};
        details.Children.Add(new TextBox{Text=_exact,IsReadOnly=true,AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,MinHeight=160,Foreground=StudioPalette.Text,Background=StudioPalette.Surface});
        var copy=new Button{Content="Copy these exact paths and identities",MinHeight=36,
            HorizontalAlignment=HorizontalAlignment.Left};
        copy.Click+=async (_,_)=>
        {
            try
            {
                var clipboard=TopLevel.GetTopLevel(this)?.Clipboard;
                if(clipboard is null)throw new InvalidOperationException("Clipboard unavailable.");
                await clipboard.SetTextAsync(_exact);
                _copyStatus.Text="Exact details copied on request.";
            }
            catch(Exception ex){_copyStatus.Text="Clipboard unavailable: "+ex.Message;}
        };
        details.Children.Add(copy);details.Children.Add(_copyStatus);scroll.Content=details;
        Grid.SetRow(scroll,1);body.Children.Add(scroll);
        var footer=new StackPanel{Spacing=10,Margin=new Thickness(0,12,0,0)};
        footer.Children.Add(new TextBlock{Text="Historical journal and owned recovery must remain intact. " +
            "A recorded status is NOT permission to bypass native verification.",
            Foreground=StudioPalette.Secondary,TextWrapping=TextWrapping.Wrap});
        var check=new CheckBox{Content=acknowledgment,IsChecked=false};footer.Children.Add(check);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,
            HorizontalAlignment=HorizontalAlignment.Right};
        var yes=new Button{Content=yesText,MinHeight=42,IsEnabled=false,
            Background=phase==StudioRejectPhase.ExecuteApproved
                ? new SolidColorBrush(Color.Parse("#983B2F")) : StudioPalette.Jade,
            Foreground=phase==StudioRejectPhase.ExecuteApproved
                ? Brushes.White : StudioPalette.JadeInk};
        check.Click+=(_,_)=>yes.IsEnabled=check.IsChecked==true;
        _cancel.Click+=(_,_)=>DeclineRequested?.Invoke(this,EventArgs.Empty);
        yes.Click+=(_,_)=>ApproveRequested?.Invoke(this,EventArgs.Empty);
        buttons.Children.Add(_cancel);buttons.Children.Add(yes);footer.Children.Add(buttons);
        Grid.SetRow(footer,2);body.Children.Add(footer);Content=body;
    }
    internal void FocusCancel()=>_cancel.Focus();
}
