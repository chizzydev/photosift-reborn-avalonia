using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

// Reusable view with NO coordinator, store, workspace or native operation API.
// M1 hosts it in the existing owned Window. M2 may embed exactly this component
// ONLY after command/keyboard/navigation containment is separately accepted.
internal sealed class StudioExactReviewView : UserControl
{
    private readonly StudioExactReviewSnapshot _snapshot;
    private readonly TextBlock _selectedHeading = new();
    private readonly TextBlock _pathCaution = new() { TextWrapping = TextWrapping.Wrap,
        Foreground = StudioPalette.Secondary, FontSize = 12 };
    private readonly TextBox _source = ExactPathBox();
    private readonly TextBox _destination = ExactPathBox();
    private readonly TextBox _recovery = ExactPathBox();
    private readonly TextBox _undoRecovery = ExactPathBox();
    private readonly StackPanel _movePaths = new() { Spacing = 5 };
    private readonly Button _copySource = new() { Content = "Copy exact source", MinHeight = 34 };
    private readonly Button _copyDestination = new() { Content = "Copy exact destination", MinHeight = 34 };
    private readonly Button _cancel = new() { Content = "Cancel review · no approval", MinHeight = 42 };
    private readonly Button _approve = new() { MinHeight = 42 };
    private readonly TextBlock _clipboardStatus = new() { TextWrapping = TextWrapping.Wrap,
        Foreground = StudioPalette.Secondary, FontSize = 12 };
    private int _clipboardFeedbackSequence;

    internal event EventHandler? ApproveRequested;
    internal event EventHandler? DeclineRequested;

    internal StudioExactReviewView(StudioExactReviewSnapshot snapshot, bool generatedFixtureMove)
    {
        _snapshot = snapshot;
        Background = StudioPalette.Shell;
        _approve.Content = snapshot.IsMove
            ? (generatedFixtureMove ? "Approve TEST-FIXTURE Move paths" : "Approve this Move · original relocates")
            : "Approve these Copy paths";
        _approve.IsEnabled = snapshot.Included.Length > 0;
        _approve.Background = StudioPalette.Jade;
        _approve.Foreground = StudioPalette.JadeInk;
        _approve.FontWeight = FontWeight.SemiBold;

        var layout = new Grid { Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var heading = new StackPanel { Spacing = 5, Margin = new Thickness(0,0,0,13) };
        heading.Children.Add(new TextBlock { Text = snapshot.IsMove ? "Review this Move" : "Review these Copy paths",
            FontSize = 25, FontWeight = FontWeight.SemiBold, Foreground = StudioPalette.Text });
        heading.Children.Add(new TextBlock { Text = snapshot.IsMove
                ? "Verify the original, destination and owned recovery paths. The original location changes only after a separate Apply."
                : "Copies will leave the originals in place. Approval is not Apply.",
            TextWrapping = TextWrapping.Wrap, Foreground = StudioPalette.Secondary });
        heading.Children.Add(new TextBlock { Text =
            $"{snapshot.Included.Length} included · {snapshot.Skipped.Length} skipped · Collisions: {snapshot.CollisionPolicy}",
            FontWeight = FontWeight.SemiBold, Foreground = StudioPalette.Jade });
        layout.Children.Add(heading);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("1.05*,1.15*") };
        Grid.SetRow(columns,1); layout.Children.Add(columns);
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        left.Children.Add(Label("Included photographs (select one for full paths)"));
        var list = new ListBox { ItemsSource = snapshot.Included.Select(r=>r.FriendlyLine).ToArray(),
            MinHeight = 100, Background = StudioPalette.Surface,
            Foreground = StudioPalette.Text, HorizontalAlignment=HorizontalAlignment.Stretch };
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        Grid.SetRow(list,1);left.Children.Add(list);
        var skippedLabel = new TextBlock { Text = snapshot.Skipped.Length == 0
                ? "No skipped photographs." : $"Skipped photographs ({snapshot.Skipped.Length}) · not copied",
            Foreground = StudioPalette.Secondary, Margin = new Thickness(0,8,0,4) };
        Grid.SetRow(skippedLabel,2); left.Children.Add(skippedLabel);
        var skipped = new ListBox { ItemsSource = snapshot.Skipped.Select(s=>s.FriendlyLine).ToArray(),
            MaxHeight = 92, MinHeight = snapshot.Skipped.Length==0 ? 0 : 40,
            Background = StudioPalette.Surface, Foreground = StudioPalette.Secondary };
        Grid.SetRow(skipped,3);left.Children.Add(skipped);
        columns.Children.Add(left);

        var right = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        var details = new StackPanel { Spacing = 7 };
        _selectedHeading.FontWeight=FontWeight.SemiBold;
        _selectedHeading.FontSize=16;
        _selectedHeading.Foreground=StudioPalette.Text;
        details.Children.Add(_selectedHeading);
        details.Children.Add(Label("Exact source (selectable, unshortened)"));
        details.Children.Add(_source);
        details.Children.Add(_copySource);
        details.Children.Add(Label("Exact destination (selectable, unshortened)"));
        details.Children.Add(_destination);
        details.Children.Add(_copyDestination);
        details.Children.Add(_pathCaution);
        if (snapshot.IsMove)
        {
            _movePaths.Children.Add(Label("Exact retained-original recovery"));
            _movePaths.Children.Add(_recovery);
            var copyRecovery = new Button { Content = "Copy exact recovery path", MinHeight = 34 };
            copyRecovery.Click += async (_,_)=>await CopyAsync(_recovery.Text ?? string.Empty, "Retained-original recovery path");
            _movePaths.Children.Add(copyRecovery);
            _movePaths.Children.Add(Label("Exact output Undo archive"));
            _movePaths.Children.Add(_undoRecovery);
            var copyUndo = new Button { Content = "Copy exact Undo archive path", MinHeight = 34 };
            copyUndo.Click += async (_,_)=>await CopyAsync(_undoRecovery.Text ?? string.Empty, "Output Undo archive path");
            _movePaths.Children.Add(copyUndo);
            _movePaths.Children.Add(new TextBlock { Text = "Move needs extra storage for the retained original. Native policy rechecks the exact files at Apply.",
                TextWrapping = TextWrapping.Wrap, Foreground = StudioPalette.Secondary });
            details.Children.Add(_movePaths);
        }
        details.Children.Add(_clipboardStatus);
        right.Content = details;
        Grid.SetColumn(right,1);columns.Children.Add(right);

        var footer = new Grid {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0,12,0,0) };
        footer.Children.Add(new TextBlock { Text = "Review only · no files changed",
            Foreground = StudioPalette.Secondary, VerticalAlignment=VerticalAlignment.Center });
        var copyAll = new Button { Content = "Copy all exact details", MinHeight = 36 };
        copyAll.Click += async (_,_)=>await CopyAsync(_snapshot.CopyAllExactDetails(), "All exact Review details");
        Grid.SetColumn(copyAll,2);footer.Children.Add(copyAll);
        Grid.SetRow(_cancel,1);footer.Children.Add(_cancel);
        Grid.SetRow(_approve,1);Grid.SetColumn(_approve,2);footer.Children.Add(_approve);
        Grid.SetRow(footer,2);layout.Children.Add(footer);

        _cancel.Click += (_,_) => DeclineRequested?.Invoke(this,EventArgs.Empty);
        _approve.Click += (_,_) => ApproveRequested?.Invoke(this,EventArgs.Empty);
        _copySource.Click += async (_,_) => await CopyAsync(_source.Text ?? string.Empty, "Source path");
        _copyDestination.Click += async (_,_) => await CopyAsync(_destination.Text ?? string.Empty, "Destination path");
        list.SelectionChanged += (_,_) =>
        {
            if (list.SelectedIndex >= 0)
            {
                skipped.SelectedIndex = -1;
                SelectItem(list.SelectedIndex);
            }
        };
        skipped.SelectionChanged += (_,_) =>
        {
            if (skipped.SelectedIndex >= 0)
            {
                list.SelectedIndex = -1;
                SelectSkipped(skipped.SelectedIndex);
            }
        };
        if (snapshot.Included.Length > 0) list.SelectedIndex=0;
        else if (snapshot.Skipped.Length > 0) skipped.SelectedIndex=0;
        Content=layout;
    }

    internal void FocusCancel() => _cancel.Focus();

    private void SelectItem(int index)
    {
        if (index<0 || index>=_snapshot.Included.Length) return;
        var item = _snapshot.Included[index];
        _selectedHeading.Text = $"Photo {item.Ordinal} of {_snapshot.Included.Length} · {item.Name}";
        _source.Text=item.Source;
        _destination.Text=item.Destination;
        _recovery.Text=item.OriginalRecovery ?? string.Empty;
        _undoRecovery.Text=item.OutputUndoRecovery ?? string.Empty;
        var warning = StudioExactReviewSnapshot.DisplayWarning(item.Source) + " " +
                      StudioExactReviewSnapshot.DisplayWarning(item.Destination);
        _pathCaution.Text=warning.Trim();
        _clipboardStatus.Text="";
    }

    private void SelectSkipped(int index)
    {
        if (index < 0 || index >= _snapshot.Skipped.Length) return;
        var skip = _snapshot.Skipped[index];
        _selectedHeading.Text = "Skipped · " + Path.GetFileName(skip.Source);
        _source.Text = skip.Source;
        _destination.Text = skip.AttemptedDestination;
        _recovery.Text = string.Empty;
        _undoRecovery.Text = string.Empty;
        _pathCaution.Text = "Not included in this operation: " + skip.Reason + ". " +
            StudioExactReviewSnapshot.DisplayWarning(skip.Source) + " " +
            StudioExactReviewSnapshot.DisplayWarning(skip.AttemptedDestination);
        _clipboardStatus.Text = string.Empty;
    }

    private async Task CopyAsync(string exact, string label)
    {
        int feedback = ++_clipboardFeedbackSequence;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) throw new InvalidOperationException("Clipboard is unavailable in this window.");
            await clipboard.SetTextAsync(exact);
            if (feedback == _clipboardFeedbackSequence)
                _clipboardStatus.Text=$"{label} copied · action {feedback}. Clipboard is not approval.";
        }
        catch (Exception ex)
        {
            if (feedback == _clipboardFeedbackSequence)
                _clipboardStatus.Text=label+" could not be copied: " + ex.Message;
        }
    }

    private static TextBlock Label(string label) => new() { Text = label,
        FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
        Foreground = StudioPalette.Secondary, FontSize = 13 };
    private static TextBox ExactPathBox() => new() { IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap, AcceptsReturn = false,
        MinHeight=52, MaxHeight=90, FontSize=13,
        Background=StudioPalette.Surface, Foreground=StudioPalette.Text };
}

