using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace PhotoSiftReborn;

// P6G3E-R3: real Studio page destinations. All original file-operation
// coordinators and handlers stay in their accepted, narrow authority lanes.
// This is navigation/layout work, NOT permission for mixed operations.
public sealed partial class MainWindow
{
    private enum StudioPage { Focus, Collection, Changes, History, Routes, PhotoTools, Diagnostics }
    private StudioPage _studioActivePage = StudioPage.Focus;
    private readonly ContentControl _studioHost = new();
    private readonly Dictionary<StudioPage, Control> _studioPages = new();
    private readonly Dictionary<StudioPage, Button> _studioTabs = new();
    private StudioCollectionSurface _studioCollectionSurface = StudioCollectionSurface.Gallery;
    private Border? _studioCollectionInspectorPanel;
    private Border? _studioCollectionGalleryPanel;
    private ScrollViewer? _studioCollectionScroll;
    private Button? _studioCollectionShowGallery;
    private Button? _studioCollectionShowInspector;
    private ListBox? _studioCollectionList;
    private TextBox? _studioCollectionSearch;
    private ComboBox? _studioCollectionFilter;
    private TextBlock? _studioCollectionSummary;
    private TextBlock? _studioCollectionProgress;
    private TextBlock? _studioCollectionRoutes;
    private TextBox? _studioCollectionExactSelection;
    private TextBlock? _studioCollectionActionFeedback;
    private Button? _studioCollectionOpen;
    private Button? _studioCollectionNext;
    // One explicit preview at a time. No background thumbnail enumeration,
    // second native decoder, original-file writes or hidden consent.
    private Image? _studioCollectionPreview;
    private TextBlock? _studioInspectorEmptyState;
    private TextBlock? _studioCollectionPreviewStatus;
    private Button? _studioCollectionPreviewLoad;
    private Button? _studioCollectionPreviewPrevious;
    private Button? _studioCollectionPreviewNext;
    private TextBlock? _studioCollectionReviewPosition;
    private Bitmap? _studioCollectionPreviewBitmap;
    private StudioCollectionPreviewTicket? _studioCollectionPreviewTicket;
    private StudioCollectionPreviewTicket? _studioCollectionDisplayedTicket;
    private CollectionDecisionRow[] _studioCollectionVisible = [];
    private TextBlock? _studioHistorySummary;
    private Border? _studioHistoryEmptyPanel;
    private Border? _studioChangesEmptyPanel;
    private StackPanel? _studioHistoryPopulatedPanel;
    private Expander? _studioHistoryLegacyBrowser;
    // Read-only projection of durable Copy rows; NEVER an execution grant or
    // claim that archived/on-disk bytes were just verified.
    private Expander? _studioChangesCopyDiagnostics;
    private TextBlock? _studioCopyHistoryCount;
    private TextBlock? _studioChangesOperationsCount;
    private StackPanel? _studioChangesOperationsRows;
    private TextBlock? _studioCopyHistoryNextStep;
    private StackPanel? _studioCopyHistoryRows;
    // M3 complete recorded browser: stable row identity, bounded ListBox viewport,
    // exact selectable paths and never a current-files verification claim.
    private TextBlock? _studioHistoryBrowserCount;
    private ListBox? _studioHistoryBrowserList;
    private TextBox? _studioHistoryBrowserDetail;
    private ComboBox? _studioHistoryBrowserFilter;
    private TextBox? _studioHistoryBrowserSearch;
    private StudioRecordedCopyRow[] _studioHistoryAll = [];
    private StudioRecordedCopyRow[] _studioHistoryShown = [];
    // M4B all-family read-only session ledger. Native family authority remains separate.
    private StudioRecordedFamilyRow[] _studioFamilyHistoryAll = [];
    private StudioRecordedFamilyRow[] _studioFamilyHistoryShown = [];
    private TextBlock? _studioFamilyHistoryCount;
    private ListBox? _studioFamilyHistoryList;
    private TextBox? _studioFamilyHistoryDetails;
    private TextBlock? _studioFamilyHistorySelectedInsight;
    private ComboBox? _studioFamilyHistoryFilter;
    private ComboBox? _studioFamilyHistoryOutcome;
    private TextBox? _studioFamilyHistorySearch;
    private TextBlock? _studioFamilyHistoryTriage;
    private TextBlock? _studioFamilyHistoryClipboardStatus;
    private Button? _studioFamilyHistoryCopyDetails;

    private static TextBlock StudioTitle(string title) => new()
    {
        Text = title, FontSize = 24, FontWeight = FontWeight.SemiBold,
        Foreground = StudioPalette.Text, Margin = new Thickness(0,0,0,8)
    };
    private static Border StudioCard(Control child, int padding = 18) => new()
    {
        Background=StudioPalette.Surface, CornerRadius=new CornerRadius(8),
        Padding=new Thickness(padding), Child=child
    };
    private static ScrollViewer StudioScroll(Control content) => new()
    {
        Content=content, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
    };
    // Two substantial independent sections share ONE page scroll, not a
    // nested accordion/scroll tree. On compact widths, stack the same controls.
    // All sections are constructed once, and no native handler is rebound.
    private void StudioAdaptColumns(Grid layout, Control second)
    {
        bool wide = Bounds.Width >= 1100 && Bounds.Height >= 550;
        layout.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(second, wide ? 1 : 0);
        Grid.SetRow(second, wide ? 0 : 1);
    }
    private Grid StudioTwoPane(Control first, Control second)
    {
        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
        first.Margin = new Thickness(0,0,12,12);
        second.Margin = new Thickness(0,0,0,12);
        split.Children.Add(first);
        split.Children.Add(second);
        void Adapt() => StudioAdaptColumns(split,second);
        SizeChanged += (_,_) => Adapt();
        Opened += (_,_) => Adapt();
        // Start stacked until actual Window.Bounds is measured on Opened.
        Grid.SetRow(second,1);
        return split;
    }

    private static void Detach(Grid from, Control child)
    {
        if (!from.Children.Remove(child))
            throw new InvalidOperationException("Studio control ownership changed: " + child.GetType().Name);
    }
    private static void Detach(StackPanel from, Control child)
    {
        if (!from.Children.Remove(child))
            throw new InvalidOperationException("Studio control ownership changed: " + child.GetType().Name);
    }
    private static void Detach(WrapPanel from, Control child)
    {
        if (!from.Children.Remove(child))
            throw new InvalidOperationException("Studio control ownership changed: " + child.GetType().Name);
    }

    private void ShowStudioPage(StudioPage page)
    {
        if (_studioEmbeddedLease.Active || _studioRenameLease.Active || _studioRejectLease.Active)
        {
            // No direct page switch, even from a programmatic callback.
            if (_studioReviewPendingMessage is not null)
                _studioReviewPendingMessage.Text = "Finish or cancel this review before changing pages.";
            return;
        }
        if (!_studioPages.TryGetValue(page,out var target)) return;
        if (ReferenceEquals(_studioHost.Content,target)) return;
        // A view switch is not an operation grant or an approval refresh.
        if (_studioActivePage == StudioPage.Collection)
        {
            ClearStudioContactSheet("Contact sheet cleared when leaving Collection.");
            ClearStudioCollectionPreview("Preview cleared when leaving Collection.");
        }
        _studioHost.Content=target;
        _studioActivePage=page;
        // A Collection preview may have superseded Focus's queued decode;
        // refresh its own current-photo request through the SAME serial worker.
        if (page==StudioPage.Focus && _work.Current is { } focusPhoto)
            RequestPreview(focusPhoto);
        _heldKeys.Clear();
        foreach (var (destination, tab) in _studioTabs)
        {
            // Tools is a navigation group: Routes, Photo tools and Diagnostics
            // all retain a visible selected destination in the primary header.
            var current = destination == page ||
                (destination == StudioPage.Routes &&
                 (page == StudioPage.PhotoTools || page == StudioPage.Diagnostics));
            if (current)
            {
                if (!tab.Classes.Contains("studio-current")) tab.Classes.Add("studio-current");
            }
            else tab.Classes.Remove("studio-current");
        }
        if (page==StudioPage.Collection) RefreshStudioCollection();
        if (page==StudioPage.History || page==StudioPage.Changes) RefreshStudioRecordedCopyHistory();
    }

    private void UpdateStudioCollectionSurface()
    {
        if (_studioCollectionGalleryPanel is not null)
            _studioCollectionGalleryPanel.IsVisible = _studioCollectionSurface == StudioCollectionSurface.Gallery;
        if (_studioCollectionInspectorPanel is not null)
            _studioCollectionInspectorPanel.IsVisible = _studioCollectionSurface == StudioCollectionSurface.Inspector;
        if (_studioCollectionShowGallery is not null)
            _studioCollectionShowGallery.IsEnabled = _studioCollectionSurface != StudioCollectionSurface.Gallery;
        if (_studioCollectionShowInspector is not null)
            _studioCollectionShowInspector.IsEnabled = _studioCollectionSurface != StudioCollectionSurface.Inspector;
    }

    private void ShowStudioCollectionSurface(StudioCollectionSurface surface)
    {
        if (surface is not (StudioCollectionSurface.Gallery or StudioCollectionSurface.Inspector))
            throw new ArgumentOutOfRangeException(nameof(surface));
        if (_studioActivePage != StudioPage.Collection || _busy || !_storeHealthy ||
            _studioEmbeddedLease.Active || _studioRenameLease.Active || _studioRejectLease.Active ||
            surface == _studioCollectionSurface) return;
        if (surface == StudioCollectionSurface.Gallery)
            ClearStudioCollectionPreview("Gallery open. Select Inspector for larger previews.");
        else
        {
            ClearStudioContactSheet("Gallery previews released when opening Inspector.");
            RenderStudioContactSheet(); // no image decoding
        }
        _studioCollectionSurface = surface;
        UpdateStudioCollectionSurface();
        // Prevent Gallery/Inspector tabs being clipped above a retained page
        // offset after switching. Does not touch session, preview worker or disk.
        if (_studioCollectionScroll is not null)
            _studioCollectionScroll.Offset = new Vector(0,0);
    }

    private void RefreshStudioRecordedCopyHistory()
    {
        if (_studioHistorySummary is null || _studioCopyHistoryCount is null ||
            _studioCopyHistoryRows is null) return;
        try
        {
            if (!_storeHealthy || _sessionStore is null)
            {
                _studioHistorySummary.Text = "Saved operation history is unavailable. " +
                    "Do not continue file operations; preserve the application state.";
                _studioCopyHistoryCount.Text = "Recorded Copy results unavailable.";
                _studioCopyHistoryRows.Children.Clear();
                SetStudioHistoryUnavailable("Recorded history is unavailable. Preserve the saved session and recovery data.");
                return;
            }
            // Only the persisted journal is read here, with no Reconcile,
            // independent evidence check, authorization, or filesystem write.
            var persisted = _sessionStore.CopyItems(_work.SessionId);
            var view = StudioCopyHistoryPresentation.Create(persisted);
            // Legacy Copy diagnostic retained, not the four-family headline.
            _studioHistorySummary.Text = view.HistorySummary;
            _studioCopyHistoryCount.Text = view.ChangesSummary;
            _studioCopyHistoryRows.Children.Clear();
            // Display ALL journal entries in History; six rows below are only
            // the concise Changes preview, never the complete operation ledger.
            _studioHistoryAll = StudioHistoryBrowserPresentation.Project(persisted);
            // The four families have distinct journals. This browser only
            // reads rows for the active session and never grants continuation.
            // Foreign sessions remain binding through the existing native guards.
            var moves = _sessionStore.MoveItems(_work.SessionId);
            var renames = _sessionStore.RenameItems(_work.SessionId);
            var rejects = _sessionStore.RejectItems()
                .Where(x=>x.SessionId==_work.SessionId).ToArray();
            _studioFamilyHistoryAll = StudioFamilyHistoryPresentation.Project(
                _work.SessionId,persisted,moves,renames,rejects);
            _studioHistorySummary.Text=StudioHistoryExperiencePresentation.Summary(_studioFamilyHistoryAll);
            if (_studioChangesCopyDiagnostics is not null)
                _studioChangesCopyDiagnostics.IsVisible =
                    StudioHistoryExperiencePresentation.ShowCopyDiagnostics(_studioFamilyHistoryAll);
            RefreshStudioFamilyHistory();
            if (_studioChangesOperationsCount is not null)
                _studioChangesOperationsCount.Text=StudioOperationOverview.Summary(_studioFamilyHistoryAll);
            if (_studioChangesOperationsRows is not null)
            {
                _studioChangesOperationsRows.Children.Clear();
                if (_studioFamilyHistoryAll.Length > 0)
                    _studioChangesOperationsRows.Children.Add(
                        Muted(StudioHistoryInsightPresentation.SessionBoundary(_studioFamilyHistoryAll)));
                foreach (var label in StudioOperationOverview.Recent(_studioFamilyHistoryAll))
                    _studioChangesOperationsRows.Children.Add(Muted(label));
            }
            UpdateStudioHistoryEmptyState(_studioFamilyHistoryAll.Length, true);
            if (_studioCopyHistoryNextStep is not null)
                _studioCopyHistoryNextStep.Text =
                    StudioHistoryBrowserPresentation.SuggestedNextStep(_studioHistoryAll);
            RefreshStudioHistoryBrowser();
            foreach (var operation in view.Recent)
            {
                var item = new StackPanel { Spacing=4, Margin=new Thickness(0,4,0,8) };
                item.Children.Add(new TextBlock { Text=operation.Title,
                    FontWeight=FontWeight.SemiBold, TextWrapping=TextWrapping.Wrap,
                    Foreground=StudioPalette.Text });
                item.Children.Add(Muted(operation.Subtitle));
                item.Children.Add(Muted(operation.HistoricalStatus));
                _studioCopyHistoryRows.Children.Add(item);
            }
        }
        catch (Exception ex)
        {
            // Stale or unreadable journal information is not a success state.
            _studioHistorySummary.Text = "Recorded history unavailable: " + ex.Message +
                ". Preserve the saved session and inspect recovery.";
            _studioCopyHistoryCount.Text = "Copy results unavailable; no verification implied.";
            if (_studioChangesOperationsCount is not null)
                _studioChangesOperationsCount.Text="Operation history unavailable. Preserve SQLite and recovery; no native operation authorized.";
            _studioChangesOperationsRows?.Children.Clear();
            _studioCopyHistoryRows.Children.Clear();
            SetStudioHistoryUnavailable("Recorded history cannot be loaded. Preserve SQLite and recovery.");
        }
    }

    private void UpdateStudioHistoryEmptyState(int count, bool healthy)
    {
        var empty = healthy && StudioExperiencePresentation.ShowEmptyHistory(count,true);
        if (_studioHistoryEmptyPanel is not null) _studioHistoryEmptyPanel.IsVisible = empty;
        if (_studioHistorySummary is not null) _studioHistorySummary.IsVisible = !empty;
        if (_studioFamilyHistoryCount is not null) _studioFamilyHistoryCount.IsVisible = !empty;
        if (_studioFamilyHistoryTriage is not null) _studioFamilyHistoryTriage.IsVisible = !empty;
        // On failure, keep read-only diagnostic detail visible instead of
        // showing a cheerful blank-state card or hiding an error.
        if (_studioHistoryPopulatedPanel is not null)
            _studioHistoryPopulatedPanel.IsVisible = !empty;
        if (_studioHistoryLegacyBrowser is not null)
            _studioHistoryLegacyBrowser.IsVisible = !empty && _studioHistoryAll.Length > 0;
    }

    private void SetStudioHistoryUnavailable(string reason)
    {
        // An unreadable journal is not an empty or healthy history.
        UpdateStudioHistoryEmptyState(-1, false);
        _studioHistoryAll = [];
        _studioHistoryShown = [];
        _studioFamilyHistoryAll = [];
        _studioFamilyHistoryShown = [];
        if (_studioFamilyHistoryCount is not null) _studioFamilyHistoryCount.Text = reason;
        if (_studioFamilyHistoryList is not null) _studioFamilyHistoryList.ItemsSource = Array.Empty<string>();
        if (_studioFamilyHistoryDetails is not null) _studioFamilyHistoryDetails.Text = reason;
        if (_studioFamilyHistorySelectedInsight is not null)
            _studioFamilyHistorySelectedInsight.Text = "HISTORY UNAVAILABLE - " + reason;
        if (_studioFamilyHistoryTriage is not null) _studioFamilyHistoryTriage.Text = reason;
        if (_studioFamilyHistoryCopyDetails is not null) _studioFamilyHistoryCopyDetails.IsEnabled = false;
        if (_studioFamilyHistoryClipboardStatus is not null) _studioFamilyHistoryClipboardStatus.Text = "";
        if (_studioCopyHistoryNextStep is not null) _studioCopyHistoryNextStep.Text = reason;
        if (_studioChangesCopyDiagnostics is not null)
            _studioChangesCopyDiagnostics.IsVisible = false;
        if (_studioChangesOperationsCount is not null) _studioChangesOperationsCount.Text=reason;
        _studioChangesOperationsRows?.Children.Clear();
        if (_studioHistoryBrowserCount is not null) _studioHistoryBrowserCount.Text = reason;
        if (_studioHistoryBrowserList is not null) _studioHistoryBrowserList.ItemsSource = Array.Empty<string>();
        if (_studioHistoryBrowserDetail is not null) _studioHistoryBrowserDetail.Text =
            "No verified current-file evidence. " + reason;
    }

    private void RefreshStudioHistoryBrowser()
    {
        if (_studioHistoryBrowserCount is null || _studioHistoryBrowserList is null ||
            _studioHistoryBrowserDetail is null || _studioHistoryBrowserFilter is null) return;
        var prior = _studioHistoryBrowserList.SelectedIndex;
        Guid? selected = prior >= 0 && prior < _studioHistoryShown.Length
            ? _studioHistoryShown[prior].Id : null;
        _studioHistoryShown = StudioHistoryBrowserPresentation.Filter(
            _studioHistoryAll, _studioHistoryBrowserFilter.SelectedIndex < 0 ? 0 :
            _studioHistoryBrowserFilter.SelectedIndex, _studioHistoryBrowserSearch?.Text);
        _studioHistoryBrowserCount.Text = StudioHistoryBrowserPresentation.CountLabel(
            _studioHistoryAll, _studioHistoryShown.Length);
        _studioHistoryBrowserList.ItemsSource = _studioHistoryShown.Select(x=>x.Display).ToArray();
        // Do not silently switch to a different record after a journal refresh.
        int index = selected is null ? -1 : Array.FindIndex(_studioHistoryShown, x=>x.Id==selected);
        _studioHistoryBrowserList.SelectedIndex = index;
        _studioHistoryBrowserDetail.Text = index >= 0
            ? _studioHistoryShown[index].ExactDetails
            : "Select a recorded Copy to inspect its exact paths. " +
              "Historical status does not prove current file or archive identity.";
    }

    private void RefreshStudioFamilyHistory()
    {
        if (_studioFamilyHistoryCount is null || _studioFamilyHistoryList is null ||
            _studioFamilyHistoryDetails is null || _studioFamilyHistoryFilter is null) return;
        int prior=_studioFamilyHistoryList.SelectedIndex;
        (StudioNativeFamily,Guid)? selected=prior>=0 && prior<_studioFamilyHistoryShown.Length
            ? (_studioFamilyHistoryShown[prior].Family,_studioFamilyHistoryShown[prior].Id) : null;
        _studioFamilyHistoryShown=StudioFamilyHistoryPresentation.Filter(_studioFamilyHistoryAll,
            Math.Max(0,_studioFamilyHistoryFilter.SelectedIndex),
            Math.Max(0,_studioFamilyHistoryOutcome?.SelectedIndex ?? 0),_studioFamilyHistorySearch?.Text);
        _studioFamilyHistoryCount.Text=StudioFamilyHistoryPresentation.CountLabel(
            _studioFamilyHistoryAll,_studioFamilyHistoryShown.Length);
        _studioFamilyHistoryList.ItemsSource=_studioFamilyHistoryShown.Select(x=>x.Display).ToArray();
        if (_studioFamilyHistoryTriage is not null)
            _studioFamilyHistoryTriage.Text=StudioFamilyHistoryPresentation.Triage(_studioFamilyHistoryAll);
        int index=StudioFamilyHistoryPresentation.SelectedIndex(_studioFamilyHistoryShown,
            selected?.Item1,selected?.Item2);
        _studioFamilyHistoryList.SelectedIndex=index;
        _studioFamilyHistoryDetails.Text=index>=0 ? _studioFamilyHistoryShown[index].ExactDetails :
            "Choose a recorded operation. Historical state is never current file verification.";
        if (_studioFamilyHistorySelectedInsight is not null)
            _studioFamilyHistorySelectedInsight.Text=index>=0
                ? StudioHistoryInsightPresentation.For(_studioFamilyHistoryShown[index]).Display
                : "Select a recorded operation to learn its family-specific meaning. " +
                  StudioHistoryInsightPresentation.NotVerified;
        if (_studioFamilyHistoryCopyDetails is not null) _studioFamilyHistoryCopyDetails.IsEnabled=index>=0;
        if (_studioFamilyHistoryClipboardStatus is not null) _studioFamilyHistoryClipboardStatus.Text="";
    }

    private CollectionDecisionRow? CurrentStudioCollectionSelection()
    {
        int index = _studioCollectionList?.SelectedIndex ?? -1;
        return index >= 0 && index < _studioCollectionVisible.Length
            ? _studioCollectionVisible[index] : null;
    }

    private void ClearStudioCollectionPreview(string reason)
    {
        // An already-running ImageMagick decode cannot be interrupted.
        // Advancing the generation makes its eventual callback stale.
        if (_studioCollectionPreviewTicket is not null)
            ++_viewGeneration;
        _studioCollectionPreviewTicket = null;
        _studioCollectionDisplayedTicket=null;
        if (_studioCollectionPreview is not null) _studioCollectionPreview.Source = null;
        if (_studioInspectorEmptyState is not null)
        {
            _studioInspectorEmptyState.Text=StudioInspectorEmptyPresentation.Message(reason);
            _studioInspectorEmptyState.IsVisible=true;
        }
        _studioCollectionPreviewBitmap?.Dispose();
        _studioCollectionPreviewBitmap = null;
        if (_studioCollectionPreviewStatus is not null)
            _studioCollectionPreviewStatus.Text = reason;
    }

    // One click means ONE identity-checked step and ONE preview request.
    // Selection changes alone, search/filter refreshes and page entry never
    // initiate decoding. Reuse the existing single bounded PreviewWorker.
    private void StepStudioCollectionPreview(int direction)
    {
        if (_studioActivePage != StudioPage.Collection || _busy || !_storeHealthy ||
            _studioEmbeddedLease.Active) return;
        try
        {
            var selected = CurrentStudioCollectionSelection() ??
                throw new InvalidDataException("Select one photograph to begin reviewing.");
            var snapshot = CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            var next = StudioCollectionReviewDeck.Adjacent(
                _studioCollectionVisible, selected, snapshot.Rows, direction);
            if (next is null)
            {
                if (_studioCollectionPreviewStatus is not null)
                    _studioCollectionPreviewStatus.Text = "End of the filtered Collection. No photo or decision changed.";
                return;
            }
            if (_studioCollectionList is null)
                throw new InvalidOperationException("The photographic Collection is unavailable.");
            // Index is used only AFTER exact GUID/path/state/revision-sensitive
            // presentation validation. Native source version is checked again
            // by LoadStudioCollectionPreview and the existing decoder.
            _studioCollectionList.SelectedIndex = next.Index;
            LoadStudioCollectionPreview();
        }
        catch (Exception ex)
        {
            ClearStudioCollectionPreview("Review step refused: " + ex.Message);
            if (_studioCollectionActionFeedback is not null)
                _studioCollectionActionFeedback.Text = "Selection changed: " + ex.Message;
            RefreshStudioCollection();
        }
    }

    private void LoadStudioCollectionPreview()
    {
        if (_studioActivePage!=StudioPage.Collection || _busy || !_storeHealthy ||
            _studioEmbeddedLease.Active) return;
        ClearStudioContactSheet("Single-photo preview takes precedence over contact-sheet loading.");
        RenderStudioContactSheet(); // only placeholders; never auto-decode
        ClearStudioCollectionPreview("Checking the selected original…");
        try
        {
            var selection = CurrentStudioCollectionSelection() ??
                throw new InvalidDataException("Select one photograph first.");
            var snapshot=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            var fresh=StudioCollectionLens.RequireFresh(selection,snapshot.Rows);
            var photos=_work.Photos.Where(x=>x.Id==fresh.PhotoId).ToArray();
            if(photos.Length!=1)
                throw new InvalidDataException("Selected photo no longer has one imported identity.");
            var original=photos[0];
            // On-demand, non-mutating file metadata inspection. The existing
            // PreviewWorker checks again BEFORE and AFTER native decoding.
            var info=new FileInfo(original.Path);
            if (!info.Exists || info.Length!=original.Length ||
                info.LastWriteTimeUtc.Ticks!=original.LastWriteUtcTicks)
                throw new IOException("Original is missing or changed since import. Reimport before previewing.");
            long generation=++_viewGeneration;
            var ticket=StudioCollectionPreviewTicket.Issue(_work.SessionId,fresh,original,generation);
            _studioCollectionPreviewTicket=ticket;
            if (_studioInspectorEmptyState is not null)
            {
                _studioInspectorEmptyState.Text="Loading one read-only photograph…";
                _studioInspectorEmptyState.IsVisible=true;
            }
            if (_studioCollectionPreviewStatus is not null)
                _studioCollectionPreviewStatus.Text="Loading a bounded read-only preview…";
            _previewWorker.Enqueue(new PreviewWorker.Request(original.Id, original.Path,
                original.Length, original.LastWriteUtcTicks,generation));
        }
        catch(Exception ex)
        {
            ClearStudioCollectionPreview("Preview unavailable: "+ex.Message+
                " No original was modified; choose another photograph or inspect recovery.");
        }
    }

    private bool AcceptStudioCollectionPreview(PreviewWorker.Request request, Bitmap? loaded, string? error)
    {
        var ticket = _studioCollectionPreviewTicket;
        if (ticket is null || request.Generation != ticket.Generation) return false;
        // This is the ONE owner of this callback, including failed/late results.
        _studioCollectionPreviewTicket=null;
        try
        {
            var selected=CurrentStudioCollectionSelection();
            var snapshot=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            var photos=_work.Photos.Where(x=>x.Id==request.PhotoId).ToArray();
            bool authorized=_studioActivePage==StudioPage.Collection && !_busy && _storeHealthy &&
                photos.Length==1 && ticket.Matches(_work.SessionId,selected,snapshot.Rows,
                    photos[0],request);
            if (!authorized || error is not null || loaded is null)
            {
                loaded?.Dispose();
                if (_studioInspectorEmptyState is not null)
                {
                    _studioInspectorEmptyState.Text="Preview unavailable. Original unchanged. Check status below.";
                    _studioInspectorEmptyState.IsVisible=true;
                }
                if (_studioCollectionPreviewStatus is not null)
                    _studioCollectionPreviewStatus.Text= authorized
                        ? "Preview could not be decoded: "+(error ?? "Unknown image error")
                        : "Preview discarded: selection or saved image identity changed.";
                return true;
            }
            _studioCollectionPreviewBitmap?.Dispose();
            _studioCollectionPreviewBitmap=loaded;
            _studioCollectionDisplayedTicket=ticket;
            if (_studioCollectionPreview is not null) _studioCollectionPreview.Source=loaded;
            if (_studioInspectorEmptyState is not null) _studioInspectorEmptyState.IsVisible=false;
            if (_studioCollectionPreviewStatus is not null)
                _studioCollectionPreviewStatus.Text="Read-only preview · " +
                    loaded.PixelSize.Width+" × "+loaded.PixelSize.Height+
                    " · compressed fit image, not an edit or current recovery verification.";
        }
        catch(Exception ex)
        {
            loaded?.Dispose();
            if (_studioInspectorEmptyState is not null)
            {
                _studioInspectorEmptyState.Text="Preview discarded. Original unchanged. Check status below.";
                _studioInspectorEmptyState.IsVisible=true;
            }
            if (_studioCollectionPreviewStatus is not null)
                _studioCollectionPreviewStatus.Text="Preview discarded: "+ex.Message;
        }
        return true;
    }

    private void RefreshStudioCollection()
    {
        if (_studioActivePage!=StudioPage.Collection || _studioCollectionList is null ||
            _studioCollectionFilter is null || _studioCollectionSummary is null) return;
        try
        {
            var prior=_studioCollectionList.SelectedIndex;
            CollectionDecisionRow? identity=prior>=0 && prior<_studioCollectionVisible.Length
                ? _studioCollectionVisible[prior] : null;
            var overview=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            var filtered=CollectionDecisionFilter.Apply(overview.Rows.ToArray(),
                Math.Max(0,_studioCollectionFilter.SelectedIndex),_studioCollectionSearch?.Text);
            bool changed=!_studioCollectionVisible.SequenceEqual(filtered);
            _studioCollectionVisible=filtered;
            // Keep the selected photo and decoded preview on unrelated UI refreshes.
            // Do not rebuild the ListBox's virtualized item source if the saved
            // photograph identity+decision sequence is byte-for-byte equivalent.
            if (changed)
                _studioCollectionList.ItemsSource=filtered.Select(x=>x.Display).ToArray();
            int selected=-1;
            if(identity is not null)
            {
                selected=Array.FindIndex(filtered,x=>x.PhotoId==identity.PhotoId);
                if(selected>=0)
                {
                    try { StudioCollectionLens.RequireFresh(identity,new[]{filtered[selected]}); }
                    catch(InvalidDataException) { selected=-1; }
                }
            }
            if (_studioCollectionList.SelectedIndex!=selected)
                _studioCollectionList.SelectedIndex=selected;
            if (changed || _studioSheetPage is null || _studioSheetSessionId != _work.SessionId)
            {
                ClearStudioContactSheet("Filtered Collection changed. Thumbnails must be requested again.");
                _studioSheetOffset=0;
                RenderStudioContactSheet();
            }
            _studioCollectionSummary.Text=$"{overview.Rows.Count} photographs · {overview.Assigned} folder choices · {overview.Unassigned} awaiting choice" +
                (filtered.Length==overview.Rows.Count ? "" : $" · {filtered.Length} in view");
            if(_studioCollectionProgress is not null)
                _studioCollectionProgress.Text=overview.Rows.Count==0
                    ? "Add photos to get started."
                    : "Folder choices are saved here. Review them in Changes before copying or moving files.";
            if(_studioCollectionRoutes is not null)
            {
                _studioCollectionRoutes.Text=StudioCollectionLens.Routes(overview);
                _studioCollectionRoutes.IsVisible=!_studioCollectionRoutes.Text.StartsWith(
                    "No destination choices",StringComparison.Ordinal);
            }
            if(_studioCollectionNext is not null)
                _studioCollectionNext.IsEnabled=StudioCollectionLens.NextUndecided(overview.Rows) is not null &&
                    !_busy && _storeHealthy;
            RefreshStudioCollectionSelected();
        }
        catch(Exception ex)
        {
            ClearStudioContactSheet("Collection unavailable; thumbnails cleared.");
            _studioCollectionVisible=[];
            _studioCollectionList.ItemsSource=Array.Empty<string>();
            _studioCollectionList.SelectedIndex=-1;
            _studioCollectionSummary.Text="Collection cannot be displayed: "+ex.Message+
                ". No file changes authorized.";
            if(_studioCollectionProgress is not null) _studioCollectionProgress.Text="Collection inspection unavailable.";
            if(_studioCollectionRoutes is not null)
            {
                _studioCollectionRoutes.Text="Saved choices unavailable.";
                _studioCollectionRoutes.IsVisible=true;
            }
            if(_studioCollectionNext is not null) _studioCollectionNext.IsEnabled=false;
            RefreshStudioCollectionSelected();
        }
    }

    private void RefreshStudioCollectionSelected()
    {
        var index=_studioCollectionList?.SelectedIndex ?? -1;
        var row=index>=0 && index<_studioCollectionVisible.Length ? _studioCollectionVisible[index] : null;
        if(_studioCollectionExactSelection is not null)
            _studioCollectionExactSelection.Text=row is null
                ? "Choose a photograph to inspect the exact imported path and saved choice. No files are changed."
                : StudioCollectionLens.ExactSelection(row);
        var previewIdentity=_studioCollectionPreviewTicket ?? _studioCollectionDisplayedTicket;
        if (previewIdentity is not null &&
            (row is null || !StudioCollectionPreviewGateSelectionMatches(previewIdentity,row)))
            ClearStudioCollectionPreview("Selection changed. Choose Preview to inspect this photo.");
        if(_studioCollectionOpen is not null)
            _studioCollectionOpen.IsEnabled=row is not null && !_busy && _storeHealthy;
        if (_studioCollectionReviewPosition is not null)
            _studioCollectionReviewPosition.Text = StudioCollectionReviewDeck.Position(
                _studioCollectionVisible,index);
        if (_studioCollectionPreviewPrevious is not null)
            _studioCollectionPreviewPrevious.IsEnabled = row is not null && index > 0 &&
                !_busy && _storeHealthy && !_studioEmbeddedLease.Active;
        if (_studioCollectionPreviewNext is not null)
            _studioCollectionPreviewNext.IsEnabled = row is not null &&
                index < _studioCollectionVisible.Length-1 && !_busy && _storeHealthy &&
                !_studioEmbeddedLease.Active;
        if(_studioCollectionPreviewLoad is not null)
            _studioCollectionPreviewLoad.IsEnabled=row is not null && !_busy && _storeHealthy &&
                !_studioEmbeddedLease.Active;
        UpdateStudioSheetButtons();
    }

    private static bool StudioCollectionPreviewGateSelectionMatches(
        StudioCollectionPreviewTicket ticket,CollectionDecisionRow current)
    {
        try { StudioCollectionLens.RequireFresh(ticket.Selection,new[]{current}); return true; }
        catch(InvalidDataException) { return false; }
    }

    private void OpenStudioCollectionPhoto(bool nextUndecided)
    {
        if(_busy || !_storeHealthy || !EnsureViewerNavigationAllowed()) return;
        try
        {
            var fresh=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            CollectionDecisionRow? selected;
            if(nextUndecided)
                selected=StudioCollectionLens.NextUndecided(fresh.Rows);
            else
            {
                int index=_studioCollectionList?.SelectedIndex ?? -1;
                if(index<0 || index>=_studioCollectionVisible.Length) return;
                selected=StudioCollectionLens.RequireFresh(_studioCollectionVisible[index],fresh.Rows);
            }
            if(selected is null) return;
            _work.JumpToPhoto(selected.Position);
            Refresh();
            ShowStudioPage(StudioPage.Focus);
        }
        catch(Exception ex)
        {
            if(_studioCollectionActionFeedback is not null)
                _studioCollectionActionFeedback.Text="Photo selection changed or navigation refused: "+ex.Message;
            Message("Collection navigation refused. Select the photograph again. "+ex.Message);
            RefreshStudioCollection();
        }
    }

    private void InstallStudioPages(
        StackPanel pickers, Grid previousLeft, Border photoSurface,
        WrapPanel navButtons, Expander morePhotoTools, WrapPanel extraPhotoActions,
        StackPanel oldSidebar, StackPanel routeSettings, StackPanel reviewDetails,
        StackPanel advancedTools, StackPanel recoveryActions, StackPanel actionDock,
        Grid priorSideShell)
    {
        // Never attach a control to two parents. Every move is deliberate and
        // occurs exactly once at startup, not on refresh/navigation.
        Detach(previousLeft,pickers);
        if (!previousLeft.Children.Remove(photoSurface) ||
            !previousLeft.Children.Remove(_photoLabel))
            throw new InvalidOperationException("Studio viewer controls missing");
        Detach(oldSidebar,_guidedHeadline);
        Detach(oldSidebar,_quickRoutes);
        Detach(oldSidebar,_guidedNextStep);
        Detach(oldSidebar,_modeStatus);
        Detach(oldSidebar,_progress);
        if (!priorSideShell.Children.Remove(actionDock))
            throw new InvalidOperationException("Studio review controls missing");
        Detach(reviewDetails,_plan);
        if (!recoveryActions.Children.Remove(_inspectRecovery) ||
            !recoveryActions.Children.Remove(_collisionMode))
            throw new InvalidOperationException("Studio existing recovery/settings controls missing");
        // M4A: relocate the existing ordinary Rename controls (not copies of
        // their event handlers) from the recovery miscellany into Changes.
        // Keep the independently verified native Rename closeout in History.
        foreach (var nativeRename in new[]{_auditRename,_renameFixture,_applyRename,_undoRename})
            Detach(recoveryActions,nativeRename);
        // Only ordinary Reject moves to Changes. All generated-fixture / test
        // controls and their existing safeguards remain inside Diagnostics.
        Detach(advancedTools,_ordinaryFirstRejectExpander);
        if (!advancedTools.Children.Remove(recoveryActions))
            throw new InvalidOperationException("Studio advanced recovery controls missing");
        _recoveryAndFileTools.Content=null;
        if (morePhotoTools.Content is not null)
            morePhotoTools.Content=null;

        // Move the entire real route editor (36-key support, absolute picker and
        // Settings.xml import). The old Expander will never appear in Focus.
        var routeExpander=oldSidebar.Children.OfType<Expander>()
            .FirstOrDefault(x=>ReferenceEquals(x.Content,routeSettings))
            ?? throw new InvalidOperationException("Studio destination editor missing");
        routeExpander.Content=null;

        // Unlink existing button instances from the old navigation WrapPanel.
        foreach(var button in new[]{_prev,_next,_nextUnassigned,_markReject,
            _undoReject,_reviewRejectQueue,_help,_decisionCenter})
            if (!navButtons.Children.Remove(button))
                throw new InvalidOperationException("Studio navigation command missing: "+button.Content);

        var oldDiagnostics=oldSidebar.Children.OfType<Expander>()
            .FirstOrDefault(x=>x.Header?.ToString()=="Diagnostics and licensing");
        if (oldDiagnostics?.Content is not StackPanel oldDiagnosticPanel ||
            !oldDiagnosticPanel.Children.Remove(_previewMetrics))
            throw new InvalidOperationException("Studio diagnostics information missing");

        var sharedShell = new Grid
        {
            RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto"),
            Background=StudioPalette.Shell
        };
        var header = new WrapPanel { Orientation=Orientation.Horizontal,
            Margin=new Thickness(22,12,18,10), VerticalAlignment=VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text="PhotoSift Reborn", Foreground=StudioPalette.Text,
            FontWeight=FontWeight.SemiBold,FontSize=20,
            VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,22,4)
        });
        Button AddTab(StudioPage page,string caption)
        {
            var button=Button(caption,()=>ShowStudioPage(page));
            button.MinHeight=39; button.Margin=new Thickness(0,0,8,4);
            button.Classes.Add("studio-nav");
            _studioTabs.Add(page,button);
            header.Children.Add(button);
            return button;
        }
        _studioNavigationHeader = header;
        AddTab(StudioPage.Focus,"Focus");
        _studioCollection=AddTab(StudioPage.Collection,"Collection");
        _studioChanges=AddTab(StudioPage.Changes,"Changes");
        _studioHistory=AddTab(StudioPage.History,"History & recovery");
        // M6H: one discoverable entry for choosing individual files OR a folder.
        // The existing AddFilesAsync/AddInputAsync paths retain their own gates.
        _studioAdd=Button("Add photos",(Func<Task>)(async ()=>await ShowStudioPhotoSourceChoiceAsync()));
        _studioAdd.Classes.Add("studio-primary");
        _studioAdd.Margin=new Thickness(0,0,8,4);
        ToolTip.SetTip(_studioAdd,"Choose individual photographs or an existing folder. No originals are modified on import.");
        header.Children.Add(_studioAdd);
        _studioChooseOutput=Button("Results folder",(Func<Task>)(async ()=>await ChooseOutputAsync()));
        _studioChooseOutput.Margin=new Thickness(0,0,8,4);
        _studioChooseOutput.MinHeight=39;
        ToolTip.SetTip(_studioChooseOutput,"Select a different local results folder; this only saves a path. No Copy or Move occurs.");
        header.Children.Add(_studioChooseOutput);
        AddTab(StudioPage.Routes,"Tools & settings");
        sharedShell.Children.Add(header);

        // The genuine current-operation status is present ABOVE every page.
        // Preserve actual status and startup failures, even if Focus is not open.
        var sharedStatus = new StackPanel { Spacing=3,
            Margin=new Thickness(24,0,22,7) };
        Detach(pickers,_startupFailureBanner);
        sharedStatus.Children.Add(_startupFailureBanner);
        var statusScroll=pickers.Children.OfType<ScrollViewer>()
            .FirstOrDefault(x=>ReferenceEquals(x.Content,_status))
            ?? throw new InvalidOperationException("Studio status display missing");
        Detach(pickers,statusScroll);
        sharedStatus.Children.Add(statusScroll);
        Grid.SetRow(sharedStatus,1);sharedShell.Children.Add(sharedStatus);
        Grid.SetRow(_studioHost,2);sharedShell.Children.Add(_studioHost);
        var footer = Muted("Before changing files, review the exact source and destination.");
        footer.FontSize=12; footer.Margin=new Thickness(24,2,24,8);
        Grid.SetRow(footer,3);sharedShell.Children.Add(footer);

        // FOCUS: a photo-dominant page with a short route rail and one decision
        // shelf. The giant nested scroll/expander tree is NOT attached here.
        var focus = new Grid { ColumnDefinitions=new ColumnDefinitions("*,272"),
            Margin=new Thickness(22,2,22,9) };
        var focusBody=new Grid { RowDefinitions=new RowDefinitions("Auto,*,Auto") };
        _photoLabel.Margin=new Thickness(3,0,3,0);
        Grid.SetRow(_photoLabel,0);
        focusBody.Children.Add(_photoLabel);
        photoSurface.MinHeight=165;
        Grid.SetRow(photoSurface,1);focusBody.Children.Add(photoSurface);
        var shelf = new WrapPanel { Orientation=Orientation.Horizontal,
            VerticalAlignment=VerticalAlignment.Center };
        foreach (var button in new[]{_prev,_next,_nextUnassigned,_markReject})
        { button.Margin=new Thickness(0,0,8,5);shelf.Children.Add(button); }
        var toChanges=Button("Review saved choices →",()=>ShowStudioPage(StudioPage.Changes));
        toChanges.Classes.Add("studio-primary");
        toChanges.Margin=new Thickness(0,0,8,5);shelf.Children.Add(toChanges);
        Grid.SetRow(shelf,2);focusBody.Children.Add(shelf);
        focus.Children.Add(focusBody);
        var routeRail=new StackPanel { Spacing=12 };
        routeRail.Children.Add(StudioTitle("Choose a destination"));
        routeRail.Children.Add(Muted("Press F, T or W to save a folder choice. Your original stays in place."));
        routeRail.Children.Add(_quickRoutes);
        routeRail.Children.Add(_studioResultsHint);
        routeRail.Children.Add(Button("All 36 keys / Edit routes →",()=>ShowStudioPage(StudioPage.Routes)));
        routeRail.Children.Add(_guidedHeadline);
        routeRail.Children.Add(_guidedNextStep);
        var routeBorder=StudioCard(routeRail);
        Grid.SetColumn(routeBorder,1);focus.Children.Add(routeBorder);
        _studioPages.Add(StudioPage.Focus,focus);
        // Responsive composition: keep the image usable at compact actual
        // Avalonia client bounds. The same routes remain available in header.
        void AdaptFocus()
        {
            bool showRail=Bounds.Width>=1120 && Bounds.Height>=610;
            routeBorder.IsVisible=showRail;
            focus.ColumnDefinitions[1].Width=showRail ? new GridLength(272) : new GridLength(0);
        }
        SizeChanged+=(_,_)=>AdaptFocus();
        Opened+=(_,_)=>AdaptFocus();

        // COLLECTION: photo-first saved-decision lens. Snapshot reads only; it
        // cannot approve native filesystem work or silently retarget a photo.
        var collection=new Grid { Margin=new Thickness(24,4,24,16) };
        var collectionContents=new Grid {
            RowDefinitions=new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        collectionContents.Children.Add(StudioTitle("Collection"));
        _studioCollectionSummary=Muted("Photographs and saved folder choices, not copied outputs.");
        Grid.SetRow(_studioCollectionSummary,1);collectionContents.Children.Add(_studioCollectionSummary);
        var collectionProgress=new StackPanel {Spacing=4,Margin=new Thickness(0,5,0,8)};
        _studioCollectionProgress=Muted("Collection progress is based only on saved choices.");
        _studioCollectionRoutes=Muted("No destination choices saved yet.");
        collectionProgress.Children.Add(_studioCollectionProgress);
        collectionProgress.Children.Add(_studioCollectionRoutes);
        Grid.SetRow(collectionProgress,2);collectionContents.Children.Add(collectionProgress);
        var collectionFilters=new WrapPanel {Orientation=Orientation.Horizontal};
        _studioCollectionFilter=new ComboBox { ItemsSource=new[]{"All photographs","Needs a decision","Assigned to a folder","Reject review only"},
            SelectedIndex=0,MinWidth=215,Margin=new Thickness(0,0,10,4) };
        _studioCollectionSearch=new TextBox { PlaceholderText="Search filenames",MinWidth=230,Width=330 };
        collectionFilters.Children.Add(_studioCollectionFilter);
        collectionFilters.Children.Add(_studioCollectionSearch);
        Grid.SetRow(collectionFilters,3);collectionContents.Children.Add(collectionFilters);
        var collectionViewControls = new WrapPanel { Orientation=Orientation.Horizontal,
            Margin=new Thickness(0,7,0,10) };
        _studioCollectionShowGallery=Button("Gallery",()=>ShowStudioCollectionSurface(StudioCollectionSurface.Gallery));
        _studioCollectionShowInspector=Button("Inspector",()=>ShowStudioCollectionSurface(StudioCollectionSurface.Inspector));
        _studioCollectionShowGallery.Margin=new Thickness(0,0,8,0);
        _studioCollectionShowInspector.Margin=new Thickness(0,0,12,0);
        collectionViewControls.Children.Add(_studioCollectionShowGallery);
        collectionViewControls.Children.Add(_studioCollectionShowInspector);
        // Navigation labels are sufficient; no additional warning is needed here.
        Grid.SetRow(collectionViewControls,4);collectionContents.Children.Add(collectionViewControls);
        _studioCollectionList=new ListBox { SelectedIndex=-1,MinHeight=150,Height=264 };
        var collectionGallery=new Grid { ColumnDefinitions=new ColumnDefinitions("*,*"),
            RowDefinitions=new RowDefinitions("Auto,Auto") };
        _studioCollectionList.Margin=new Thickness(0,0,14,0);
        collectionGallery.Children.Add(_studioCollectionList);
        var previewColumn=new StackPanel {Spacing=8};
        previewColumn.Children.Add(new TextBlock { Text="Selected photograph",
            FontSize=17,FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text });
        var previewBed=new Border { Background=StudioPalette.PhotoBed,
            CornerRadius=new CornerRadius(9), Height=254, Padding=new Thickness(8) };
        _studioCollectionPreview=new Image { Stretch=Stretch.Uniform,
            HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch };
        // A real visible empty state lives INSIDE the black photo bed, not
        // below the fold. This overlay is never a photo, file or approval.
        var previewCanvas=new Grid();
        previewCanvas.Children.Add(_studioCollectionPreview);
        _studioInspectorEmptyState=new TextBlock {
            Text=StudioInspectorEmptyPresentation.Message(null),
            Foreground=StudioPalette.Secondary,TextAlignment=TextAlignment.Center,
            TextWrapping=TextWrapping.Wrap,MaxWidth=360,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(20),IsVisible=true };
        previewCanvas.Children.Add(_studioInspectorEmptyState);
        previewBed.Child=previewCanvas;
        previewColumn.Children.Add(previewBed);
        _studioCollectionPreviewLoad=Button("Preview selected photo",LoadStudioCollectionPreview);
        _studioCollectionPreviewLoad.IsEnabled=false;
        previewColumn.Children.Add(_studioCollectionPreviewLoad);
        var deckCommands = new WrapPanel { Orientation=Orientation.Horizontal };
        _studioCollectionPreviewPrevious=Button("← Preview previous",()=>StepStudioCollectionPreview(-1));
        _studioCollectionPreviewNext=Button("Preview next →",()=>StepStudioCollectionPreview(1));
        _studioCollectionPreviewPrevious.IsEnabled=false;
        _studioCollectionPreviewNext.IsEnabled=false;
        deckCommands.Children.Add(_studioCollectionPreviewPrevious);
        deckCommands.Children.Add(_studioCollectionPreviewNext);
        previewColumn.Children.Add(deckCommands);
        _studioCollectionReviewPosition=Muted("Select a photograph to begin reviewing.");
        previewColumn.Children.Add(_studioCollectionReviewPosition);
        _studioCollectionPreviewStatus=Muted("Choose a photograph, then Preview. Image decoding is on demand, one at a time.");
        previewColumn.Children.Add(_studioCollectionPreviewStatus);
        Grid.SetColumn(previewColumn,1);collectionGallery.Children.Add(previewColumn);
        // Compact windows stack Inspector elements; all safety and preview
        // controls remain reachable without half-width horizontal clipping.
        Grid.SetRow(previewColumn,1);
        void AdaptInspector() => StudioAdaptColumns(collectionGallery,previewColumn);
        SizeChanged+=(_,_)=>AdaptInspector();
        Opened+=(_,_)=>AdaptInspector();
        _studioCollectionInspectorPanel=StudioCard(collectionGallery,12);
        Grid.SetRow(_studioCollectionInspectorPanel,5);
        collectionContents.Children.Add(_studioCollectionInspectorPanel);
        // M5D contact sheet: four actual bitmap previews maximum, loaded only
        // by explicit user request, one at a time via existing serial decoder.
        var sheet = new StackPanel {Spacing=8,Margin=new Thickness(0,12,0,10)};
        sheet.Children.Add(new TextBlock {Text="Contact sheet",FontSize=19,
            FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text});
        sheet.Children.Add(Muted("Preview four photographs at a time. Thumbnails load only when requested."));
        _studioSheetPageLabel=Muted("Choose a filtered Collection page.");
        sheet.Children.Add(_studioSheetPageLabel);
        var sheetControls=new WrapPanel {Orientation=Orientation.Horizontal};
        _studioSheetPrevious=Button("← Previous four",()=>StepStudioContactSheet(-1));
        _studioSheetNext=Button("Next four →",()=>StepStudioContactSheet(1));
        _studioSheetLoad=Button("Load four thumbnails",LoadStudioContactSheet);
        _studioSheetLoad.Classes.Add("studio-primary");
        sheetControls.Children.Add(_studioSheetPrevious);
        sheetControls.Children.Add(_studioSheetNext);
        sheetControls.Children.Add(_studioSheetLoad);
        sheet.Children.Add(sheetControls);
        // Each 4-tile page is bounded; all thumbnails are disposed on page changes.
        _studioSheetTiles = new Grid { HorizontalAlignment=HorizontalAlignment.Stretch };
        // Measured gallery panel width is authoritative after layout; resize
        // only existing Image/Border dimensions, never reload any photo bytes.
        _studioSheetTiles.SizeChanged+=(_,_)=>UpdateStudioGalleryTileSizing();
        sheet.Children.Add(_studioSheetTiles);
        _studioSheetStatus=Muted("Contact sheet previews have not been requested.");
        sheet.Children.Add(_studioSheetStatus);
        var sheetCard=StudioCard(sheet);
        SizeChanged+=(_,_)=>UpdateStudioGalleryTileSizing();
        _studioCollectionGalleryPanel=sheetCard;
        Grid.SetRow(sheetCard,6);collectionContents.Children.Add(sheetCard);
        UpdateStudioCollectionSurface();
        _studioCollectionFilter.SelectionChanged+=(_,_)=>RefreshStudioCollection();
        _studioCollectionSearch.PropertyChanged+=(_,e)=>
        { if (e.Property==TextBox.TextProperty) RefreshStudioCollection(); };
        _studioCollectionList.SelectionChanged+=(_,_)=>RefreshStudioCollectionSelected();
        _studioCollectionExactSelection=new TextBox {
            IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            Background=StudioPalette.Surface,Foreground=StudioPalette.Text,
            Text="Choose a photograph to inspect its exact imported path and saved choice.",
            MinHeight=85,MaxHeight=135 };
        ScrollViewer.SetVerticalScrollBarVisibility(_studioCollectionExactSelection,ScrollBarVisibility.Auto);
        var selectionCard=StudioCard(_studioCollectionExactSelection,12);
        selectionCard.Margin=new Thickness(0,6,0,6);
        Grid.SetRow(selectionCard,7);collectionContents.Children.Add(selectionCard);
        var collectionActions=new StackPanel {Spacing=5};
        var actionButtons=new WrapPanel {Orientation=Orientation.Horizontal};
        _studioCollectionOpen=Button("Open selected photo in Focus",()=>OpenStudioCollectionPhoto(false));
        _studioCollectionOpen.IsEnabled=false;
        _studioCollectionNext=Button("Continue with next undecided",()=>OpenStudioCollectionPhoto(true));
        actionButtons.Children.Add(_studioCollectionOpen);
        actionButtons.Children.Add(_studioCollectionNext);
        actionButtons.Children.Add(_decisionCenter);
        collectionActions.Children.Add(actionButtons);
        _studioCollectionActionFeedback=Muted("Review your folder choices in Changes before copying or moving files.");
        collectionActions.Children.Add(_studioCollectionActionFeedback);
        Grid.SetRow(collectionActions,8);collectionContents.Children.Add(collectionActions);
        // Bounded list viewport keeps virtualization alive even while a compact
        // window scrolls the larger Collection page to reach consent-free actions.
        _studioCollectionScroll=StudioScroll(collectionContents);
        collection.Children.Add(_studioCollectionScroll);
        _studioPages.Add(StudioPage.Collection,collection);

        // CHANGES: a real dedicated place for the saved rows, operation mode,
        // and existing separately reviewed final action. Copy uses embedded
        // Review; other native families keep their distinct approved paths.
        var changesBody = new StackPanel { Spacing=14 };
        changesBody.Children.Add(StudioTitle("Changes"));
        changesBody.Children.Add(Muted("Every file operation needs a fresh review of exact paths and explicit approval. Saved choices do not change files."));
        changesBody.Children.Add(_progress);
        changesBody.Children.Add(Muted(StudioHistoryExperiencePresentation.ChangesDecisionBoundary));
        var planned = new StackPanel { Spacing=11 };
        planned.Children.Add(new TextBlock { Text="Saved decisions & review",
            FontWeight=FontWeight.SemiBold, FontSize=18, Foreground=StudioPalette.Text });
        planned.Children.Add(Muted("Choose a route in Focus, then review its exact destination here."));
        _plan.MinHeight=110; _plan.Height=160; _plan.MaxHeight=215;
        planned.Children.Add(_plan);
        _studioChangesEmptyPanel=StudioCard(new StackPanel {Spacing=6,Children={
            new TextBlock {Text="No saved folder decisions yet",FontSize=16,
                FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text},
            Muted("Choose a folder for a photograph in Focus. Review the exact source and destination here before any file action.")
        }},12);
        _studioChangesEmptyPanel.Background=StudioPalette.PhotoBed;
        _studioChangesEmptyPanel.IsVisible=false;
        planned.Children.Add(_studioChangesEmptyPanel);
        // Family-aware recorded snapshot, NEVER fresh filesystem evidence.
        planned.Children.Add(new TextBlock { Text="Recent file operations · all four families", FontSize=16,
            FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        _studioChangesOperationsCount=Muted("Loading recorded Copy, Move, Rename and Reject history...");
        planned.Children.Add(_studioChangesOperationsCount);
        _studioChangesOperationsRows=new StackPanel { Spacing=4 };
        planned.Children.Add(_studioChangesOperationsRows);
        planned.Children.Add(Muted(StudioOperationOverview.EvidenceBoundary));
        var copyDiagnostics=new StackPanel { Spacing=6 };
        _studioCopyHistoryCount=Muted("No completed Copy has been recorded in this session.");
        copyDiagnostics.Children.Add(_studioCopyHistoryCount);
        _studioCopyHistoryNextStep = Muted("Recorded history is not current file proof.");
        copyDiagnostics.Children.Add(_studioCopyHistoryNextStep);
        _studioCopyHistoryRows=new StackPanel { Spacing=2 };
        copyDiagnostics.Children.Add(_studioCopyHistoryRows);
        _studioChangesCopyDiagnostics=new Expander { Header="Copy-specific recorded details (optional)",
            IsExpanded=false, Content=copyDiagnostics,IsVisible=false };
        planned.Children.Add(_studioChangesCopyDiagnostics);
        planned.Children.Add(Button("Inspect all recorded operations →",
            ()=>ShowStudioPage(StudioPage.History)));
        var workActions = new StackPanel { Spacing=12 };
        workActions.Children.Add(new TextBlock { Text="Review and finish",
            FontWeight=FontWeight.SemiBold, FontSize=18, Foreground=StudioPalette.Text });
        workActions.Children.Add(_modeStatus);
        workActions.Children.Add(actionDock);
        var rejectActions=new WrapPanel { Orientation=Orientation.Horizontal };
        rejectActions.Children.Add(_reviewRejectQueue);
        rejectActions.Children.Add(_undoReject);
        workActions.Children.Add(rejectActions);
        var changesActionsCard=StudioCard(workActions);
        changesActionsCard.VerticalAlignment=VerticalAlignment.Top;
        changesBody.Children.Add(StudioTwoPane(StudioCard(planned),changesActionsCard));
        // M4A ordinary-native operation center. These are the SAME controls
        // with the SAME handlers and IsEnabled native gates used before M4A.
        // Moving controls into Changes grants absolutely no extra authority.
        var renamePanel = new StackPanel { Spacing=10 };
        renamePanel.Children.Add(new TextBlock { Text="Rename one photograph",
            FontWeight=FontWeight.SemiBold,FontSize=18,Foreground=StudioPalette.Text });
        var renameInfo=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Rename);
        renamePanel.Children.Add(Muted("Changes a filename, not its image bytes. Exact-path approval and Apply are separate."));
        renamePanel.Children.Add(new Expander { Header="Rename scope and recovery details",
            IsExpanded=false, Content=new StackPanel { Spacing=6, Children={
                Muted(renameInfo.Scope), Muted(renameInfo.OriginalEffect), Muted(renameInfo.ReviewAndConsent),
                Muted(renameInfo.Recovery) } } });
        renamePanel.Children.Add(_auditRename);
        renamePanel.Children.Add(_renameFixture);
        renamePanel.Children.Add(_applyRename);
        renamePanel.Children.Add(_undoRename);
        var rejectPanel = new StackPanel { Spacing=11 };
        rejectPanel.Children.Add(new TextBlock { Text="Reject to owned recovery",
            FontWeight=FontWeight.SemiBold,FontSize=18,Foreground=StudioPalette.Text });
        var rejectInfo=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Reject);
        rejectPanel.Children.Add(Muted("Execute relocates the ORIGINAL into app-owned recovery. Stage and Approve do not move it."));
        rejectPanel.Children.Add(new Expander { Header="Reject scope and recovery details",
            IsExpanded=false, Content=new StackPanel { Spacing=6, Children={
                Muted(rejectInfo.Scope), Muted(rejectInfo.OriginalEffect), Muted(rejectInfo.ReviewAndConsent),
                Muted(rejectInfo.Recovery) } } });
        rejectPanel.Children.Add(_ordinaryFirstRejectExpander);
        changesBody.Children.Add(StudioTwoPane(StudioCard(renamePanel),StudioCard(rejectPanel)));
        var moveInfo=StudioNativeFamilyPresentation.Describe(StudioNativeFamily.Move);
        changesBody.Children.Add(new Expander { Header="Move scope and recovery details",
            IsExpanded=false, Content=new StackPanel { Spacing=6, Children={
                Muted("Select Move explicitly in Review and finish above."),
                Muted(moveInfo.Scope), Muted(moveInfo.ReviewAndConsent), Muted(moveInfo.Recovery) } } });
        var changes=new Grid { Margin=new Thickness(24,4,24,16) };
        // The exact Review task lives inside the Changes page, above and
        // instead of the ordinary action panel. Background controls are
        // both hidden and disabled for the entire asynchronous approval proof.
        var normalChanges = new Grid();
        normalChanges.Children.Add(StudioScroll(changesBody));
        _studioNormalChanges = normalChanges;
        changes.Children.Add(normalChanges);
        _studioReviewContent = new ContentControl();
        _studioReviewPendingMessage = Muted("Finish or cancel this review.");
        var reviewTask = new Grid { RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(0,0,0,6) };
        reviewTask.Children.Add(_studioReviewPendingMessage);
        Grid.SetRow(_studioReviewContent,1);
        reviewTask.Children.Add(_studioReviewContent);
        KeyboardNavigation.SetTabNavigation(reviewTask,KeyboardNavigationMode.Cycle);
        _studioReviewSurface = new Border { Background = StudioPalette.Shell,
            Child = reviewTask, IsVisible = false };
        changes.Children.Add(_studioReviewSurface);
        _studioPages.Add(StudioPage.Changes,changes);

        // HISTORY: one page scroll, two responsive sections. Recorded history
        // and current on-disk verification must never be mistaken for a grant.
        // Keep all accepted guarded commands, but don't render them as huge
        // full-window-width buttons. No hidden recovery or automatic replay.
        var historyBody=new StackPanel { Spacing=13 };
        historyBody.Children.Add(StudioTitle("History & recovery"));
        historyBody.Children.Add(Muted("Recorded results are not proof of what is on disk now. Inspect recovery for current evidence."));
        // Read-only inspection must remain reachable when Changes and all
        // mutation controls are locked, including missing/corrupt R30 anchors.
        _r30Readiness=new TextBox {IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            MinHeight=100,MaxHeight=260,Text="Ordinary mixed-family operations remain locked. Inspect to read generated operation dependencies and recovery holds."};
        historyBody.Children.Add(new Expander {IsExpanded=true,Header="Operation dependencies & readiness (read-only)",
            Content=new StackPanel {Spacing=6,Children={
                Button("Inspect operation readiness",(Func<Task>)InspectR30ReadinessAsync),_r30Readiness}}});
        var recorded=new StackPanel { Spacing=12 };
        recorded.Children.Add(new TextBlock { Text="Recorded history", FontSize=18,
            FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        _studioHistorySummary=Muted("Recorded operations appear here. Inspect recovery for fresh file evidence.");
        recorded.Children.Add(_studioHistorySummary);
        recorded.Children.Add(new TextBlock { Text="Four operation families - historical records", FontSize=16,
            FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        _studioFamilyHistoryCount=Muted("Recorded operation history loading…");
        recorded.Children.Add(_studioFamilyHistoryCount);
        _studioFamilyHistoryTriage=Muted("Recorded status is not a current filesystem check.");
        recorded.Children.Add(_studioFamilyHistoryTriage);
        // Move the SAME already-bound read-only recovery inspector control
        // into immediate reach. This introduces no handler and no new grant.
        _inspectRecovery.HorizontalAlignment=HorizontalAlignment.Left;
        _inspectRecovery.MaxWidth=390;
        recorded.Children.Add(_inspectRecovery);
        recorded.Children.Add(Button("Open recovery command center · all saved sessions →",
            (Func<Task>)(async () => await ShowRecoveryCommandCenterAsync())));
        recorded.Children.Add(Muted("New: inspect installation-wide native evidence, dependency conflicts " +
            "and unfinished journals in one read-only view. No automatic repair or execution."));
        recorded.Children.Add(Muted(StudioHistoryExperiencePresentation.EvidenceBoundary));
        var familyFilters=new WrapPanel { Orientation=Orientation.Horizontal };
        _studioFamilyHistoryFilter=new ComboBox { ItemsSource=new[]{"All families","Copy","Move","Rename","Reject"},
            SelectedIndex=0,MinWidth=150,Margin=new Thickness(0,0,8,6) };
        _studioFamilyHistoryOutcome=new ComboBox { ItemsSource=new[]{
            "All outcomes","Completed (recorded)","Undone (recorded)",
            "Pending Reject proposal","Needs attention"},
            SelectedIndex=0,MinWidth=180,Margin=new Thickness(0,0,8,6) };
        _studioFamilyHistorySearch=new TextBox { PlaceholderText="Search family, path, issue or ID",
            MinWidth=190,Width=290,Margin=new Thickness(0,0,0,6) };
        familyFilters.Children.Add(_studioFamilyHistoryFilter);
        familyFilters.Children.Add(_studioFamilyHistoryOutcome);
        familyFilters.Children.Add(_studioFamilyHistorySearch);
        // Empty-state users do not need active filters for a nonexistent ledger.
        // The filters remain constructed and retain their existing handlers.
        // The populated panel is created immediately below.
        
        _studioHistoryEmptyPanel=StudioCard(new StackPanel { Spacing=8, Children={
            new TextBlock { Text="No recorded operations in this session",
                FontSize=18,FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text },
            Muted("When you finish a reviewed file action, its saved result appears here."),
            Muted("Earlier sessions may still have binding recovery history. Inspect the recovery journal before further file actions.")
        }},12);
        _studioHistoryEmptyPanel.Background=StudioPalette.PhotoBed;
        _studioHistoryEmptyPanel.IsVisible=false;
        recorded.Children.Add(_studioHistoryEmptyPanel);
        _studioHistoryPopulatedPanel=new StackPanel { Spacing=9 };
        var historyLedger=new StackPanel { Spacing=9 };
        historyLedger.Children.Add(new TextBlock { Text="Saved operations - select one",
            FontSize=16,FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text });
        historyLedger.Children.Add(familyFilters);
        _studioFamilyHistoryList=new ListBox { Height=320,MinHeight=130,SelectedIndex=-1 };
        historyLedger.Children.Add(_studioFamilyHistoryList);
        var historySelectedDetails=new StackPanel { Spacing=9 };
        historySelectedDetails.Children.Add(new TextBlock { Text="Selected operation / what it means",
            FontSize=16,FontWeight=FontWeight.SemiBold,Foreground=StudioPalette.Text });
        _studioFamilyHistorySelectedInsight=Muted("Select a recorded operation. " +
            StudioHistoryInsightPresentation.NotVerified);
        historySelectedDetails.Children.Add(StudioCard(_studioFamilyHistorySelectedInsight,12));
        _studioFamilyHistoryDetails=new TextBox { IsReadOnly=true,AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,MinHeight=180,Height=280,
            Text="Select a recorded operation; no current file inspection." };
        ScrollViewer.SetVerticalScrollBarVisibility(_studioFamilyHistoryDetails,ScrollBarVisibility.Auto);
        historySelectedDetails.Children.Add(_studioFamilyHistoryDetails);
        _studioFamilyHistoryCopyDetails=new Button { Content="Copy selected exact recorded details",
            MinHeight=38,IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Left };
        _studioFamilyHistoryClipboardStatus=Muted("Select an operation to copy its recorded details. No approval or file changes.");
        _studioFamilyHistoryCopyDetails.Click+=async (_,_)=>
        {
            int index=_studioFamilyHistoryList?.SelectedIndex ?? -1;
            if(index<0 || index>=_studioFamilyHistoryShown.Length) return;
            var original=_studioFamilyHistoryShown[index];
            // Selection must still refer to the immutable read-only projection;
            // never construct authority or current evidence from clipboard text.
            if(!Array.Exists(_studioFamilyHistoryAll,
                x=>ReferenceEquals(x,original))) return;
            try
            {
                var clipboard=TopLevel.GetTopLevel(this)?.Clipboard;
                if(clipboard is null) throw new InvalidOperationException("Clipboard is unavailable.");
                await clipboard.SetTextAsync(original.ExactDetails);
                if(_studioFamilyHistoryClipboardStatus is not null)
                    _studioFamilyHistoryClipboardStatus.Text="Recorded details copied. This is not current verification or approval.";
            }
            catch(Exception ex)
            {
                if(_studioFamilyHistoryClipboardStatus is not null)
                    _studioFamilyHistoryClipboardStatus.Text="Copy unavailable: "+ex.Message;
            }
        };
        historySelectedDetails.Children.Add(_studioFamilyHistoryCopyDetails);
        historySelectedDetails.Children.Add(_studioFamilyHistoryClipboardStatus);
        _studioHistoryPopulatedPanel.Children.Add(StudioTwoPane(historyLedger,historySelectedDetails));
        recorded.Children.Add(_studioHistoryPopulatedPanel);
        recorded.Children.Add(Muted("Recorded history is session-scoped here. Foreign or earlier sessions may still " +
            "block file operations. Use Inspect recovery journal for the authoritative present-time evidence."));
        _studioFamilyHistoryFilter.SelectionChanged+=(_,_)=>RefreshStudioFamilyHistory();
        _studioFamilyHistoryOutcome.SelectionChanged+=(_,_)=>RefreshStudioFamilyHistory();
        _studioFamilyHistorySearch.PropertyChanged+=(_,e)=>
        { if(e.Property==TextBox.TextProperty) RefreshStudioFamilyHistory(); };
        _studioFamilyHistoryList.SelectionChanged+=(_,_)=>
        {
            int index=_studioFamilyHistoryList.SelectedIndex;
            if (_studioFamilyHistoryDetails is not null)
                _studioFamilyHistoryDetails.Text=index>=0 && index<_studioFamilyHistoryShown.Length
                    ? _studioFamilyHistoryShown[index].ExactDetails
                    : "Select a family record for exact historical paths; no current verification implied.";
            if (_studioFamilyHistorySelectedInsight is not null)
                _studioFamilyHistorySelectedInsight.Text=index>=0 && index<_studioFamilyHistoryShown.Length
                    ? StudioHistoryInsightPresentation.For(_studioFamilyHistoryShown[index]).Display
                    : "Select a recorded operation. " + StudioHistoryInsightPresentation.NotVerified;
            if (_studioFamilyHistoryCopyDetails is not null)
                _studioFamilyHistoryCopyDetails.IsEnabled=index>=0 && index<_studioFamilyHistoryShown.Length;
            if (_studioFamilyHistoryClipboardStatus is not null)
                _studioFamilyHistoryClipboardStatus.Text="";
        };
        var legacyCopyBrowser=new StackPanel { Spacing=10 };
        legacyCopyBrowser.Children.Add(new TextBlock { Text="Browse recorded Copy files", FontSize=16,
            FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        _studioHistoryBrowserCount=Muted("Recorded Copy files loading…");
        legacyCopyBrowser.Children.Add(_studioHistoryBrowserCount);
        var historyFilters=new WrapPanel { Orientation=Orientation.Horizontal };
        _studioHistoryBrowserFilter=new ComboBox { ItemsSource=new[]{
            "All recorded","Completed","Undone","Needs attention"},
            SelectedIndex=0,MinWidth=155,Margin=new Thickness(0,0,8,6) };
        _studioHistoryBrowserSearch=new TextBox { PlaceholderText="Search names or exact paths",
            MinWidth=190, Width=300,Margin=new Thickness(0,0,0,6) };
        historyFilters.Children.Add(_studioHistoryBrowserFilter);
        historyFilters.Children.Add(_studioHistoryBrowserSearch);
        legacyCopyBrowser.Children.Add(historyFilters);
        _studioHistoryBrowserList=new ListBox { Height=194,MinHeight=110,
            SelectedIndex=-1 };
        legacyCopyBrowser.Children.Add(_studioHistoryBrowserList);
        legacyCopyBrowser.Children.Add(Muted("Recorded entries are read-only. Select an item to see its exact source, " +
            "destination and owned recovery path. Showing history does not check files."));
        _studioHistoryBrowserDetail=new TextBox { IsReadOnly=true,AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,Height=194,
            Text="Select a Copy record. Checked now: not inspected." };
        ScrollViewer.SetVerticalScrollBarVisibility(_studioHistoryBrowserDetail, ScrollBarVisibility.Auto);
        legacyCopyBrowser.Children.Add(_studioHistoryBrowserDetail);
        _studioHistoryBrowserFilter.SelectionChanged+=(_,_)=>RefreshStudioHistoryBrowser();
        _studioHistoryBrowserSearch.PropertyChanged+=(_,e)=>
        { if(e.Property==TextBox.TextProperty) RefreshStudioHistoryBrowser(); };
        _studioHistoryBrowserList.SelectionChanged+=(_,_)=>
        {
            int index=_studioHistoryBrowserList.SelectedIndex;
            if (_studioHistoryBrowserDetail is not null)
                _studioHistoryBrowserDetail.Text=index>=0 && index<_studioHistoryShown.Length
                    ? _studioHistoryShown[index].ExactDetails
                    : "Select a Copy record for exact recorded paths; no current verification implied.";
        };
        _studioHistoryLegacyBrowser=new Expander {
            Header="Copy-only details · legacy browser",
            Content=legacyCopyBrowser,IsExpanded=false,IsVisible=false };
        recorded.Children.Add(_studioHistoryLegacyBrowser);
        recorded.Children.Add(new TextBlock { Text="Check current files", FontSize=16,
            FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        recorded.Children.Add(Muted("View exact paths, completion records and any files that need attention."));
        recorded.Children.Add(Muted("Use the recovery inspection button above for current evidence. " +
            "Rechecking may update verified saved records, but never moves photographs."));
        recorded.Children.Add(Button("Diagnostics →",()=>ShowStudioPage(StudioPage.Diagnostics)));
        var available=new StackPanel { Spacing=12 };
        available.Children.Add(new TextBlock { Text="File actions and Undo", FontSize=18,
            FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        available.Children.Add(Muted("Only actions supported by the current photographs, " +
            "drive and recovery history become available. A disabled action doesn't change files."));
        recoveryActions.MaxWidth=440;
        recoveryActions.HorizontalAlignment=HorizontalAlignment.Left;
        available.Children.Add(recoveryActions);
        var historyActionsCard=StudioCard(available);
        historyActionsCard.VerticalAlignment=VerticalAlignment.Top;
        // The operation list and its exact evidence now use the FULL window width.
        // Existing native recovery actions remain separate and keep their handlers.
        historyBody.Children.Add(StudioCard(recorded));
        historyBody.Children.Add(historyActionsCard);
        var history=new Grid { Margin=new Thickness(24,4,24,16) };
        history.Children.Add(StudioScroll(historyBody));
        _studioPages.Add(StudioPage.History,history);

        // ROUTES: original 36-key editor and legacy Settings.xml path, moved
        // intact with all handlers into one independently scrolling page.
        var routesBody = new StackPanel { Spacing=12 };
        routesBody.Children.Add(StudioTitle("Tools & settings"));
        routesBody.Children.Add(Muted("Edit how PhotoSift organizes photographs, " +
            "or access less frequent tools. No file is changed by opening settings."));
        var routeLeft = new StackPanel { Spacing=12 };
        routeLeft.Children.Add(new TextBlock { Text="Folders for each key",
            FontSize=18, FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        routeLeft.Children.Add(Muted("Choose what happens when you press any of the 36 sorting keys."));
        routeLeft.Children.Add(routeSettings);
        var routeRight = new StackPanel { Spacing=13 };
        routeRight.Children.Add(new TextBlock { Text="Import & results",
            FontSize=18, FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        routeRight.Children.Add(Muted("Choose sources, recursive discovery and the " +
            "results root. Imported photos remain in their original folders."));
        routeRight.Children.Add(pickers);
        routeRight.Children.Add(new TextBlock { Text="When names collide",
            FontSize=16, FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        routeRight.Children.Add(_collisionMode);
        routeRight.Children.Add(Muted("Conflict decisions are still frozen in fresh " +
            "exact-path Review. Existing files are never silently replaced."));
        var morePlaces = new WrapPanel { Orientation=Orientation.Horizontal };
        var photoToolsLink = Button("Photo tools →",()=>ShowStudioPage(StudioPage.PhotoTools));
        photoToolsLink.Margin=new Thickness(0,0,8,8);
        morePlaces.Children.Add(photoToolsLink);
        morePlaces.Children.Add(Button("Diagnostics →",()=>ShowStudioPage(StudioPage.Diagnostics)));
        routeRight.Children.Add(new TextBlock { Text="More tools",
            FontSize=16, FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        routeRight.Children.Add(morePlaces);
        routesBody.Children.Add(StudioTwoPane(StudioCard(routeLeft),StudioCard(routeRight)));
        var routesPage=new Grid { Margin=new Thickness(24,4,24,16) };
        routesPage.Children.Add(StudioScroll(routesBody));
        _studioPages.Add(StudioPage.Routes,routesPage);

        // Less frequent navigation controls become an actual page, never an
        // expander that can collapse the photo viewer.
        var toolsBody=new StackPanel { Spacing=12 };
        toolsBody.Children.Add(StudioTitle("Photo tools"));
        toolsBody.Children.Add(Muted("Inspection and collection tools do not move or overwrite original files."));
        toolsBody.Children.Add(extraPhotoActions);
        toolsBody.Children.Add(_help);
        toolsBody.Children.Add(Button("Back to Focus",()=>ShowStudioPage(StudioPage.Focus)));
        toolsBody.Children.Add(Button("Tools & settings →",()=>ShowStudioPage(StudioPage.Routes)));
        var toolsPage=new Grid { Margin=new Thickness(24,4,24,16) };
        toolsPage.Children.Add(StudioScroll(StudioCard(toolsBody)));
        _studioPages.Add(StudioPage.PhotoTools,toolsPage);

        // Generated fixture interfaces, decoder statistics and licenses do
        // not occupy Focus. They are retained, NOT removed or made authoritative.
        var diagnosticsBody=new StackPanel { Spacing=12 };
        diagnosticsBody.Children.Add(StudioTitle("Diagnostics"));
        diagnosticsBody.Children.Add(Muted("Advanced verification and generated-only " +
            "test lanes. These controls do not grant ordinary filesystem authority."));
        var diagnosticLeft=new StackPanel { Spacing=12 };
        diagnosticLeft.Children.Add(new TextBlock { Text="Generated tests & safety checks",
            FontSize=18, FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        diagnosticLeft.Children.Add(advancedTools);
        var diagnosticRight=new StackPanel { Spacing=13 };
        diagnosticRight.Children.Add(new TextBlock { Text="System & support",
            FontSize=18, FontWeight=FontWeight.SemiBold, Foreground=StudioPalette.Text });
        diagnosticRight.Children.Add(Muted("Preview decoder activity and cache usage"));
        diagnosticRight.Children.Add(_previewMetrics);
        diagnosticRight.Children.Add(Muted("PhotoSift Reborn is an independent GPLv3-compatible " +
            "port of PhotoSift by RL Vision."));
        diagnosticRight.Children.Add(Muted("Use History & recovery for recorded " +
            "operations and current evidence. Diagnostic controls never clear history."));
        diagnosticRight.Children.Add(Button("History & recovery →",()=>ShowStudioPage(StudioPage.History)));
        diagnosticRight.Children.Add(Button("Recovery command center · native evidence →",
            (Func<Task>)(async () => await ShowRecoveryCommandCenterAsync())));
        diagnosticRight.Children.Add(Button("Tools & settings →",()=>ShowStudioPage(StudioPage.Routes)));
        diagnosticsBody.Children.Add(StudioTwoPane(StudioCard(diagnosticLeft),StudioCard(diagnosticRight)));
        var diagnosticsPage=new Grid { Margin=new Thickness(24,4,24,16) };
        diagnosticsPage.Children.Add(StudioScroll(diagnosticsBody));
        _studioPages.Add(StudioPage.Diagnostics,diagnosticsPage);

        Content=sharedShell;
        _studioHost.Content=focus;
        _studioActivePage=StudioPage.Focus;
        _studioTabs[StudioPage.Focus].Classes.Add("studio-current");
        RefreshStudioRecordedCopyHistory();
    }
}
