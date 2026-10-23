using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;

namespace PhotoSiftReborn;

// Contained, read-only exact Rename presentation. The button is NOT a native
// grant: MainWindow must still recheck the frozen proposal and explicitly
// preserve the separate Apply click/engine validation. No file/store APIs.
internal sealed class StudioRenameExactView : UserControl
{
    internal event EventHandler? ApproveRequested;
    internal event EventHandler? DeclineRequested;
    private readonly Button _cancel = new() { Content="Cancel · original unchanged", MinHeight=42 };
    private readonly TextBlock _copyStatus = new() { Foreground=StudioPalette.Secondary,
        TextWrapping=TextWrapping.Wrap };
    private readonly string _exact;

    internal StudioRenameExactView(RenameProposal p)
    {
        Background = StudioPalette.Shell;
        _exact = "ORIGINAL (same directory):\n" + p.Source +
            "\n\nPROPOSED NEW NAME (no file changed yet):\n" + p.Target +
            "\n\nStable photograph identity: " + p.PhotoId +
            "\nNative original identity: " + p.NativeIdentity +
            "\nOriginal SHA-256: " + p.Hash +
            "\nBytes: " + p.Length + " · Imported write ticks: " + p.WriteTicks;
        var body = new Grid { Margin=new Thickness(18),
            RowDefinitions=new RowDefinitions("Auto,*,Auto") };
        var heading=new StackPanel { Spacing=7,Margin=new Thickness(0,0,0,10) };
        heading.Children.Add(new TextBlock { Text="Review this Rename",FontSize=24,
            FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text });
        heading.Children.Add(new TextBlock { Text="One ordinary local photograph · exact native paths. " +
            "Approval does not rename the file. A separate Apply remains required.",
            TextWrapping=TextWrapping.Wrap,Foreground=StudioPalette.Secondary });
        body.Children.Add(heading);
        var scroller = new ScrollViewer { VerticalScrollBarVisibility= Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var details=new StackPanel { Spacing=9 };
        var box=new TextBox { Text=_exact,IsReadOnly=true,AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,MinHeight=190,Foreground=StudioPalette.Text,
            Background=StudioPalette.Surface };
        details.Children.Add(box);
        var copy=new Button { Content="Copy these exact details",MinHeight=37,
            HorizontalAlignment=HorizontalAlignment.Left };
        copy.Click += async (_,_) =>
        {
            try
            {
                var clipboard=TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null) throw new InvalidOperationException("Clipboard unavailable.");
                await clipboard.SetTextAsync(_exact);
                _copyStatus.Text="Exact details copied on request.";
            }
            catch (Exception ex) { _copyStatus.Text="Clipboard unavailable: " + ex.Message; }
        };
        details.Children.Add(copy);
        details.Children.Add(_copyStatus);
        scroller.Content=details;
        Grid.SetRow(scroller,1); body.Children.Add(scroller);
        var footer=new StackPanel { Spacing=10,Margin=new Thickness(0,12,0,0) };
        footer.Children.Add(new TextBlock { Text="Native no-replace Rename; durable journal before mutation. " +
            "Undo is separately verified. Never automatically retry an interrupted operation.",
            TextWrapping=TextWrapping.Wrap,Foreground=StudioPalette.Secondary });
        var acknowledged=new CheckBox { Content="I checked the exact original and proposed new path.",
            IsChecked=false };
        footer.Children.Add(acknowledged);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,Spacing=9,
            HorizontalAlignment=HorizontalAlignment.Right };
        var approve=new Button { Content="Approve this ONE Rename · Apply separately",
            Background=StudioPalette.Jade,Foreground=StudioPalette.JadeInk,
            FontWeight=FontWeight.SemiBold,MinHeight=42,IsEnabled=false };
        acknowledged.Click += (_,_)=>approve.IsEnabled=acknowledged.IsChecked==true;
        _cancel.Click += (_,_)=>DeclineRequested?.Invoke(this,EventArgs.Empty);
        approve.Click += (_,_)=>ApproveRequested?.Invoke(this,EventArgs.Empty);
        buttons.Children.Add(_cancel); buttons.Children.Add(approve);
        footer.Children.Add(buttons);
        Grid.SetRow(footer,2); body.Children.Add(footer);
        Content=body;
    }
    internal void FocusCancel() => _cancel.Focus();
}
