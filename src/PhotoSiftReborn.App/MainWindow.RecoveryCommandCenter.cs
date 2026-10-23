using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

public sealed partial class MainWindow
{
    private bool _recoveryCommandCenterOpen;

    private bool P3R2CanInspectHistory() => P3R2RecoveryInspectionGate.CanInspect(
        _sessionStore is not null, _storeHealthy, _p3R2MixedCopyMoveReadOnlyStartup,
        _busy, _studioEmbeddedLease.Active, _studioRenameLease.Active, _studioRejectLease.Active);

    // Full installation history, never a write authority. Use a separate modal
    // so the main window cannot start a concurrent operation during native reads.
    private async Task ShowRecoveryCommandCenterAsync()
    {
        if (_recoveryCommandCenterOpen || !P3R2CanInspectHistory())
        {
            Message("Finish the current operation before inspecting all recovery history. Nothing changed.");
            return;
        }
        _recoveryCommandCenterOpen = true;
        var store = _sessionStore;
        var session = _work.SessionId;
        var revision = _work.Revision;
        var modal = new Window
        {
            Title = "PhotoSift Reborn · Recovery command center (read only)",
            Width = 1130, Height = 790, MinWidth = 820, MinHeight = 570,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = StudioPalette.Shell, Foreground = StudioPalette.Text
        };
        var frame = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto")
        };
        var title = new StackPanel { Spacing = 3 };
        title.Children.Add(new TextBlock
        {
            Text = "Recovery command center", FontSize = 25,
            FontWeight = FontWeight.SemiBold, Foreground = StudioPalette.Text
        });
        title.Children.Add(new TextBlock
        {
            Text = "Native evidence across every saved session · Read-only inspection",
            TextWrapping = TextWrapping.Wrap, Foreground = StudioPalette.Secondary
        });
        frame.Children.Add(title);

        var banner = new TextBlock
        {
            Text = "NOT INSPECTED · NO OPERATION AUTHORITY",
            Foreground = StudioPalette.Jade, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 7)
        };
        Grid.SetRow(banner, 1);
        frame.Children.Add(banner);

        // Explicit chips make all four families discoverable before opening a menu.
        // This is UI state, never a SQLite or native coordinator command.
        var controlStack = new StackPanel { Spacing = 6, Margin = new Thickness(0,0,0,10) };
        var familyRow = new WrapPanel { Orientation = Orientation.Horizontal };
        familyRow.Children.Add(new TextBlock
        {
            Text = "Operation family", Foreground = StudioPalette.Secondary,
            FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0,0,13,5)
        });
        string[] familyNames = ["All operations", "Copy", "Move", "Rename", "Reject"];
        var familyButtons = familyNames.Select((label, index) => new Button
        {
            Content = label, MinHeight = 34, MinWidth = index == 0 ? 115 : 85,
            Margin = new Thickness(0,0,7,5)
        }).ToArray();
        foreach (var button in familyButtons) familyRow.Children.Add(button);
        controlStack.Children.Add(familyRow);
        var filterRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var scan = new Button { Content = "Reinspect live evidence", MinHeight = 36,
            Margin = new Thickness(0,0,13,5) };
        var evidenceLabel = new TextBlock { Text = "Evidence status", FontWeight = FontWeight.SemiBold,
            Foreground = StudioPalette.Secondary, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0,0,8,5) };
        var evidence = new ComboBox
        {
            ItemsSource = new[] { "All evidence", "Unverified", "Unfinished", "Verified terminal" },
            SelectedIndex = 0, MinWidth = 165, Margin = new Thickness(0,0,13,5)
        };
        var query = new TextBox
        {
            PlaceholderText = "Search journal, state or proof", Width = 245,
            Margin = new Thickness(0,0,13,5)
        };
        var preview = new Button
        {
            Content = "Show example records", MinHeight = 36,
            Margin = new Thickness(0,0,0,5)
        };
        filterRow.Children.Add(scan);
        filterRow.Children.Add(evidenceLabel);
        filterRow.Children.Add(evidence);
        filterRow.Children.Add(query);
        filterRow.Children.Add(preview);
        controlStack.Children.Add(filterRow);
        Grid.SetRow(controlStack, 2);
        frame.Children.Add(controlStack);

        var stats = new TextBlock
        {
            Text = "Waiting for a native inspection", Foreground = StudioPalette.Text,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 11)
        };
        Grid.SetRow(stats, 3);
        frame.Children.Add(stats);

        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions("0.95*,1.05*") };
        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        var leftHeading = new TextBlock
        {
            Text = "Saved operations", Foreground = StudioPalette.Text,
            FontWeight = FontWeight.SemiBold, FontSize = 16,
            Margin = new Thickness(0,0,0,8)
        };
        left.Children.Add(leftHeading);
        var listArea = new Grid();
        var list = new ListBox { SelectedIndex = -1 };
        var emptyHeading = new TextBlock
        {
            Text = "Inspection hasn't completed", FontSize = 18,
            FontWeight = FontWeight.SemiBold, Foreground = StudioPalette.Text,
            TextWrapping = TextWrapping.Wrap
        };
        var emptyDescription = new TextBlock
        {
            Text = "Choose Reinspect live evidence to load recorded history.",
            Foreground = StudioPalette.Secondary, TextWrapping = TextWrapping.Wrap
        };
        var emptyBody = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        emptyBody.Children.Add(emptyHeading);
        emptyBody.Children.Add(emptyDescription);
        var emptyPanel = new Border
        {
            Background = StudioPalette.Surface,
            CornerRadius = new CornerRadius(9), Padding = new Thickness(22),
            Child = emptyBody
        };
        listArea.Children.Add(list);
        listArea.Children.Add(emptyPanel);
        Grid.SetRow(listArea, 1);
        left.Children.Add(listArea);
        var dependencyBlock = new Expander
        {
            Header = "Cross-family dependencies · Not inspected",
            IsExpanded = false,
            Margin = new Thickness(0,9,0,0)
        };
        var dependencyText = new TextBlock
        {
            Text = "Dependency evidence will appear after the native inspection.",
            TextWrapping = TextWrapping.Wrap, Foreground = StudioPalette.Secondary,
            Margin = new Thickness(12)
        };
        dependencyBlock.Content = new ScrollViewer
        {
            Content = dependencyText,
            MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(dependencyBlock, 2);
        left.Children.Add(dependencyBlock);
        panes.Children.Add(left);

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(10, 0, 0, 0)
        };
        var riskHeading = new TextBlock
        {
            Text = "Current observation", Foreground = StudioPalette.Jade,
            FontWeight = FontWeight.SemiBold, FontSize = 15,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,7)
        };
        right.Children.Add(riskHeading);
        var next = new TextBlock
        {
            Text = "No history has been inspected yet.", TextWrapping = TextWrapping.Wrap,
            Foreground = StudioPalette.Secondary, Margin = new Thickness(0,0,0,10)
        };
        Grid.SetRow(next,1);
        right.Children.Add(next);
        var detailArea = new Grid();
        var detailEmpty = new TextBlock
        {
            Text = "Select a saved operation to view its exact native evidence.\n\n" +
                   "This panel never executes an operation or repairs a journal.",
            Foreground = StudioPalette.Secondary, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(18)
        };
        var details = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Background = StudioPalette.Surface, Foreground = StudioPalette.Text,
            IsVisible = false
        };
        var detailPlaceholder = new Border
        {
            Background = StudioPalette.Surface, CornerRadius = new CornerRadius(9),
            Child = detailEmpty
        };
        detailArea.Children.Add(detailPlaceholder);
        detailArea.Children.Add(details);
        Grid.SetRow(detailArea, 2);
        right.Children.Add(detailArea);
        var boundary = new TextBlock
        {
            Text = "Snapshot only · File identities may change after inspection. No recovery or Undo is authorized.",
            Foreground = StudioPalette.Secondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0)
        };
        Grid.SetRow(boundary,3);
        right.Children.Add(boundary);
        Grid.SetColumn(right,1);
        panes.Children.Add(right);
        Grid.SetRow(panes,4);
        frame.Children.Add(panes);

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0,10,0,0)
        };
        var copy = new Button
        {
            Content = "Copy live diagnostic dossier", IsEnabled = false,
            MinHeight = 40, Margin = new Thickness(0,0,10,5)
        };
        var close = new Button
        {
            Content = "Close", MinHeight = 40,
            Margin = new Thickness(0,0,10,5)
        };
        var clipboardStatus = new TextBlock
        {
            Text = "Read only · No uploads or automatic actions",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioPalette.Secondary, TextWrapping = TextWrapping.Wrap
        };
        close.Click += (_,_) => modal.Close();
        footer.Children.Add(copy);
        footer.Children.Add(close);
        footer.Children.Add(clipboardStatus);
        Grid.SetRow(footer,5);
        frame.Children.Add(footer);
        modal.Content = frame;

        StudioRecoveryDossier? current = null;
        StudioRecoveryCase[] displayed = [];
        int selectedFamily = 0;
        int generation = 0;
        bool exampleMode = false;
        bool IsFresh() => ReferenceEquals(_sessionStore, store) &&
            P3R2CanInspectHistory() && _work.SessionId == session &&
            _work.Revision == revision;
        void StyleFamilyButtons()
        {
            for (int i = 0; i < familyButtons.Length; i++)
            {
                familyButtons[i].Background = selectedFamily == i ? StudioPalette.Jade : StudioPalette.Surface;
                familyButtons[i].Foreground = selectedFamily == i ? StudioPalette.JadeInk : StudioPalette.Text;
                familyButtons[i].FontWeight = selectedFamily == i ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        void ClearSelection()
        {
            detailPlaceholder.IsVisible = true;
            details.IsVisible = false;
            details.Text = "";
        }
        void ShowFiltered()
        {
            if (current is null) return;
            var index = list.SelectedIndex;
            string? selected = index >= 0 && index < displayed.Length
                ? displayed[index].Family + ":" + displayed[index].Key : null;
            displayed = StudioRecoveryCommandCenterPresentation.Filter(current.Cases,
                selectedFamily, evidence.SelectedIndex < 0 ? 0 : evidence.SelectedIndex, query.Text);
            list.ItemsSource = displayed.Select(StudioRecoveryCommandCenterUiState.OperationRow).ToArray();
            list.SelectedIndex = selected is null ? -1 : Array.FindIndex(displayed,
                x => x.Family + ":" + x.Key == selected);
            bool nothing = displayed.Length == 0;
            emptyPanel.IsVisible = nothing;
            list.IsVisible = !nothing;
            emptyHeading.Text = StudioRecoveryCommandCenterUiState.EmptyHeading(current,
                displayed.Length, selectedFamily, evidence.SelectedIndex, query.Text);
            emptyDescription.Text = StudioRecoveryCommandCenterUiState.EmptyExplanation(current,
                displayed.Length, selectedFamily, evidence.SelectedIndex, query.Text);
            stats.Text = StudioRecoveryCommandCenterUiState.Summary(current, displayed.Length) +
                (exampleMode ? "  ·  EXAMPLE ONLY" : "");
            if (list.SelectedIndex < 0) ClearSelection();
        }
        list.SelectionChanged += (_,_) =>
        {
            int index = list.SelectedIndex;
            if (index >= 0 && index < displayed.Length)
            {
                details.Text = displayed[index].ExactDetails;
                details.IsVisible = true;
                detailPlaceholder.IsVisible = false;
            }
            else ClearSelection();
        };
        for (int i = 0; i < familyButtons.Length; i++)
        {
            int target = i;
            familyButtons[i].Click += (_,_) =>
            {
                selectedFamily = target;
                StyleFamilyButtons();
                ShowFiltered();
            };
        }
        StyleFamilyButtons();
        evidence.SelectionChanged += (_,_) => ShowFiltered();
        query.PropertyChanged += (_,e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowFiltered();
        };

        void ShowExample()
        {
            // Explicitly opt-in, in-memory training content only.
            // It is impossible to promote these rows into an operation ticket.
            generation++;
            exampleMode = true;
            current = StudioRecoveryCommandCenterUiState.ExampleOnly();
            banner.Text = "EXAMPLE ONLY · NO NATIVE OR SQLITE INSPECTION · NOT YOUR FILES";
            riskHeading.Text = "Fictitious scenarios — UI preview";
            next.Text = current.NextStep;
            dependencyBlock.Header = "Illustrative dependency · NOT native evidence";
            dependencyText.Text = current.DependencySummary + "\n\n" +
                current.Conflicts[0].Explanation;
            clipboardStatus.Text = "Sample view: live dossier copying disabled. Reinspect to return to real history.";
            copy.IsEnabled = false;
            ShowFiltered();
        }
        preview.Click += (_,_) => ShowExample();
        async Task Inspect()
        {
            int request = ++generation;
            exampleMode = false;
            current = null;
            displayed = [];
            list.ItemsSource = Array.Empty<string>();
            list.IsVisible = false;
            emptyPanel.IsVisible = true;
            emptyHeading.Text = "Inspecting native evidence…";
            emptyDescription.Text = "Reading journals and checking current native file identities.";
            ClearSelection();
            copy.IsEnabled = false;
            scan.IsEnabled = false;
            preview.IsEnabled = false;
            banner.Text = "SCANNING · READ ONLY · NO OPERATION AUTHORITY";
            riskHeading.Text = "Native inspection in progress";
            stats.Text = "No completed scan yet";
            clipboardStatus.Text = "Native evidence remains read only.";
            try
            {
                if (!IsFresh()) throw new InvalidOperationException(
                    "Workspace changed; close and reopen recovery inspection.");
                var result = await Task.Run(() =>
                {
                    var paired = store.InspectM6JP3PairedRecoveryObservation();
                    return StudioRecoveryCommandCenterPresentation.Create(
                        paired.Inventory, paired.Dependencies);
                });
                if (request != generation || !IsFresh()) throw new InvalidOperationException(
                    "Native inspection became stale; no observation may be trusted.");
                current = result;
                banner.Text = "LIVE NATIVE SNAPSHOT · READ ONLY · NO OPERATION AUTHORITY";
                riskHeading.Text = result.Risk;
                next.Text = result.Cases.Count == 0
                    ? "No saved operations were observed. This is an empty history, not an operation grant."
                    : result.NextStep;
                dependencyBlock.Header = "Cross-family dependencies · " +
                    result.Conflicts.Count + " observed conflicts";
                dependencyText.Text = result.DependencySummary + "\n\n" +
                    (result.Conflicts.Count == 0
                        ? "No recorded edges in this observation. This does not prove independence."
                        : string.Join("\n\n", result.Conflicts.Take(16).Select(x =>
                            x.Kind + " · " + x.FirstFamily + " → " + x.SecondFamily +
                            "\n" + x.Explanation)) +
                            (result.Conflicts.Count > 16 ? "\nMore in copied dossier." : ""));
                copy.IsEnabled = true;
                clipboardStatus.Text = "Live report available locally · Copies only when requested";
                ShowFiltered();
            }
            catch(Exception ex)
            {
                current = null;
                displayed = [];
                list.ItemsSource = Array.Empty<string>();
                list.IsVisible = false;
                emptyPanel.IsVisible = true;
                emptyHeading.Text = "Inspection could not be completed";
                emptyDescription.Text = "Preserve all saved journals, source photos, output files and recovery archives. Reinspect only after investigating the cause.";
                riskHeading.Text = "INSPECTION FAILED CLOSED";
                next.Text = ex.GetType().Name + ": " + ex.Message;
                dependencyBlock.Header = "Dependencies · Unknown";
                dependencyText.Text = "Not verified. No authorization is available.";
                stats.Text = "NO USABLE OBSERVATION";
                banner.Text = "FAILED INSPECTION · NO OPERATION AUTHORITY";
                clipboardStatus.Text = "No live dossier available.";
                copy.IsEnabled = false;
            }
            finally
            {
                scan.IsEnabled = IsFresh();
                preview.IsEnabled = IsFresh();
            }
        }
        scan.Click += async (_,_) => await Inspect();
        copy.Click += async (_,_) =>
        {
            if (exampleMode || current is null || !IsFresh())
            {
                copy.IsEnabled = false;
                clipboardStatus.Text = "Live report unavailable or stale. Reinspect first.";
                return;
            }
            try
            {
                var clipboard = TopLevel.GetTopLevel(modal)?.Clipboard
                    ?? throw new InvalidOperationException("System clipboard unavailable.");
                await clipboard.SetTextAsync(current.Report);
                clipboardStatus.Text = "Live diagnostic dossier copied locally; contains journal IDs and native evidence.";
            }
            catch(Exception ex)
            {
                clipboardStatus.Text = "Clipboard refused: " + ex.Message;
            }
        };
        try
        {
            modal.Opened += async (_,_) => await Inspect();
            await modal.ShowDialog(this);
        }
        finally { _recoveryCommandCenterOpen = false; }
        Message("Recovery command center closed. No files, grants or journals were changed.");
    }
}

// P3-R2: an exact exception type, not exception-message matching, distinguishes
// the supported mixed-history read-only observation from database failures.
// All mutation gates stay closed because MainWindow._storeHealthy remains false.
internal sealed class P3R2MixedCopyMoveStartupException(string reason) : IOException(reason);

internal static class P3R2RecoveryInspectionGate
{
    internal static bool IsExactlyKnownMixedHistory(Exception failure, bool storePresent) =>
        storePresent && failure is P3R2MixedCopyMoveStartupException;

    internal static bool CanInspect(bool storePresent, bool storeHealthy,
        bool specificallyLockedMixedHistory, bool busy,
        bool embeddedLease, bool renameLease, bool rejectLease) =>
        storePresent && (storeHealthy || specificallyLockedMixedHistory) && !busy &&
        !embeddedLease && !renameLease && !rejectLease;
}
