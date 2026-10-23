using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

public sealed partial class MainWindow
{
    private readonly Button _decisionCenter = new() { Content = "Review collection…", MinHeight = 38 };
    private readonly TextBlock _decisionHeadline = Muted("Open photographs to review decisions.");
    private readonly Expander _fixtureRecoveryExpander = new()
    {
        Header = "Advanced · generated test recovery controls",
        IsExpanded = false
    };
    private sealed record DecisionCenterSelection(Guid PhotoId, CollectionDecisionAction Action,
        Guid[]? BatchPhotoIds = null, char? BatchRouteKey = null);

    // Deliberately separate: folder clearing and Reject-review unmarking must
    // each satisfy their existing history/journal barriers. Neither grants
    // native Reject relocation or bypasses a recovered operation.
    private bool CanUnmarkSelectedRejectReview() => CanStageRejectReview();

    // Batch changes must respect the same read/write/journal locks as clearing
    // a staged assignment. They are NEVER a file-operation authorization.
    private bool CanAssignFilteredBatch() => CanClearSelectedDecision();

    private bool CanShowSelectedCollectionPhoto() =>
        !_busy && _storeHealthy && !_anyRejectHistory &&
        EnsureViewerNavigationAllowed();

    private bool CanClearSelectedDecision() =>
        !_busy && _storeHealthy && !_copyBarrier && !_anyMoveHistory &&
        !_anyRejectHistory && !_anyRenameHistory && !RenameLocksWrites &&
        !JournalNeedsAttention && !_work.HasAppliedCopies &&
        _sessionStore is not null && _journal is not null &&
        (_sessionStore.AllCopyHistoryCount()==0 || _copyContinuationApproved);

    private async Task ReviewCollectionAsync()
    {
        if (_busy || !_storeHealthy || _work.Photos.Count == 0) return;
        var captured = _work.CaptureSession();
        CollectionDecisionOverview overview;
        try { overview = CollectionDecisionPresentation.FromSnapshot(captured); }
        catch (Exception ex) { Message("Collection review refused: " + ex.Message); return; }
        var rows = overview.Rows.ToArray();
        var dialog = new Window
        {
            Title = "Review this collection — no file changes",
            Width = 870, Height = 650, MinWidth = 650, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#101C28")), Foreground = Brushes.White
        };
        var panel = new Grid { Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto") };
        var title = new TextBlock
        {
            Text = "Collection decisions", FontSize = 24,
            FontWeight = FontWeight.SemiBold, Margin = new Thickness(0,0,0,5)
        };
        Grid.SetRow(title,0); panel.Children.Add(title);
        var info = Muted(overview.Headline + "\nThese are saved CHOICES, not native recovery authorization. " +
            (_anyMoveHistory || _anyRejectHistory || RenameLocksWrites || _copyBarrier || JournalNeedsAttention
                ? "File-operation history is present: inspect recovery; collection changes remain locked."
                : "Folder choices and Reject-review marks can be reconsidered individually. No file moves here."));
        Grid.SetRow(info,1);panel.Children.Add(info);
        var filters = new ComboBox
        {
            ItemsSource = new[] { "All photographs", "Needs a decision", "Assigned to a folder", "Reject review only" },
            SelectedIndex = 0, MinWidth = 220, Margin = new Thickness(0,12,0,7)
        };
        Grid.SetRow(filters,2); panel.Children.Add(filters);
        // Search and state filtering never change the saved session. A filter
        // change deliberately clears selection so an action always requires a
        // fresh click on the exact visible photograph, not the old list index.
        var search = new TextBox
        {
            PlaceholderText = "Search photograph filename (not folder path)",
            Margin = new Thickness(0,0,0,5)
        };
        Grid.SetRow(search,3); panel.Children.Add(search);
        var visibleCount = Muted("Select a photograph to inspect its exact original path.");
        Grid.SetRow(visibleCount,4); panel.Children.Add(visibleCount);
        var shown = CollectionDecisionFilter.Apply(rows,0,null);
        var list = new ListBox
        {
            ItemsSource = shown.Select(row => row.Display).ToArray(),
            SelectedIndex = -1,
            Margin = new Thickness(0,4,0,10)
        };
        var selectedPath = Muted("No photograph selected. Selection never changes any original file.");
        void UpdateVisibleRows()
        {
            shown = CollectionDecisionFilter.Apply(rows,
                filters.SelectedIndex < 0 ? 0 : filters.SelectedIndex, search.Text);
            list.ItemsSource = shown.Select(row => row.Display).ToArray();
            list.SelectedIndex = -1;
            visibleCount.Text = $"Showing {shown.Length} of {rows.Length} photographs · read-only view";
            selectedPath.Text = "No photograph selected. Choose one row before acting.";
        }
        Grid.SetRow(list,5);panel.Children.Add(list);
        Grid.SetRow(selectedPath,6);panel.Children.Add(selectedPath);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        var close = Button("Close",()=>dialog.Close((DecisionCenterSelection?)null));
        var batch = Button("Assign FILTERED undecided photos…",(Func<Task>)(async () =>
        {
            // Deliberately require the explicit Needs a decision filter. A
            // search within that filter narrows the exact photographed set.
            if (filters.SelectedIndex != 1 ||
                !CollectionDecisionActionPolicy.CanOfferBatch(shown,CanAssignFilteredBatch())) return;
            var picked = shown.ToArray(); // immutable visible selection at review
            var routeChoices = captured.Routes.OrderBy(r=>r.Key).ToArray();
            if (routeChoices.Length == 0) return;
            var approve = new Window
            {
                Title = "Stage filtered collection decisions — no file changes",
                Width = 790, Height = 540, MinWidth = 590, MinHeight = 390,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#101C28")), Foreground = Brushes.White
            };
            var body = new Grid { Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
            var summary = new TextBlock
            {
                Text = $"Stage {picked.Length} exact undecided photographs to ONE route?",
                FontSize = 20, FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(summary,0); body.Children.Add(summary);
            var notice = Muted("This saves folder CHOICES only. It does not Copy, Move, Rename or Reject any file. " +
                "Any previous Copy approval is revoked. Review every source path before continuing.");
            notice.Margin = new Thickness(0,9,0,9);
            Grid.SetRow(notice,1);body.Children.Add(notice);
            var routes = new ComboBox
            {
                ItemsSource = routeChoices.Select(r=>$"{r.Key} · {r.Label} ({r.Kind})").ToArray(),
                SelectedIndex = -1, MinWidth = 230,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(routes,2);body.Children.Add(routes);
            var allPaths = new TextBlock
            {
                Text = string.Join("\n",picked.Select(row => $"{row.Position}. {row.Path}")),
                TextWrapping = TextWrapping.Wrap
            };
            var scroll = new ScrollViewer
            {
                Content = allPaths, Margin = new Thickness(0,14,0,12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroll,3);body.Children.Add(scroll);
            var buttons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(Button("Cancel — keep decisions",()=>approve.Close(false)));
            var confirm = Button("STAGE EXACT FILTERED CHOICES (NO FILE ACTION)",()=>approve.Close(true));
            confirm.Margin=new Thickness(9,0,0,0);
            confirm.IsEnabled = false;
            routes.SelectionChanged += (_,_) =>
                confirm.IsEnabled = routes.SelectedIndex >= 0 && routes.SelectedIndex < routeChoices.Length;
            buttons.Children.Add(confirm);
            Grid.SetRow(buttons,4);body.Children.Add(buttons);
            approve.Content=body;
            if (!await approve.ShowDialog<bool>(dialog)) return;
            if (routes.SelectedIndex < 0 || routes.SelectedIndex >= routeChoices.Length) return;
            dialog.Close(new DecisionCenterSelection(Guid.Empty,
                CollectionDecisionAction.AssignFilteredBatch,
                picked.Select(p=>p.PhotoId).ToArray(),routeChoices[routes.SelectedIndex].Key));
        }));
        var show = Button("Show selected photograph",()=>
        {
            if (list.SelectedIndex is int i && i>=0 && i<shown.Length)
                dialog.Close(new DecisionCenterSelection(shown[i].PhotoId,CollectionDecisionAction.Show));
        });
        var clear = Button("Clear selected folder choice…",()=>
        {
            if (list.SelectedIndex is int i && i>=0 && i<shown.Length && shown[i].State=="Assigned")
                dialog.Close(new DecisionCenterSelection(shown[i].PhotoId,CollectionDecisionAction.ClearFolderChoice));
        });
        var unmark = Button("Unmark selected Reject-review photo…",()=>
        {
            if (list.SelectedIndex is int i && i>=0 && i<shown.Length &&
                shown[i].State=="RejectReview")
                dialog.Close(new DecisionCenterSelection(shown[i].PhotoId,
                    CollectionDecisionAction.UnmarkRejectReview));
        });
        void RefreshDecisionActions()
        {
            var selectedRow = list.SelectedIndex is int i && i>=0 && i<shown.Length
                ? shown[i] : null;
            var navigate = CanShowSelectedCollectionPhoto();
            var clearFolder = CanClearSelectedDecision();
            var clearReject = CanUnmarkSelectedRejectReview();
            show.IsEnabled = CollectionDecisionActionPolicy.CanOffer(selectedRow,
                CollectionDecisionAction.Show,navigate,clearFolder,clearReject);
            clear.IsEnabled = CollectionDecisionActionPolicy.CanOffer(selectedRow,
                CollectionDecisionAction.ClearFolderChoice,navigate,clearFolder,clearReject);
            unmark.IsEnabled = CollectionDecisionActionPolicy.CanOffer(selectedRow,
                CollectionDecisionAction.UnmarkRejectReview,navigate,clearFolder,clearReject);
            batch.IsEnabled = filters.SelectedIndex==1 &&
                CollectionDecisionActionPolicy.CanOfferBatch(shown,CanAssignFilteredBatch()) &&
                captured.Routes.Length>0;
        }
        list.SelectionChanged += (_,_)=>
        {
            var selectedRow = list.SelectedIndex is int i && i>=0 && i<shown.Length
                ? shown[i] : null;
            selectedPath.Text = selectedRow is null
                ? "No photograph selected. Choose one row before acting."
                : $"Selected original: {selectedRow.Path} · {selectedRow.State} · no file changes";
            RefreshDecisionActions();
        };
        // All four action buttons and the batch control exist before we
        // subscribe to any callbacks that can refresh their enabled states.
        // An Avalonia filter/search change can now NEVER invoke a local
        // function which captures not-yet-assigned controls (CS0165).
        filters.SelectionChanged += (_,_) =>
        {
            UpdateVisibleRows();
            RefreshDecisionActions();
        };
        search.PropertyChanged += (_,e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                UpdateVisibleRows();
                RefreshDecisionActions();
            }
        };
        UpdateVisibleRows();
        RefreshDecisionActions();
        foreach(var b in new[]{close,show,clear,unmark})
        { b.Margin = new Thickness(6,0,0,5);actions.Children.Add(b); }
        batch.Margin=new Thickness(6,0,0,5);
        actions.Children.Add(batch);
        Grid.SetRow(actions,7);panel.Children.Add(actions);
        dialog.Content = panel;
        var selected = await dialog.ShowDialog<DecisionCenterSelection?>(this);
        if (selected is null) return;
        // Cursor-only actions require the pre-existing navigation gate. Changing
        // an assignment additionally requires all native journal barriers.
        if (_work.SessionId!=captured.SessionId || _work.Revision!=captured.Revision ||
            !CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession()).Rows
                .Select(x=>x.PhotoId).SequenceEqual(rows.Select(x=>x.PhotoId)))
        { Message("Collection changed while reviewing; no selection applied."); return; }
        var batchAction = selected.Action==CollectionDecisionAction.AssignFilteredBatch;
        var original = batchAction ? null : rows.SingleOrDefault(row => row.PhotoId == selected.PhotoId);
        if (!batchAction && original is null)
        { Message("Selection is no longer in this collection."); return; }
        if (selected.Action == CollectionDecisionAction.ClearFolderChoice)
        {
            if (original is null || original.State!="Assigned" || !CanClearSelectedDecision())
            { Message("Choice clearing is locked by file-operation history. No change made."); return; }
            var confirm = new Window { Title="Clear ONE staged choice — no file changes",
                Width=690,Height=270,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White };
            var body = new StackPanel { Margin=new Thickness(20),Spacing=13 };
            body.Children.Add(new TextBlock { Text="Clear only this photograph's folder choice?",
                FontSize=19,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap });
            body.Children.Add(Muted(original.Path + "\n\nOther staged choices and Reject-review marks remain. All Copy approvals are revoked. No file changes."));
            var consent = new WrapPanel { Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right };
            consent.Children.Add(Button("Keep choice",()=>confirm.Close(false)));
            var yes=Button("CLEAR THIS ONE CHOICE (NO FILE MOVE)",()=>confirm.Close(true));
            yes.Margin=new Thickness(10,0,0,0);consent.Children.Add(yes);
            body.Children.Add(consent);confirm.Content=body;
            if (!await confirm.ShowDialog<bool>(this)) return;
        }
        if (selected.Action == CollectionDecisionAction.UnmarkRejectReview)
        {
            if (original is null || original.State!="RejectReview" || !CanUnmarkSelectedRejectReview())
            { Message("Reject-review changes are locked by saved operation history. No change made."); return; }
            var confirm = new Window { Title="Unmark ONE saved Reject-review choice — no file changes",
                Width=690, Height=280, WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White };
            var body = new StackPanel { Margin=new Thickness(20),Spacing=13 };
            body.Children.Add(new TextBlock {Text="Unmark only this photograph for Reject review?",
                FontSize=19,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap});
            body.Children.Add(Muted(original.Path +
                "\n\nThis removes ONE reversible review mark. Other photographs, folder choices, " +
                "native journals and the original files are untouched. Existing Copy approval is revoked."));
            var consent=new WrapPanel {Orientation=Orientation.Horizontal,
                HorizontalAlignment=HorizontalAlignment.Right};
            consent.Children.Add(Button("Keep Reject-review mark",()=>confirm.Close(false)));
            var yes=Button("UNMARK THIS ONE REVIEW CHOICE (NO FILE MOVE)",()=>confirm.Close(true));
            yes.Margin=new Thickness(10,0,0,0);consent.Children.Add(yes);
            body.Children.Add(consent);confirm.Content=body;
            if (!await confirm.ShowDialog<bool>(this)) return;
        }
        try
        {
            if (_busy || _work.SessionId!=captured.SessionId || _work.Revision!=captured.Revision)
                throw new IOException("Collection changed while reviewing. No choice changed.");
            // Recheck all saved IDs and route after both nested review dialogs:
            // stale list positions, changes to history, or a changed search
            // can never turn a read-only filter into native file authority.
            var current = CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            if (batchAction)
            {
                var ids=selected.BatchPhotoIds ?? Array.Empty<Guid>();
                var currentById=current.Rows.ToDictionary(x=>x.PhotoId);
                var subset = ids.Select(id=>currentById.TryGetValue(id,out var row)
                    ? row : null).ToArray();
                if (!CollectionDecisionActionPolicy.CanOfferBatch(subset!,CanAssignFilteredBatch()) ||
                    selected.BatchRouteKey is not { } key ||
                    !captured.Routes.Any(route=>route.Key==key))
                    throw new IOException("Filtered batch is no longer an exact undecided selection. No choice changed.");
            }
            else
            {
                if (original is null)
                    throw new IOException("Selected photograph is no longer in this collection.");
                if (!CollectionDecisionActionPolicy.CanOffer(original,selected.Action,
                    CanShowSelectedCollectionPhoto(),CanClearSelectedDecision(),
                    CanUnmarkSelectedRejectReview()))
                    throw new IOException("Selected collection action is no longer eligible; no change made.");
                if (current.Rows.SingleOrDefault(x=>x.PhotoId==selected.PhotoId)?.State!=original.State)
                    throw new IOException("Selected decision changed during confirmation.");
            }
            // Cache/UI gating is not trusted. A fresh on-disk native proof is
            // required AFTER all modal confirmations and BEFORE editing.
            if (_sessionStore is not null && _sessionStore.AllCopyHistoryCount()!=0)
            {
                var proof=CopyUndoContinuation.Inspect(_sessionStore,_work);
                if (!_copyContinuationApproved || !proof.Verified)
                    throw new IOException("Previous Copy Undo no longer verifies. "+proof.Reason);
            }
            _busy=true;
            if (batchAction)
            {
                _work.AssignReviewedBatch(selected.BatchPhotoIds!,selected.BatchRouteKey!.Value,
                    captured.SessionId,captured.Revision);
                _prepared=null;_hasReviewed=false;
            }
            else if (selected.Action == CollectionDecisionAction.ClearFolderChoice)
            {
                // A Copy retirement removes the old decision-undo entries.
                // After verified Undo, three valid restored routes may have NO
                // ordinary decision history. Only the exact matching historical
                // Copy row and full fresh native proof may authorize clearing
                // that kind of route. Ordinary choices retain their old guard.
                var selectedHasDecision=_work.CaptureSession().Decisions.Any(
                    decision=>decision.PhotoId==selected.PhotoId);
                if (!selectedHasDecision && _sessionStore is not null &&
                    _sessionStore.AllCopyHistoryCount()!=0)
                    CopyUndoChoiceEditor.ClearRestoredChoice(_sessionStore,_work,selected.PhotoId);
                else
                    _work.ClearSelectedAssignment(selected.PhotoId);
                _prepared=null;_hasReviewed=false;
            }
            else if (selected.Action == CollectionDecisionAction.UnmarkRejectReview)
            {
                _work.UnmarkRejectReview(selected.PhotoId);
                _prepared=null;_hasReviewed=false;
            }
            else if (original is { } selectedOriginal) _work.JumpToPhoto(selectedOriginal.Position);
            else throw new IOException("Selected photograph is no longer in this collection.");
            await SaveSessionAsync();
            Message(selected.Action switch
            {
                CollectionDecisionAction.AssignFilteredBatch =>
                    $"Saved {selected.BatchPhotoIds!.Length} reviewed folder choices; Copy approval revoked. No file operations performed.",
                CollectionDecisionAction.ClearFolderChoice =>
                    "Only the selected folder choice was cleared and saved. Other decisions remain; prior Copy approval revoked. No files moved.",
                CollectionDecisionAction.UnmarkRejectReview =>
                    "Only the selected Reject-review mark was removed and saved. Other marks remain; Copy approval revoked. No files moved.",
                _ => "Showing selected photograph. No file changes."
            });
        }
        catch(Exception ex) { Message("Collection review action refused: " + ex.Message); }
        finally { _busy=false;Refresh(); }
    }
}
