using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PhotoSiftReborn;

public sealed partial class MainWindow : Window
{
    private readonly SortingWorkspace _work = new() { IdentityResolver = NativeFileCapability.IsSupported ? NativeFileCapability.Identity : null };
    private WorkspaceSessionStore? _sessionStore;
    private bool _storeHealthy = true;
    // P3-R2: recovery inspection alone remains available for a precisely identified
    // mixed Copy/Move startup lock. This flag never relaxes EnsureWritable.
    private bool _p3R2MixedCopyMoveReadOnlyStartup;
    private bool _copyBarrier;
    // Per-process, explicit owner acknowledgement. Never saved or inherited
    // after restart; native Copy proof is re-run before every mutation.
    private bool _copyContinuationApproved;
    private readonly Button _resumeVerifiedCopy = new()
    { Content="Verify completed Copy Undo → resume sorting", MinHeight=40 };
    private DurableCopyCoordinator? _journal;
    private IReadOnlyList<PreparedCopy>? _prepared;
    private string _journalSummary = "";
    private DurableMoveCoordinator? _fixtureMover;
    private bool _moveFixtureEnabled;
    private bool _ordinaryMoveAvailable;
    private bool MoveModeAvailable => _moveFixtureEnabled || _ordinaryMoveAvailable;
    private bool _anyMoveHistory;
    // P6D2B: native Reject remains a historically locked generated-fixture lane.
    // Only dedicated review / approval / execute / proven Undo UI actions may
    // operate with that history. Normal Copy, Move, Rename and mark edits stay locked.
    private bool _anyRejectHistory;
    // P6C2-B4A: bounded, immutable first successor session only. The general
    // Copy/Move/Import/keyboard gates remain locked whenever Move history exists.
    private bool _boundedSuccessorGui;
    private DurableMoveItem? _approvedSuccessorTicket;
    private PreparedCopy? _approvedSuccessorPreparation;
    private readonly Button _beginSuccessorGui = new() { Content="Finish verified Move Undo → begin next TEST Move", MinHeight=44 };
    private readonly Button _stageSuccessorGui = new() { Content="Stage photo_02.png → Family (fixture only)", MinHeight=42 };
    private readonly Button _reviewSuccessorGui = new() { Content="Review & authorize successor Move", MinHeight=42 };
    private readonly Button _executeSuccessorGui = new() {
        Content="EXECUTE APPROVED TEST MOVE — relocates original", MinHeight=45,
        Background=new SolidColorBrush(Color.Parse("#983B2F")),
        Foreground=Brushes.White,FontWeight=FontWeight.Bold
    };

    private bool _anyRenameHistory;
    private bool _renameCloseoutVerified;
    private bool RenameLocksWrites => _anyRenameHistory && !_renameCloseoutVerified;
    private readonly Button _closeoutRename = new() { Content="Resume sorting after verified Rename Undo", MinHeight=40 };
    private DurableRenameCoordinator? _fixtureRenamer;
    private DurableRenameCoordinator? _windowsRenameTrial;
    private DurableRenameCoordinator? _ordinaryFirstRenamer;
    private RenameProposal? _pendingRename;
    private readonly Button _auditRename = new() { Content="Check Rename readiness (read-only)", MinHeight=38 };
    private readonly Button _renameFixture = new() { Content="F2 Rename (fixture only)", MinHeight=38 };
    private readonly Button _applyRename = new() { Content="Apply TEST Rename", MinHeight=38 };
    private readonly Button _undoRename = new() { Content="Undo verified Rename (fixture)", MinHeight=38 };
    private readonly P5COperationModeGate _modeGate = new();
    private Guid? _reviewedMoveId;
    private readonly Button _chooseCopy = new() { Content="COPY — keep originals", MinHeight=38 };
    private readonly Button _chooseMove = new() { Content="MOVE — fixture only", MinHeight=38 };
    private readonly Button _undoMove = new() { Content="Undo verified Move (fixture)", MinHeight=38 };
    private readonly TextBlock _modeStatus = Muted("COPY selected · Move only in generated disposable GUI fixture.");
    private bool JournalNeedsAttention => _journal is not null && _journal.NeedsAttention(_work.SessionId)>0;

    private static void StartupTrace(string stage)
    {
        if (Environment.GetEnvironmentVariable("PHOTOSIFT_STARTUP_TRACE") == "1")
            Console.Error.WriteLine("P3B_GUI_STARTUP=" + stage);
    }


    private bool EnsureWritable()
    {
        if (!_storeHealthy)
        { Message("Saved session is unavailable. Mutations are disabled to protect prior work."); return false; }
        if (_copyBarrier)
        { Message("Earlier P3A copies lack a native identity journal. No automatic deletion or barrier bypass is permitted."); return false; }
        if (_sessionStore is not null && _sessionStore.AllCopyHistoryCount()!=0)
        {
            if (!_copyContinuationApproved)
            { Message("Completed Copy history requires explicit fresh verification before continuing. Use Resume sorting after verified Copy Undo. No changes."); return false; }
            var proof=CopyUndoContinuation.Inspect(_sessionStore,_work);
            if (!proof.Verified)
            { _copyContinuationApproved=false; Message("Copy history verification refused: "+proof.Reason+" No changes."); return false; }
        }
        if (RenameLocksWrites)
        { Message("Rename history is not closed out. Undo every Rename, then use Resume sorting after verified Rename Undo; unresolved history cannot be bypassed."); return false; }
        if (_anyMoveHistory)
        { Message("Move journal history exists. Only read-only recovery inspection and verified fixture Move Undo are permitted; no Copy, rebind or new Move."); return false; }
        if (_anyRejectHistory)
        { Message("Native Reject history is present. Only the dedicated generated-fixture Reject recovery controls can act; Copy/Move/Rename/mark edits remain locked."); return false; }
        if (JournalNeedsAttention)
        { Message("Recovery needs attention. Choose Inspect recovery journal to see exact paths and safe next steps. Files preserved."); return false; }
        return true;
    }

    // Cursor-only actions are allowed with terminal Rename history. Copy, Move,
    // assignment, source import and route edits still use EnsureWritable().
    private bool EnsureViewerNavigationAllowed()
    {
        if (!_storeHealthy || _copyBarrier || _anyMoveHistory || JournalNeedsAttention) return false;
        if (!RenameLocksWrites) return true;
        if (_sessionStore is null) return false;
        return _moveFixtureEnabled
            ? FixtureRenameGuiGate.CanBrowse(_sessionStore,_work)
            : _windowsRenameTrial is not null && WindowsRenameGuiGate.CanBrowse(_sessionStore,_work);
    }

    private async Task SaveSessionAsync()
    {
        if (_sessionStore is null) throw new IOException("The workspace database is unavailable.");
        var snapshot = _work.CaptureSession();
        try { await Task.Run(() => _sessionStore.Save(snapshot)); }
        catch (Exception ex)
        {
            _storeHealthy = false;
            Message("SESSION SAVE FAILED. Changes may not survive restart. Stop editing: " + ex.Message);
            throw;
        }
    }

    private readonly Image _photo = new() { Stretch = Stretch.Uniform };
    private readonly Border _viewerMessage = new() { IsVisible = true };
    private readonly TextBlock _viewerHeading = new()
    {
        FontSize = 25, FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.White, TextAlignment = TextAlignment.Center
    };
    private readonly TextBlock _viewerDescription = new()
    {
        FontSize = 15, TextWrapping = TextWrapping.Wrap,
        Foreground = StudioPalette.Secondary,
        TextAlignment = TextAlignment.Center, MaxWidth = 510
    };
    private readonly Button _viewerBack = new() { Content = "← Correct last photo", MinHeight = 39 };
    private readonly Button _viewerReview = new() { Content = "Review destinations →", MinHeight = 39 };
    private readonly TextBlock _photoLabel = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly TextBlock _sourceLabel = Muted("Source: choose a folder");
    private readonly TextBlock _outputLabel = Muted("Output: choose a DIFFERENT folder");
    private readonly TextBlock _status = Muted("Choose real input and output folders. No changes occur until Apply.");
    // Critical startup failures must be visible at 125–150% display scaling.
    // The bottom status footer can be clipped behind the taskbar.
    private readonly TextBlock _startupFailureBanner = new()
    {
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.Parse("#FFB38A")),
        FontWeight = FontWeight.SemiBold
    };
    private readonly ListBox _plan = new() { MinHeight = 175, MaxHeight = 250 };
    private readonly TextBlock _progress = Muted("0 staged · 0 applied");
    private readonly TextBlock _previewMetrics = Muted("Decode worker: awaiting first image");
    private readonly TextBlock _routeList = Muted("");
    private readonly TextBox _studioExactLocations = new() { IsReadOnly=true, AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, Height=110 };
    private readonly WrapPanel _quickRoutes = new() { Orientation=Orientation.Horizontal };
    private readonly TextBox _newKey = new() { Width = 55, PlaceholderText = "Key", Text = "P" };
    private readonly TextBox _newLabel = new() { Width = 185, PlaceholderText = "Family/Holidays", Text = "Projects" };
    private readonly ComboBox _routeKind = new() { Width = 105, ItemsSource = new[] { "Default", "Relative", "Absolute" }, SelectedIndex = 1 };
    private readonly TextBlock _routePreview = Muted("Choose a key and route type. Review always shows the exact final path.");
    private readonly TextBlock _reviewHint = new()
    {
        Foreground = new SolidColorBrush(Color.Parse("#FFB38E")),
        TextWrapping = TextWrapping.Wrap, IsVisible = false, FontWeight=FontWeight.SemiBold
    };
    private readonly Button _apply = new() { Content = "Apply reviewed copies", MinHeight = 40 };
    private readonly Button _undoApplied = new() { Content = "Undo verified copies", MinHeight = 40 };
    private readonly Button _review = new() { Content = "Review exact paths", MinHeight = 40 };
    private readonly Button _undoStage = new() { Content = "Undo assignment", MinHeight = 40 };
    private readonly Button _inspectRecovery = new() { Content = "Inspect recovery journal", MinHeight = 40 };
    // Transitional Studio header commands: no new native operation paths.
    private Button _studioCollection = new();
    private Button _studioChanges = new();
    private Button _studioHistory = new();
    private Button _studioAdd = new();
    private readonly Button _prev = new() { Content = "← Previous", MinHeight = 38 };
    private readonly Button _next = new() { Content = "Next →", MinHeight = 38 };
    private readonly Button _jump = new() { Content = "Jump to #…", MinHeight = 38 };
    private readonly Button _nextUnassigned = new() { Content = "Next unassigned", MinHeight = 38 };
    private readonly Button _shuffle = new() { Content = "Shuffle order", MinHeight = 38 };
    private readonly Button _removeSession = new() { Content = "Remove from session", MinHeight = 38 };
    private readonly Button _undoRemove = new() { Content = "Undo remove", MinHeight = 38 };
    private readonly Button _markReject = new() { Content = "Mark Reject for review (no file move)", MinHeight = 38 };
    private readonly Button _undoReject = new() { Content = "Undo Reject-review mark", MinHeight = 38 };
    private readonly Button _reviewRejectQueue = new() { Content = "Review marked photos…", MinHeight = 38 };
    private readonly TextBlock _rejectOverview = Muted("Reject review: 0 marked · No files moved");
    private readonly Button _help = new() { Content = "Help (F1)", MinHeight = 38 };
    private readonly Button _inspectPhoto = new() { Content = "Inspect (F12)", MinHeight = 38 };
    private readonly Button _fullScreen = new() { Content = "Full screen (F11)", MinHeight = 38 };
    private readonly PreviewWorker _previewWorker = new();
    private CancellationTokenSource? _importCts;
    private readonly HashSet<Key> _heldKeys = [];
    private Guid? _requestedPhotoId;
    private long _requestedLength;
    private long _requestedTicks;
    private Guid? _displayedPhotoId;
    private bool _displayReady;
    private Bitmap? _currentBitmap;
    private long _viewGeneration;
    private readonly PreviewFailureState _previewFailure = new();
    
    private bool _busy;
    private bool _hasReviewed;
    private readonly Button _cancelImport = new() { Content = "Cancel import", IsEnabled = false };
    private readonly CheckBox _recursiveInput = new() { Content = "Include subfolders", IsChecked = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _collisionMode = new() { Content = "Collisions: Refuse (change before Review)", MinHeight = 38 };

    private static TextBlock Muted(string text) => new()
    {
        Text = text, Foreground = StudioPalette.Secondary, TextWrapping = TextWrapping.Wrap
    };

    public MainWindow(string[]? startupPaths = null)
    {
        Title = "PhotoSift Reborn — cross-platform photo sorting";
        // Leave room for the taskbar on common 768/900px-high desktops.
        Width = 1360; Height = 780; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // M6E: explicitly request native desktop chrome (Avalonia 12 API).
        // The owner observed caption flags but an inaccessible caption, so
        // retained in-client commands below are real Window actions, not art.
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = false;
        CanResize = true; CanMinimize = true; CanMaximize = true;
        WindowState = WindowState.Normal;
        Opened += (_,_) => FitStudioNormalWindowToWorkArea();
        Background = StudioPalette.Shell;
        Foreground = StudioPalette.Text;

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), ColumnDefinitions = new ColumnDefinitions("*,324") };
        // P6G3E-R1 Studio foundation: real commands, NOT fake complete views.
        // Collection uses the existing read-only/choice decision center; Changes
        // uses the existing exact-path review, retaining every native approval.
        var head = new WrapPanel { Orientation = Orientation.Horizontal,
            Margin = new Thickness(22,12,22,13), VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(new TextBlock { Text = "PhotoSift Reborn", FontSize = 21,
            FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioPalette.Text, Margin = new Thickness(0,0,24,3) });
        head.Children.Add(new Border { Background = StudioPalette.Surface,
            CornerRadius = new CornerRadius(6), Padding = new Thickness(15,9),
            Margin = new Thickness(0,0,8,3), Child = new TextBlock { Text = "Focus",
                FontWeight = FontWeight.SemiBold, Foreground = StudioPalette.Jade } });
        _studioCollection = Button("Collection", (Func<Task>)(async () => await ReviewCollectionAsync()));
        _studioCollection.Classes.Add("studio-nav");
        _studioCollection.Margin = new Thickness(0,0,8,3);
        ToolTip.SetTip(_studioCollection, "Open saved photo choices and filters. File changes require separate review.");
        head.Children.Add(_studioCollection);
        _studioChanges = Button("Changes", (Func<Task>)(async () => await ReviewAsync()));
        _studioChanges.Classes.Add("studio-nav");
        _studioChanges.Margin = new Thickness(0,0,8,3);
        ToolTip.SetTip(_studioChanges, "Open the existing exact-path review; no file changes until independently approved.");
        head.Children.Add(_studioChanges);
        _studioHistory = Button("History & recovery", (Func<Task>)(async () => await ShowRecoveryAsync()));
        _studioHistory.Margin = new Thickness(0,0,8,3);
        ToolTip.SetTip(_studioHistory, "Review recorded journal history and current recovery evidence. This does not authorize changes.");
        head.Children.Add(_studioHistory);
        _studioAdd = Button("Add photos", (Func<Task>)(async () => await AddInputAsync()));
        _studioAdd.Margin = new Thickness(0,0,0,3);
        _studioAdd.Classes.Add("studio-primary");
        ToolTip.SetTip(_studioAdd, "Add a folder of photos to the current collection. Existing review/history gates still apply.");
        head.Children.Add(_studioAdd);
        // Genuine window state actions remain accessible even if Windows
        // places its native caption above the visible client area.
        var windowTools = new WrapPanel { Orientation=Orientation.Horizontal,
            Margin=new Thickness(13,0,0,0) };
        var minWindow=Button("—", () => WindowState = WindowState.Minimized);
        var toggleWindow=Button("▢", ToggleStudioWindowMaximize);
        var closeWindow=Button("×", () => Close()); // honors existing Closing vetoes
        ToolTip.SetTip(minWindow, "Minimize PhotoSift window");
        ToolTip.SetTip(toggleWindow, "Maximize or restore window; normal view is movable and resizable");
        ToolTip.SetTip(closeWindow, "Close PhotoSift (active review and operation safeguards still apply)");
        foreach(var button in new[]{minWindow,toggleWindow,closeWindow})
        {
            button.MinWidth=38;button.MinHeight=32;
            button.Margin=new Thickness(0,0,5,3);
            windowTools.Children.Add(button);
        }
        head.Children.Add(windowTools);
        Grid.SetColumnSpan(head, 2); layout.Children.Add(head);

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(22,0,10,13) };
        Grid.SetRow(left,1);
        // Compact import controls: no fixed root scroll and no giant empty sidebar.
        var pickers = new StackPanel { Spacing = 5, Margin = new Thickness(0,0,0,9) };
        // Wrap controls at 920px-wide desktop instead of clipping the newly added buttons.
        var pickerButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        void PickerButton(Button control) { control.Margin = new Thickness(0,0,8,5); pickerButtons.Children.Add(control); }
        var openPhotoFolder = Button("Open photo folder", (Func<Task>)(async () => await ChooseInputAsync()));
        openPhotoFolder.Classes.Add("studio-primary");
        PickerButton(openPhotoFolder);
        PickerButton(Button("Results folder…", (Func<Task>)(async () => await ChooseOutputAsync())));
        pickers.Children.Add(pickerButtons);
        // Original Add / recursion and cancel commands still run the same
        // source importer; avoid six equal first-screen calls to action.
        var additionalImport = new WrapPanel { Orientation = Orientation.Horizontal };
        void AddImportOption(Button control) { control.Margin = new Thickness(0,0,8,5); additionalImport.Children.Add(control); }
        AddImportOption(Button("Add folder", (Func<Task>)(async () => await AddInputAsync())));
        AddImportOption(Button("Add files", (Func<Task>)(async () => await AddFilesAsync())));
        _recursiveInput.Margin = new Thickness(2,0,10,5);
        additionalImport.Children.Add(_recursiveInput);
        _cancelImport.Click += (_,_) => _importCts?.Cancel();
        AddImportOption(_cancelImport);
        pickers.Children.Add(new Expander { Header = "More import options", Content = additionalImport,
            IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 255, MaxWidth = 315 });
        // Show source/output together when there is width, wrapping on smaller clients.
        // The labels remain live instances updated by Refresh.
        var sourceAndResults = new WrapPanel { Orientation = Orientation.Horizontal };
        _sourceLabel.Margin = new Thickness(0,0,20,0);
        _outputLabel.Margin = new Thickness(0,0,8,0);
        sourceAndResults.Children.Add(_sourceLabel);
        sourceAndResults.Children.Add(_outputLabel);
        pickers.Children.Add(sourceAndResults);
        // Do not bury or corrupt full paths: disclose exact saved settings
        // on demand, with normal selectable read-only text and Ctrl+C.
        pickers.Children.Add(new Expander {
            Header="Show exact source and results paths (read-only)",
            IsExpanded=false,
            Content=_studioExactLocations,
            HorizontalAlignment=HorizontalAlignment.Stretch });
        pickers.Children.Add(_startupFailureBanner);
        // Unlike the old footer, important operation/recovery messages stay
        // above the photo viewer even at 150–175% Windows display scaling.
        _status.FontWeight = FontWeight.SemiBold;
        _status.Foreground = new SolidColorBrush(Color.Parse("#BBDFFF"));
        var liveStatus = new ScrollViewer
        {
            Content = _status, MaxHeight = 56,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        pickers.Children.Add(liveStatus);
        left.Children.Add(pickers);

        // Explicit empty/loading/finished states: never leave a giant blank
        // viewer after the final assignment or when decoding fails.
        // The Windows R1 screenshot showed the viewer collapsing to a sliver when
        // the old navigation expander opened. Bound its siblings; never permit
        // instructional text to paint over the next row.
        var surface = new Border { CornerRadius = new CornerRadius(8), Background = StudioPalette.PhotoBed, Padding = new Thickness(12),
            MinHeight = 165, ClipToBounds = true };
        var viewer = new Grid();
        viewer.Children.Add(_photo);
        var messageContent = new StackPanel { Spacing = 13, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        messageContent.Children.Add(_viewerHeading);
        messageContent.Children.Add(_viewerDescription);
        var viewerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        _viewerBack.Click += async (_, _) => await NavigateAsync(-1);
        _viewerReview.Click += async (_, _) => await ReviewAsync();
        viewerActions.Children.Add(_viewerBack);
        viewerActions.Children.Add(_viewerReview);
        messageContent.Children.Add(viewerActions);
        _viewerMessage.Child = messageContent;
        viewer.Children.Add(_viewerMessage);
        Grid.SetRow(surface,1); surface.Child = viewer; left.Children.Add(surface);
        Grid.SetRow(_photoLabel,2); _photoLabel.Margin = new Thickness(4,10,4,8); left.Children.Add(_photoLabel);
        var nav = new StackPanel { Orientation = Orientation.Vertical, Spacing = 5 };
        var navButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        void NavButton(Button button) { button.Margin = new Thickness(0,0,7,5); navButtons.Children.Add(button); }
        _prev.Click += async (_,_) => await NavigateAsync(-1);
        _next.Click += async (_,_) => await NavigateAsync(1);
        _jump.Click += async (_,_) => await JumpDialogAsync();
        _nextUnassigned.Click += async (_,_) => await JumpUnassignedAsync();
        _shuffle.Click += async (_,_) => await ShuffleAsync();
        _removeSession.Click += async (_,_) => await RemoveFromSessionAsync();
        _undoRemove.Click += async (_,_) => await UndoRemoveAsync();
        _markReject.Click += async (_,_) => await MarkRejectReviewAsync();
        _undoReject.Click += async (_,_) => await UndoRejectReviewAsync();
        _reviewRejectQueue.Click += async (_,_) => await ReviewRejectQueueAsync();
        _help.Click += async (_,_) => await ShowHelpAsync();
        _inspectPhoto.Click += async (_,_) => await ShowPhotoInspectionAsync();
        _fullScreen.Click += (_,_) => ToggleFullScreen();
        // Keep the ordinary photo flow visible; uncommon navigation remains
        // available in a bounded keyboard-accessible expander, not removed.
        foreach (var b in new[]{_prev,_next,_nextUnassigned,_markReject,
            _undoReject,_reviewRejectQueue,_help}) NavButton(b);
        var extraPhotoActions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[]{_jump,_shuffle,_removeSession,_undoRemove,
            _inspectPhoto,_fullScreen})
        { b.Margin = new Thickness(0,0,7,5); extraPhotoActions.Children.Add(b); }
        // Single bounded scroll owner: no nested scrollbars inside this expander.
        var morePhotoTools = new Expander { Header = "More photo navigation and tools",
            Content = extraPhotoActions, IsExpanded = false };
        _decisionCenter.Click += async (_,_) => await ReviewCollectionAsync();
        ToolTip.SetTip(_decisionCenter, "Review and filter saved choices; stage exact filtered undecided photos to one route, or clear a single choice. No file changes.");
        NavButton(_decisionCenter);
        nav.Children.Add(navButtons);
        nav.Children.Add(morePhotoTools);
        nav.Children.Add(_decisionHeadline);
        nav.Children.Add(_rejectOverview);
        nav.Children.Add(new TextBlock { Text = "←/→ navigate · Ctrl+J jump · Ctrl+U next unassigned · F1 help · A–Z / 0–9 route", VerticalAlignment = VerticalAlignment.Center, FontSize=12, Foreground=StudioPalette.Secondary });
        // R1 defect: an Auto-sized expander in a Grid Auto row took height from
        // the viewer until its text painted on top of the bottom commands.
        // R2 gives this whole tool region ONE bounded scrolling viewport.
        var navScroll = new ScrollViewer { Content = nav, MaxHeight = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            ClipToBounds = true };
        Grid.SetRow(navScroll,3); left.Children.Add(navScroll);
        layout.Children.Add(left);

        var sideBorder = new Border { Margin = new Thickness(7,0,22,13), Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Background = StudioPalette.Surface };
        Grid.SetRow(sideBorder,1); Grid.SetColumn(sideBorder,1);
        // At 150% Windows scaling a 1080px screen offers roughly 720 layout pixels.
        // The former bottom action dock was taller than the entire available sidebar:
        // it starved the scrolling route editor and truncated two-column labels.
        // Pin only the common Sort/Review/Apply/choice Undo controls; put the other
        // tools in the scrolling area. The photo viewer never scrolls as a page.
        // Main route guidance and keys now lead the rail. File action controls stay
        // persistently available at the bottom, without becoming new authority.
        var sideShell = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        // Keep explanatory text and route paths clear of Avalonia's overlaid
        // vertical scrollbar at 125–150% Windows scaling. A right inset
        // reduces the measure width so wrapped labels finish before the thumb.
        // This does not alter action docking, review approval or file operations.
        var side = new StackPanel { Spacing = 10, Margin = new Thickness(0,0,26,0) };
        side.Children.Add(_guidedHeadline);
        side.Children.Add(_quickRoutes);
        side.Children.Add(_guidedNextStep);
        side.Children.Add(_modeStatus);
        side.Children.Add(Muted("Press a destination letter or number to save a choice. Review before applying files."));
        _showMarkedRecovery.HorizontalAlignment = HorizontalAlignment.Stretch;
        _showMarkedRecovery.Click += (_,_) => ShowRecoveryAndFileTools();
        side.Children.Add(_showMarkedRecovery);
        side.Children.Add(_progress);
        // All native/recovery controls remain wired as before, but they no
        // longer precede the ordinary destination keys in the first viewport.
        var advancedTools = new StackPanel { Spacing = 8 };
        var nativeFixtureTools = new StackPanel { Spacing = 8 };
        AddNativeRejectGuiSection(nativeFixtureTools);
        _fixtureRecoveryExpander.Content = nativeFixtureTools;
        advancedTools.Children.Add(_fixtureRecoveryExpander);
        AddOrdinaryFirstRejectSection(advancedTools);
        if (OrdinaryRejectGuiProbePolicy.IsRequested)
            AddOrdinaryRejectGuiProbeSection(advancedTools);
        _ordinaryOperationsSafety.Click += async (_,_) => await ShowOrdinaryOperationsSafetyAsync();
        advancedTools.Children.Add(_ordinaryOperationsSafety);
        advancedTools.Children.Add(Muted("File changes require verified ownership and exact review. History is never cleared to unlock a button."));
        var settingsTools = new StackPanel { Spacing = 8 };
        settingsTools.Children.Add(new TextBlock { Text = "Destination keys",
            FontSize = 16, FontWeight = FontWeight.SemiBold });
        settingsTools.Children.Add(_routeList);
        var newRoute = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Control control in new Control[] { _newKey, _routeKind, _newLabel })
        { control.Margin = new Thickness(0,0,6,6); newRoute.Children.Add(control); }
        var setRoute=Button("Save route", (Func<Task>)AddRouteAsync);
        setRoute.Margin=new Thickness(0,0,6,6); newRoute.Children.Add(setRoute);
        var absolutePicker=Button("Pick absolute folder…", (Func<Task>)PickAbsoluteRouteAsync);
        absolutePicker.Margin=new Thickness(0,0,6,6); newRoute.Children.Add(absolutePicker);
        settingsTools.Children.Add(newRoute);
        _routeKind.SelectionChanged += (_,_) =>
        {
            _newLabel.IsReadOnly = _routeKind.SelectedIndex == 2;
            _newLabel.PlaceholderText = _routeKind.SelectedIndex == 2 ? "Use Pick absolute folder…" : "Family/Holidays";
        };
        settingsTools.Children.Add(_routePreview);
        var importLegacy=Button("Import old Settings.xml…", (Func<Task>)ImportLegacySettingsAsync);
        importLegacy.HorizontalAlignment=HorizontalAlignment.Stretch;
        settingsTools.Children.Add(importLegacy);
        settingsTools.Children.Add(Muted("Preview legacy routes before import. Absolute paths need relinking; legacy Move/Delete and Overwrite stay disabled."));
        var probeMove = Button("Assess Move safety (read-only)", (Func<Task>)AssessMoveReadinessAsync);
        probeMove.HorizontalAlignment = HorizontalAlignment.Stretch;
        advancedTools.Children.Add(probeMove);
        advancedTools.Children.Add(Muted("Ordinary Move and same-directory Rename require separate exact review, native checks and explicit Apply. Reject recovery remains independently guarded."));
        settingsTools.Children.Add(Muted("Unused A–Z/0–9 keys create folders. Relative routes can nest (Family/Holidays); absolute routes use the folder picker. Every target needs fresh Review."));
        side.Children.Add(new Expander { Header = "Edit destinations and import old settings",
            Content = settingsTools, IsExpanded = false });
        var reviewDetails = new StackPanel { Spacing = 8 };
        reviewDetails.Children.Add(_plan);
        reviewDetails.Children.Add(Muted("Only reviewed paths can be applied. Copy Undo retains verified output in owned recovery."));
        side.Children.Add(new Expander { Header = "See saved photo choices",
            Content = reviewDetails, IsExpanded = false });
        // Position close to the everyday controls so recovery is discoverable.
        side.Children.Add(_recoveryAndFileTools);
        var diagnostics = new StackPanel { Spacing = 7 };
        diagnostics.Children.Add(_previewMetrics);
        diagnostics.Children.Add(Muted("Independent GPLv3-compatible port of PhotoSift (RL Vision, 2013–2014). Original pinned commit a5e83b9198f5dd251053c48182cc37fea9ff6670."));
        side.Children.Add(new Expander { Header = "Diagnostics and licensing", Content = diagnostics,
            IsExpanded = false });
        scroll.Content = side;
        Grid.SetRow(scroll, 0);
        sideShell.Children.Add(scroll);

        // Compact pinned controls remain accessible at 150% scale; uncommon
        // operations are placed in the sidebar scroll rather than consuming
        // the entire available height.
        _review.Content = "Review paths";
        _apply.Content = "Apply copies";
        _undoStage.Content = "Undo choice";
        _undoApplied.Content = "Undo copies";
        _review.Click += async (_,_) => await ReviewAsync();
        _apply.Click += async (_,_) => await ApplyAsync();
        _undoApplied.Click += async (_,_) => await UndoAppliedAsync();
        _undoStage.Click += async (_,_) => await UndoStageAsync();

        var actionDock = new StackPanel { Spacing = 6, Margin = new Thickness(0,0,0,10) };
        actionDock.Children.Add(new TextBlock { Text = "Sort actions", FontSize = 17,
            FontWeight = FontWeight.SemiBold });
        var actionsPrimary = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        _review.Margin = new Thickness(0,0,5,0);
        _apply.Margin = new Thickness(5,0,0,0);
        _review.HorizontalAlignment = HorizontalAlignment.Stretch;
        _apply.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(_apply, 1);
        actionsPrimary.Children.Add(_review);
        actionsPrimary.Children.Add(_apply);
        var modes=new Grid { ColumnDefinitions=new ColumnDefinitions("*,*") };
        _chooseCopy.Margin=new Thickness(0,0,5,0);
        _chooseMove.Margin=new Thickness(5,0,0,0);
        _chooseCopy.HorizontalAlignment=HorizontalAlignment.Stretch;
        _chooseMove.HorizontalAlignment=HorizontalAlignment.Stretch;
        Grid.SetColumn(_chooseMove,1);
        modes.Children.Add(_chooseCopy); modes.Children.Add(_chooseMove);
        _chooseCopy.Click+=(_,_)=>SelectOperationMode(ReviewOperationMode.Copy);
        _chooseMove.Click+=(_,_)=>SelectOperationMode(ReviewOperationMode.Move);
        ToolTip.SetTip(_chooseCopy, "Copy reviewed photographs; originals stay in place.");
        ToolTip.SetTip(_chooseMove, "Move requires one flat, local native-identity-backed source, an independent exact review, and a first-epoch owned recovery journal. Other filesystems remain Copy-only.");
        ToolTip.SetTip(_review, "Inspect the exact paths and approve the current plan before applying.");
        ToolTip.SetTip(_apply, "Execute only the separately approved operation.");
        actionDock.Children.Add(modes);
        actionDock.Children.Add(actionsPrimary);
        actionDock.Children.Add(_reviewHint);
        _collisionMode.HorizontalAlignment = HorizontalAlignment.Stretch;
        _collisionMode.Click += async (_,_) => await CycleCollisionChoiceAsync();
        var actionsUndo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        _undoStage.Margin = new Thickness(0,0,5,0);
        _undoApplied.Margin = new Thickness(5,0,0,0);
        _undoStage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _undoApplied.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(_undoApplied, 1);
        actionsUndo.Children.Add(_undoStage);
        actionsUndo.Children.Add(_undoApplied);
        actionDock.Children.Add(actionsUndo);
        // A scrollable recovery section keeps the common sorting controls usable
        // without clipping or shrinking them at high DPI.
        var recoveryActions = new StackPanel { Spacing=8, Margin=new Thickness(0,8,0,6) };
        recoveryActions.Children.Add(new Border { Height=1, Background=Brushes.Gray, Margin=new Thickness(0,3) });
        recoveryActions.Children.Add(new TextBlock { Text="More file actions", FontSize=17,
            FontWeight=FontWeight.SemiBold });
        _resumeVerifiedCopy.HorizontalAlignment=HorizontalAlignment.Stretch;
        _resumeVerifiedCopy.Click+=async (_,_)=>await ResumeAfterVerifiedCopyUndoAsync();
        recoveryActions.Children.Add(_resumeVerifiedCopy);
        recoveryActions.Children.Add(_collisionMode);
        _undoMove.Click+=async (_,_)=>await UndoMoveAsync();
        _undoMove.HorizontalAlignment=HorizontalAlignment.Stretch;
        recoveryActions.Children.Add(_undoMove);
        _beginSuccessorGui.HorizontalAlignment=HorizontalAlignment.Stretch;
        _stageSuccessorGui.HorizontalAlignment=HorizontalAlignment.Stretch;
        _reviewSuccessorGui.HorizontalAlignment=HorizontalAlignment.Stretch;
        _executeSuccessorGui.HorizontalAlignment=HorizontalAlignment.Stretch;
        _beginSuccessorGui.Click += async (_,_) => await BeginFixtureSuccessorAsync();
        _stageSuccessorGui.Click += async (_,_) => await StageFixtureSuccessorAsync();
        _reviewSuccessorGui.Click += async (_,_) => await ReviewFixtureSuccessorAsync();
        _executeSuccessorGui.Click += async (_,_) => await ExecuteFixtureSuccessorAsync();
        recoveryActions.Children.Add(_beginSuccessorGui);
        recoveryActions.Children.Add(_stageSuccessorGui);
        recoveryActions.Children.Add(_reviewSuccessorGui);
        recoveryActions.Children.Add(_executeSuccessorGui);
        foreach(var button in new[]{_beginThirdGui,_stageThirdGui,_reviewThirdGui,_executeThirdGui})
            button.HorizontalAlignment=HorizontalAlignment.Stretch;
        _beginThirdGui.Click+=async (_,_)=>await BeginThirdFixtureGuiAsync();
        _stageThirdGui.Click+=async (_,_)=>await StageThirdFixtureGuiAsync();
        _reviewThirdGui.Click+=async (_,_)=>await ReviewThirdFixtureGuiAsync();
        _executeThirdGui.Click+=async (_,_)=>await ExecuteThirdFixtureGuiAsync();
        recoveryActions.Children.Add(_beginThirdGui);
        recoveryActions.Children.Add(_stageThirdGui);
        recoveryActions.Children.Add(_reviewThirdGui);
        recoveryActions.Children.Add(_executeThirdGui);
        _renameFixture.Click+=async (_,_)=>await RenameDialogAsync();
        _applyRename.Click+=async (_,_)=>await ApplyRenameAsync();
        _undoRename.Click+=async (_,_)=>await UndoRenameAsync();
        _renameFixture.HorizontalAlignment=HorizontalAlignment.Stretch;
        _applyRename.HorizontalAlignment=HorizontalAlignment.Stretch;
        _undoRename.HorizontalAlignment=HorizontalAlignment.Stretch;
        // Full-width Rename buttons: neither name nor recovery actions can be
        // cut off inside a narrow half-column on scaled Windows desktops.
        _auditRename.Click += async (_,_) => await InspectProductionRenameReadinessAsync();
        _auditRename.HorizontalAlignment=HorizontalAlignment.Stretch;
        recoveryActions.Children.Add(_auditRename);
        recoveryActions.Children.Add(_renameFixture);
        recoveryActions.Children.Add(_applyRename);
        recoveryActions.Children.Add(_undoRename);
        _closeoutRename.Click += async (_,_)=>await CloseoutRenameAsync();
        recoveryActions.Children.Add(_closeoutRename);
        _inspectRecovery.Click += async (_, _) => await ShowRecoveryAsync();
        _inspectRecovery.HorizontalAlignment = HorizontalAlignment.Stretch;
        recoveryActions.Children.Add(_inspectRecovery);
        var clearAssignments = Button("Clear saved folder choices", (Func<Task>)(async () => {
            if (_busy || !EnsureWritable()) return;
            try { _busy=true; _work.ClearAssignments(); _hasReviewed=false; await SaveSessionAsync(); Message("Cleared saved choices. No disk changes."); }
            catch (Exception ex) { Message("Cannot save clearing assignments: " + ex.Message); }
            finally { _busy=false; Refresh(); }
        }));
        clearAssignments.HorizontalAlignment = HorizontalAlignment.Stretch;
        recoveryActions.Children.Add(clearAssignments);
        advancedTools.Children.Add(recoveryActions);
        _recoveryAndFileTools.Content = advancedTools;
        Grid.SetRow(actionDock, 1);
        sideShell.Children.Add(actionDock);
        sideBorder.Child = sideShell; layout.Children.Add(sideBorder);

        // No debug telemetry strip under the photo; it used to steal height
        // from the only important surface. Evidence remains under Diagnostics.
        var foot = new TextBlock { Text = "File changes require exact review. History & recovery is always available.",
            Foreground = StudioPalette.Secondary, FontSize = 12,
            Margin = new Thickness(22,3,22,8) };
        Grid.SetRow(foot,2); Grid.SetColumnSpan(foot,2);
        layout.Children.Add(foot);
        Content = layout;
        // R3: the original wiring above remains intact, but the UI actually
        // displays distinct Studio pages. Controls are moved once, never cloned
        // or reparented during Refresh; the old long-dropdown shell is abandoned.
        InstallStudioPages(pickers, left, surface, navButtons, morePhotoTools,
            extraPhotoActions, side, settingsTools, reviewDetails,
            advancedTools, recoveryActions, actionDock, sideShell);
        // Dropped local files/folders and CLI paths use exactly the same Add pipeline as pickers.
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_,e) => {
            e.DragEffects = (!_busy && e.DataTransfer.Formats.Contains(DataFormat.File))
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        DragDrop.AddDropHandler(this, async (_,e) => {
            if (_busy || !e.DataTransfer.Formats.Contains(DataFormat.File)) return;
            var items=e.DataTransfer.TryGetFiles();
            if (items is null) { Message("Only local file and folder drops are supported."); return; }
            var paths=items.Select(x=>x.TryGetLocalPath()).ToArray();
            if (paths.Length==0 || paths.Any(string.IsNullOrWhiteSpace)) {
                Message("Some dropped items have no local path; nothing imported."); return;
            }
            e.Handled=true;
            await ImportPathsAsync(paths!, replace:false, recursive:_recursiveInput.IsChecked==true);
        });
        AddHandler(InputElement.KeyDownEvent,OnStudioEmbeddedPreviewKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,handledEventsToo:true);
        KeyDown += OnKeyDown;
        KeyUp += (_, e) => _heldKeys.Remove(e.Key);
        Deactivated += (_,_) => _heldKeys.Clear();
        _previewWorker.Completed += AcceptPreview;
        try
        {
            StartupTrace("OPENING_WORKSPACE_DB");
            _sessionStore = new WorkspaceSessionStore();
            var r30History=_sessionStore.InspectR30History();
            if(r30History.Present)
            {
                // The generated v20 ledger has its own read-only readiness
                // projection. Never run legacy reconciliation, checkpoint
                // rewrites, or ordinary command binding over this history.
                _storeHealthy=false;
                if(_r30Readiness is not null)
                    _r30Readiness.Text=StudioR30ReadinessPresentation.Describe(r30History);
                Message("Generated R30 operation history opened for read-only inspection. " +
                    "Ordinary file operations remain locked. " + r30History.Reason);
            }
            else
            {
            var saved = _sessionStore.Load();
            StartupTrace("LOADED_WORKSPACE_DB");
            if (saved is not null) _work.RestoreSession(saved);
            _copyBarrier = _sessionStore.HasUnreconciledCopy;
            _anyMoveHistory = _sessionStore.AllMoveHistoryCount()!=0;
            _anyRejectHistory = _sessionStore.AllRejectHistoryCount()!=0;
            if (_anyRejectHistory && (_anyMoveHistory || _sessionStore.AllRenameHistoryCount()!=0 ||
                _sessionStore.CopyItems(_work.SessionId).Count!=0 || _copyBarrier))
                throw new InvalidDataException("Reject recovery journal is mixed with another operation; stop. Preserve all owned paths.");
            _moveFixtureEnabled=P5CGuiFixturePolicy.IsMarkedStore(_sessionStore);
            _boundedSuccessorGui=_anyMoveHistory && _moveFixtureEnabled &&
                _sessionStore.InspectGuiSuccessor(_work.CaptureSession()).Verified;
            _boundedThirdGui=_anyMoveHistory && _moveFixtureEnabled &&
                _sessionStore.InspectThirdGuiSession(_work.CaptureSession()).Verified;
            var sessionMoveRows = _sessionStore.MoveItems(_work.SessionId);
            if (_anyMoveHistory && !_boundedSuccessorGui && !_boundedThirdGui &&
                _sessionStore.AllMoveHistoryCount()!=sessionMoveRows.Count)
                throw new InvalidDataException("Move history belongs to a different session. Stop: history cannot be ignored.");
            if (_anyMoveHistory && _sessionStore.CopyItems(_work.SessionId).Count>0)
                throw new P3R2MixedCopyMoveStartupException(
                    "Mixed Copy and Move journal history requires manual recovery; mutations locked.");
            _anyRenameHistory=_sessionStore.AllRenameHistoryCount()!=0;
            var renames=_sessionStore.RenameItems(_work.SessionId);
            if (_anyRenameHistory && (_sessionStore.AllRenameHistoryCount()!=renames.Count || _anyMoveHistory))
                throw new InvalidDataException("Rename history is foreign or mixed with Move; stop.");
            bool hasCopyRows=_sessionStore.CopyItems(_work.SessionId).Count!=0;
            if (_anyRenameHistory && hasCopyRows &&
                !_sessionStore.IsRenameCloseoutVerified(_work.SessionId,_work))
                throw new InvalidDataException("Copy/Rename mixed history lacks verified prior closeout; stop.");
            _moveFixtureEnabled=P5CGuiFixturePolicy.IsMarkedStore(_sessionStore);
            if (_moveFixtureEnabled) _fixtureRenamer=new DurableRenameCoordinator(_sessionStore);
            else if(WindowsLocalRenamePolicy.Enabled)
                _windowsRenameTrial=DurableRenameCoordinator.ForWindowsLocalTrial(_sessionStore);
            else if(NativeFileCapability.IsSupported)
                _ordinaryFirstRenamer=DurableRenameCoordinator.ForOrdinaryFirstEpoch(_sessionStore);
            if (renames.Count>0 && renames.All(r=>r.State is "Committed" or "Undone"))
            {
                if ((!_moveFixtureEnabled && _windowsRenameTrial is null && _ordinaryFirstRenamer is null) &&
                    renames.Any(x=>x.State!="Undone"))
                    throw new InvalidDataException("Committed Rename history requires the correct authorized workspace. Files preserved.");
                // A superseded Undone record is immutable historical evidence, not
                // another live filesystem path. Verify only the latest generation.
                foreach(var group in renames.GroupBy(row=>row.PhotoId))
                {
                    var ordered=group.OrderBy(row=>row.Sequence).ToArray();
                    if (_moveFixtureEnabled && ordered.Length!=1)
                        throw new InvalidDataException("Legacy fixture Rename history is ambiguous.");
                    for(int i=1;i<ordered.Length;i++)
                        if(ordered[i-1].State!="Undone" ||
                           ordered[i].SupersedesRenameId!=ordered[i-1].Id ||
                           ordered[i].ReviewedDirectoryIdentity is null ||
                           ordered[i].ReviewedDirectoryIdentity!=ordered[i-1].ReviewedDirectoryIdentity)
                            throw new InvalidDataException("Rename history dependency chain is invalid.");
                    var tip=ordered[^1];
                    if(!DurableRenameCoordinator.ProveState(tip))
                        throw new InvalidDataException("Rename journal and native file ownership disagree; preserve files.");
                    var now=_work.Photos.SingleOrDefault(x=>x.Id==tip.PhotoId)
                        ?? throw new InvalidDataException("Rename journal photograph missing from checkpoint.");
                    var expected=tip.State=="Committed"?tip.Target:tip.Source;
                    if(!SortingWorkspace.PathComparer.Equals(now.Path,expected))
                    {
                        var from=tip.State=="Committed"?tip.Source:tip.Target;
                        _work.ApplyVerifiedRename(tip.PhotoId,from,expected);
                    }
                }
                _sessionStore.Save(_work.CaptureSession());
            }
            _renameCloseoutVerified=_anyRenameHistory &&
                _sessionStore.IsRenameCloseoutVerified(_work.SessionId,_work);
            if (hasCopyRows && _anyRenameHistory && !_renameCloseoutVerified)
                throw new InvalidDataException("Mixed history cannot be proved after Rename reconciliation.");
            if (_moveFixtureEnabled) _fixtureMover=new DurableMoveCoordinator(_sessionStore);
            else if (NativeFileCapability.IsSupported)
            {
                _fixtureMover=DurableMoveCoordinator.ForOrdinaryFirstEpoch(_sessionStore);
                _ordinaryMoveAvailable=true;
            }
            _journal = new DurableCopyCoordinator(_sessionStore);
            // Only marked generated fixtures can expose native Reject controls.
            // Inspect is read-only: no cut or silent restart replay.
            InitializeNativeRejectGui();
            InitializeOrdinaryRejectGuiProbe();
            TryBindOrdinaryFirstReject();
            if (!_copyBarrier && !_anyMoveHistory && !_anyRejectHistory && !RenameLocksWrites)
            {
                StartupTrace("RECONCILE_BEGIN");
                _journalSummary=_journal.Reconcile(_work.SessionId);
                StartupTrace("RECONCILE_DONE");
                var committed=_journal.CompletedItems(_work.SessionId);
                if (committed.Count>0)
                {
                    _work.RetireCommitted(committed.Select(i=>i.PhotoId));
                    _sessionStore.Save(_work.CaptureSession());
                }
            }
            // A crash after durable Move commit and before checkpoint save must not
            // resurrect an assignment that could be applied as Copy on restart.
            if (sessionMoveRows.Count==1 && sessionMoveRows[0].State=="Committed")
            {
                _work.RetireCommitted(new[] {sessionMoveRows[0].PhotoId});
                _sessionStore.Save(_work.CaptureSession());
            }
            Message(_anyRejectHistory
                ? (OrdinaryRejectGuiProbePolicy.IsRequested ? OrdinaryRejectGuiProbeStartupSummary() :
                    _ordinaryFirstReject is not null ? "Ordinary Reject history: use the verified owned recovery actions. No auto replay." : NativeRejectStartupSummary())
                : _renameCloseoutVerified
                ? "Verified Rename closeout restored Copy and sorting. Historical journal retained; further Rename/Move in this workspace remain locked."
                : _anyRenameHistory
                ? StudioHistoryExperiencePresentation.RenameStartupLocked
                : _boundedThirdGui
                ? "BOUNDED THIRD fixture session restored. First two native Move Undo archives preserved; use dedicated third-Move actions only."
                : _boundedSuccessorGui
                ? "BOUNDED successor fixture session restored. Old Move journal retained; use the dedicated successor actions, not Copy."
                : _anyMoveHistory
                ? "Move history detected. New file operations are locked. Inspect recovery journal or use verified Move Undo where available. Never reset history to bypass the lock."
                : _copyBarrier
                ? "RECOVERY NEEDED: P3A-era copies lack native ownership evidence; old barrier preserved."
                : JournalNeedsAttention ? "Recovery needs attention. Open Inspect recovery journal. " + _journalSummary
                : saved is null ? "Choose a folder or add photos to begin. Looking at photographs never moves the originals."
                : $"Resumed saved session: {_work.Photos.Count} photographs and {_work.AssignmentCount} decisions. Review approval must be renewed. {_journalSummary}");
            }
        }
        catch (Exception ex)
        {
            // Only the explicitly typed, post-open mixed-history veto can leave
            // the read-only observer available. SQLite faults, migrations,
            // foreign journals, and save failures do not receive this exception.
            _p3R2MixedCopyMoveReadOnlyStartup =
                P3R2RecoveryInspectionGate.IsExactlyKnownMixedHistory(ex, _sessionStore is not null);
            _storeHealthy = false; // No sorting or native mutation after ANY startup failure.
            Message(_p3R2MixedCopyMoveReadOnlyStartup
                ? "Mixed Copy and Move history detected. Sorting and all file mutations remain locked. " +
                  "Open Recovery command center for read-only native evidence; no repair or Undo is granted."
                : "Workspace database could not be safely opened: " + ex.Message +
                  " Original database retained. No mutations allowed.");
            _startupFailureBanner.Text = _status.Text;
            _startupFailureBanner.IsVisible = true;
        }
        Closing += (_, e) =>
        {
            if (_studioRenameLease.Active)
            {
                e.Cancel=true;
                if (_studioRenameLease.DecisionPending)
                    Message("Rename review decision is being checked. Wait before closing.");
                else
                {
                    CancelStudioRenameReview();
                    Message("Rename review cancelled. Close again to exit.");
                }
            }
            else if (_studioRejectLease.Active)
            {
                e.Cancel=true;
                if (_studioRejectLease.DecisionPending)
                    Message("Reject review native proof is being checked. Wait before closing.");
                else
                {
                    CancelStudioRejectReview();
                    Message("Reject review cancelled. Close again to exit.");
                }
            }
            else if (_studioEmbeddedLease.Active)
            {
                e.Cancel = true;
                if (_studioEmbeddedLease.DecisionPending)
                    Message("Native Review proof is finishing. Wait before closing the app.");
                else
                {
                    CancelStudioEmbeddedReview();
                    Message("Review cancelled without approval. Close again to exit the app.");
                }
            }
            else if (_busy)
            {
                e.Cancel=true;
                Message("Wait until the current save/file operation finishes before closing.");
            }
        };
        Closed += (_,_) => { _importCts?.Cancel(); _previewWorker.Dispose(); _currentBitmap?.Dispose(); _studioCollectionPreviewBitmap?.Dispose(); _sessionStore?.Dispose(); };
        Refresh();
        if (startupPaths is { Length: > 0 })
        {
            // Never reset a saved workspace on startup. Invalid arguments cannot
            // change previously saved decisions; parsing happens before importing.
            Opened += async (_,_) => {
                try {
                    var cli=CliImportPaths.Parse(startupPaths);
                    await ImportPathsAsync(cli.Paths, replace:false, recursive:cli.Recursive);
                }
                catch(Exception ex) { Message("Command-line import refused: "+ex.Message); }
            };
        }
        StartupTrace("WINDOW_CONSTRUCTOR_COMPLETE");
    }

    private static Button Button(string title, Action action)
    {
        var b = new Button { Content = title, MinHeight = 34 };
        b.Click += (_,_) => action();
        return b;
    }
    private static Button Button(string title, Func<Task> action)
    {
        var b = new Button { Content = title, MinHeight = 34 };
        b.Click += async (_,_) => await action();
        return b;
    }
    private void Message(string text)
    {
        Message(text,text);
    }
    private void Message(string concise, string fullDetail)
    {
        _status.Text = concise;
        ToolTip.SetTip(_status,fullDetail); // Exact import counts and warnings are retained.
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) { Message("Folder picker is not available on this platform."); return null; }
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple=false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task ImportLegacySettingsAsync()
    {
        if (_busy || !EnsureWritable()) return;
        if (_journal is not null && _journal.Outstanding(_work.SessionId) != 0)
        { Message("Finish or resolve applied file history before changing imported routes."); return; }
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) { Message("Local file picker is unavailable."); return; }
        var chosen = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose original PhotoSift Settings.xml (read-only preview)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PhotoSift settings XML (*.xml)")
                {
                    Patterns = new[] { "*.xml" },
                    MimeTypes = new[] { "application/xml", "text/xml" }
                }
            }
        });
        if (chosen.Count == 0) return;
        var path = chosen[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        { Message("Only local Settings.xml files can be imported. No changes."); return; }
        try
        {
            _busy = true;
            var preview = await Task.Run(() => LegacySettingsImporter.Prepare(path, _work));
            var dialog = new Window
            {
                Title = "Preview legacy PhotoSift settings — no changes yet",
                Width = 880, Height = 615, MinWidth = 620, MinHeight = 390,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#101C28")), Foreground = Brushes.White
            };
            var shell = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            shell.Children.Add(new TextBlock { Text = "Import safe settings from old PhotoSift?",
                FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0,0,0,10) });
            var details = new TextBox
            {
                Text = preview.Report, IsReadOnly = true, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, FontSize = 13,
                Background = new SolidColorBrush(Color.Parse("#1A2939")), Foreground = Brushes.White
            };
            ScrollViewer.SetVerticalScrollBarVisibility(details, ScrollBarVisibility.Visible);
            Grid.SetRow(details,1); shell.Children.Add(details);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,10,0,0) };
            buttons.Children.Add(Button("Cancel — leave everything unchanged", () => dialog.Close(false)));
            var accept = Button("Import listed safe settings", () => dialog.Close(true));
            accept.IsEnabled = preview.HasChanges;
            buttons.Children.Add(accept);
            Grid.SetRow(buttons,2); shell.Children.Add(buttons);
            dialog.Content = shell;
            bool accepted = await dialog.ShowDialog<bool>(this);
            if (!accepted)
            { Message("Legacy settings preview closed. Existing routes and files unchanged."); return; }
            // Recheck safety after modal dialog; no other actions may have
            // committed in this window while _busy, and outstanding work blocks.
            if (!EnsureWritable() || (_journal is not null && _journal.Outstanding(_work.SessionId) != 0))
            { Message("Legacy import stopped: journal/workspace state changed."); return; }
            _work.RestoreSession(preview.Candidate);
            _work.RevokeApproval(); _prepared = null; _hasReviewed = false;
            await SaveSessionAsync();
            Message($"Imported {preview.RoutesChanged} safe legacy route(s). Collision mode: {_work.CollisionMode}. " +
                "Photo files unchanged; review exact paths before any Apply.");
        }
        catch (Exception ex)
        {
            var reason = ex is System.Xml.XmlException
                ? "The file is not a valid PhotoSift Settings.xml document. Choose your original Settings.xml; no settings or photographs were changed."
                : "Settings import stopped: " + ex.Message + " No photographs were changed.";
            Message(reason);
            await ShowLegacyFileErrorAsync(reason);
        }
        finally { _busy = false; Refresh(); }
    }

    private async Task AssessMoveReadinessAsync()
    {
        if (_busy || !EnsureWritable()) return;
        if (_sessionStore is null) { Message("Cannot inspect Move: saved workspace unavailable."); return; }
        _busy = true;
        try
        {
            _hasReviewed = false; _prepared = null; _work.RevokeApproval();
            Message("Assessing Move safety without making any file changes…"); Refresh();
            var result = await Task.Run(() => MoveReadinessProbe.InspectAsync(_work, _sessionStore));
            var dialog = new Window
            {
                Title = "Move readiness inspection — preflight only",
                Width = 900, Height = 510, MinWidth = 630, MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#101C28")), Foreground = Brushes.White
            };
            var panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = "Move is NOT enabled — this is a read-only safety inspection",
                FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBox { Text = result.Report, IsReadOnly = true,
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 270, Height = 340 });
            var close = Button("Close — no approval or file changes", () => dialog.Close());
            close.HorizontalAlignment = HorizontalAlignment.Right;
            panel.Children.Add(close);
            dialog.Content = panel;
            await dialog.ShowDialog(this);
            Message("Move readiness inspected. No approval, source relocation, folder creation or Copy was performed.");
        }
        catch (Exception ex)
        { Message("Move preflight refused: " + ex.Message + " No files changed."); }
        finally { _hasReviewed = false; _prepared = null; _work.RevokeApproval(); _busy = false; Refresh(); }
    }

    private async Task ShowLegacyFileErrorAsync(string reason)
    {
        var dialog = new Window
        {
            Title = "Cannot import this settings file — PhotoSift Reborn",
            Width = 610, Height = 225, MinWidth = 490, MinHeight = 195,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#101C28")), Foreground = Brushes.White
        };
        var content = new StackPanel { Margin = new Thickness(19), Spacing = 14 };
        content.Children.Add(new TextBlock { Text = "Choose an original PhotoSift Settings.xml file",
            FontSize = 19, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap });
        var okay = Button("OK — nothing imported", () => dialog.Close());
        okay.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(okay);
        dialog.Content = content;
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmReplaceAsync()
    {
        if (_work.Photos.Count == 0) return true;
        var dialog=new Window { Title="Replace current photo collection?", Width=570, Height=225,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White };
        var shell=new StackPanel { Margin=new Thickness(18), Spacing=12 };
        shell.Children.Add(new TextBlock { Text="Replace the current collection?", FontSize=20 });
        shell.Children.Add(Muted($"This discards {_work.Photos.Count} imported photos and {_work.AssignmentCount} staged choices. For non-destructive addition, choose Add folder, Add files, or drop paths instead."));
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, Spacing=12, HorizontalAlignment=HorizontalAlignment.Right };
        buttons.Children.Add(Button("Keep existing work",()=>dialog.Close(false)));
        buttons.Children.Add(Button("Replace collection",()=>dialog.Close(true)));
        shell.Children.Add(buttons); dialog.Content=shell;
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ChooseInputAsync()
    {
        if (_busy || !EnsureWritable()) return;
        if (_anyRenameHistory)
        { Message("Replace collection refused: this SQLite workspace permanently retains Rename history. Use Add folder/Add files, or a separate fresh workspace after preserving the journal."); return; }
        var path=await PickFolderAsync("Open a new photograph collection (replaces current session choices)");
        if (path is null || !await ConfirmReplaceAsync()) return;
        await ImportPathsAsync([path], replace:true, recursive:_recursiveInput.IsChecked==true);
    }
    private async Task AddInputAsync()
    {
        if (_busy || !EnsureWritable()) return;
        var path=await PickFolderAsync("Add a folder to this saved collection");
        if (path is not null) await ImportPathsAsync([path], replace:false, recursive:_recursiveInput.IsChecked==true);
    }
    private async Task AddFilesAsync()
    {
        if (_busy || !EnsureWritable()) return;
        var provider=TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) { Message("File picker is unavailable on this platform."); return; }
        var files=await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title="Choose the photographs to add (select multiple)",
            AllowMultiple=true,
            FileTypeFilter=new[]
            {
                new FilePickerFileType("Photographs (JPEG, PNG, BMP, GIF)")
                {
                    Patterns=new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif" },
                    MimeTypes=new[] { "image/jpeg", "image/png", "image/bmp", "image/gif" }
                }
            }
        });
        if (files.Count==0) return;
        var paths=files.Select(x=>x.TryGetLocalPath()).ToArray();
        if (paths.Any(string.IsNullOrWhiteSpace)) { Message("A chosen item is not a local file. No import performed."); return; }
        await ImportPathsAsync(paths!,replace:false,recursive:false);
    }
    private async Task ImportPathsAsync(IEnumerable<string?> paths, bool replace, bool recursive)
    {
        if (_busy || !EnsureWritable()) return;
        // The terminal Copy proof is bound to exact historical photo IDs and
        // a saved session. Do not let import/replace invalidate those identities
        // during this conservative continuation increment.
        if (_sessionStore?.AllCopyHistoryCount()!=0)
        { Message("Import requires a separate future history-bound session. Existing Copy recovery is preserved; no source changes."); return; }
        if (replace && _anyRenameHistory)
        { Message("Replacing a collection with retained Rename history is forbidden. Add input non-destructively or start a separate workspace."); return; }
        var items=paths.ToArray();
        if (items.Any(string.IsNullOrWhiteSpace)) { Message("Import refused: empty/non-local path."); return; }
        try
        {
            _busy=true; _importCts=new CancellationTokenSource(); _cancelImport.IsEnabled=true;
            Message(recursive ? "Scanning folders and subfolders (no links followed)…" :
                "Scanning selected files and top-level folders…");
            var progress=new Progress<int>(n=>Message($"Discovered {n} new photographs; Cancel safely aborts unsaved scan…"));
            var report=await Task.Run(()=>_work.ImportPaths(items.Select(x=>x!),replace,recursive,_importCts.Token,progress));
            _hasReviewed=false; _prepared=null;
            await SaveSessionAsync();
            string warnings=report.Warnings.Count==0 ? "" : " First warning: "+report.Warnings[0];
            var importDetail=(replace ? "Opened collection. " : "Added to existing collection. ") + report.Summary +
                $"; {_work.Photos.Count} total. Previous approvals expired if the collection changed." +
                (string.IsNullOrWhiteSpace(_work.DestinationRoot)
                    ? " Next: choose Results folder (not the originals folder)." : "") + warnings;
            Message(StudioImportStatusPresentation.Short(report.Added.Count,_work.Photos.Count,
                !string.IsNullOrWhiteSpace(_work.DestinationRoot),report.Warnings.Count),importDetail);
        }
        catch(OperationCanceledException) { Message("Import cancelled. Existing photos, assignments and saved work remain unchanged."); }
        catch(Exception ex) { Message("Import refused; existing session preserved: "+ex.Message); }
        finally { _busy=false; _cancelImport.IsEnabled=false; _importCts?.Dispose(); _importCts=null; Refresh(); }
    }

    private async Task CycleCollisionChoiceAsync()
    {
        if (_busy || !EnsureWritable()) return;
        try
        {
            _busy=true;
            var next=_work.CollisionMode switch
            {
                CollisionChoice.Refuse=>CollisionChoice.Number,
                CollisionChoice.Number=>CollisionChoice.Skip,
                _=>CollisionChoice.Refuse
            };
            _work.SetCollisionChoice(next);
            _hasReviewed=false; _prepared=null;
            await SaveSessionAsync();
            Message($"Collision choice: {next}. Number resolves exact names during Review; Skip leaves collisions staged. No automatic overwrites.");
        }
        catch(Exception ex) { Message("Cannot save collision choice: "+ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private async Task ChooseOutputAsync()
    {
        if (_busy || !EnsureWritable()) return;
        var path = await PickFolderAsync("Choose a different folder where copies will go");
        if (path is null) return;
        try
        {
            _busy = true;
            _work.SetDestinationRoot(path); _hasReviewed = false;
            await SaveSessionAsync();
            Message("Output folder saved. No disk changes.");
        }
        catch (Exception e) { Message("Output refused: " + e.Message); }
        finally { _busy=false; Refresh(); }
    }

    private char ReadRouteKey()
    {
        if (_newKey.Text?.Trim().Length != 1)
            throw new ArgumentException("Enter one A–Z or 0–9 route key.");
        char key = char.ToUpperInvariant(_newKey.Text.Trim()[0]);
        if (!SortingWorkspace.IsValidKey(key)) throw new ArgumentException("Use A–Z or 0–9.");
        return key;
    }

    private async Task AddRouteAsync()
    {
        if (_busy || !EnsureWritable()) return;
        _busy=true;
        try
        {
            char key=ReadRouteKey();
            switch (_routeKind.SelectedIndex)
            {
                case 0: _work.SetDefaultRoute(key); break;
                case 1: _work.SetRelativeRoute(key,_newLabel.Text ?? ""); break;
                case 2: throw new InvalidOperationException("For an Absolute route, click Pick absolute folder… instead of typing a path.");
                default: throw new InvalidOperationException("Select a valid route type.");
            }
            _hasReviewed = false; _prepared=null; _reviewHint.IsVisible=false;
            await SaveSessionAsync();
            Message($"Route {key} saved. Press {key} on a photo, then Review the exact output path.");
        }
        catch (Exception ex) { Message("Route refused: " + ex.Message); _reviewHint.Text="Route refused: "+ex.Message; _reviewHint.IsVisible=true; }
        finally { _busy=false; Refresh(); }
    }

    private async Task PickAbsoluteRouteAsync()
    {
        if (_busy || !EnsureWritable()) return;
        char key;
        try { key=ReadRouteKey(); }
        catch(Exception ex) { Message(ex.Message); return; }
        var path = await PickFolderAsync($"Choose an absolute destination folder for route {key}");
        if (path is null) return;
        try
        {
            _busy=true;
            _work.SetAbsoluteRoute(key,path);
            _newLabel.Text=path; _routeKind.SelectedIndex=2;
            _hasReviewed=false; _prepared=null; _reviewHint.IsVisible=false;
            await SaveSessionAsync();
            Message($"Absolute route {key} saved. Always Review before Apply.");
        }
        catch(Exception ex) { Message("Absolute route refused: "+ex.Message); _reviewHint.Text="Absolute route refused: "+ex.Message; _reviewHint.IsVisible=true; }
        finally { _busy=false; Refresh(); }
    }

    private async Task AssignAsync(char key)
    {
        if (_busy || !EnsureWritable()) return;
        _busy=true;
        try
        {
            // The photo must be actually displayed, never just selected while decoding.
            if (!_displayReady || _work.Current?.Id != _displayedPhotoId)
                throw new InvalidOperationException("Wait until the selected photograph is visible before sorting.");
            _work.Assign(key);
            _hasReviewed = false;
            await SaveSessionAsync();
            Message($"Saved choice {key}. No files written. Review exact paths before Apply.");
        }
        catch (Exception ex) { Message("Assignment refused: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }
    private async Task NavigateAsync(int direction)
    {
        if (_busy || !EnsureViewerNavigationAllowed()) return;
        try
        {
            _busy=true; _work.Navigate(direction); await SaveSessionAsync();
        }
        catch (Exception ex) { Message("Cannot save navigation: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }
    private async Task JumpToAsync(int number)
    {
        if (_busy || !EnsureViewerNavigationAllowed()) return;
        try
        {
            _busy=true; _work.JumpToPhoto(number); await SaveSessionAsync();
            Message($"Jumped to photograph {number} of {_work.Photos.Count}. No file changes.");
        }
        catch(Exception ex) { Message("Cannot jump: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private async Task JumpDialogAsync()
    {
        if (_busy || _work.Photos.Count==0 || !EnsureViewerNavigationAllowed()) return;
        var dlg = new Window { Title="Jump to photograph — PhotoSift Reborn", Width=400, Height=200,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White };
        var shell=new StackPanel { Margin=new Thickness(19), Spacing=12 };
        shell.Children.Add(Muted($"Enter a photo number from 1 to {_work.Photos.Count}. This only changes the viewer position."));
        var input=new TextBox { Text=(_work.Index < _work.Photos.Count ? _work.Index+1 : _work.Photos.Count).ToString(),
            PlaceholderText="1-based photograph number" };
        shell.Children.Add(input);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Spacing=10 };
        buttons.Children.Add(Button("Cancel",()=>dlg.Close((int?)null)));
        buttons.Children.Add(Button("Jump",()=> {
            if(int.TryParse(input.Text,out int n) && n>=1 && n<=_work.Photos.Count) dlg.Close((int?)n);
            else Message($"Enter a number from 1 to {_work.Photos.Count}.");
        }));
        shell.Children.Add(buttons); dlg.Content=shell;
        var selected=await dlg.ShowDialog<int?>(this);
        if(selected is { } n) await JumpToAsync(n);
    }

    private async Task JumpUnassignedAsync()
    {
        if (_busy || !EnsureViewerNavigationAllowed()) return;
        try
        {
            _busy=true;
            bool found=_work.JumpToNextUnassigned();
            if(found) await SaveSessionAsync();
            Message(found ? $"Next unassigned photograph: {_work.Index+1} of {_work.Photos.Count}." :
                "No unassigned photographs in this collection. No changes made.");
        }
        catch(Exception ex) { Message("Cannot navigate unassigned photos: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private async Task ShuffleAsync()
    {
        if (_busy || !EnsureWritable() || _work.Photos.Count<2) return;
        try
        {
            _busy=true;
            _work.ShufflePhotos(); _prepared=null; _hasReviewed=false;
            await SaveSessionAsync();
            Message("Photo browsing order shuffled and saved. Stable photo IDs and staged decisions preserved. Review approval expired; no disk changes.");
        }
        catch(Exception ex) { Message("Cannot shuffle: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private async Task RemoveFromSessionAsync()
    {
        if (_busy || !EnsureWritable() || _work.Current is not { } photo) return;
        if (_sessionStore?.AllCopyHistoryCount()!=0)
        { Message("Removal refused: terminal Copy history still binds these stable photograph identities. No files changed."); return; }
        if (_anyRenameHistory && _sessionStore?.RenameItems(_work.SessionId).Any(row=>row.PhotoId==photo.Id)==true)
        { Message("Removal refused: this photograph is named in the immutable Rename journal, including after Undo. Keep it in this history-owned workspace."); return; }
        if (_journal?.CompletedItems(_work.SessionId).Any(item=>item.PhotoId==photo.Id)==true)
        { Message("Removal blocked: this photo has committed journal history. Resolve or undo its copies first."); return; }
        var dlg = new Window { Title="Remove from session? — PhotoSift Reborn", Width=560, Height=225,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White };
        var shell=new StackPanel { Margin=new Thickness(18), Spacing=12 };
        shell.Children.Add(new TextBlock { Text="Remove this photograph from the list?", FontSize=19 });
        shell.Children.Add(Muted(Path.GetFileName(photo.Path)+" will be hidden from this saved sorting session only. The original file will remain untouched. Use Undo remove to restore the same photo ID. Assigned photos must first have their choices undone."));
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, Spacing=10, HorizontalAlignment=HorizontalAlignment.Right };
        buttons.Children.Add(Button("Keep in session",()=>dlg.Close(false)));
        buttons.Children.Add(Button("Remove from session only",()=>dlg.Close(true)));
        shell.Children.Add(buttons); dlg.Content=shell;
        if (!await dlg.ShowDialog<bool>(this)) return;
        try
        {
            _busy=true;
            _work.RemoveCurrentFromSession(); _prepared=null; _hasReviewed=false;
            await SaveSessionAsync();
            Message("Removed from sorting session only. Original photograph stays on disk. Undo remove restores it.");
        }
        catch(Exception ex) { Message("Session removal refused: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    // Review-only Reject staging, not a filesystem Reject/Trash operation.
    // Deliberately unavailable while any Copy, Move or Rename journal exists:
    // the future durable Reject-to-recovery flow will have a separate authority.
    private bool CanStageRejectReview() =>
        !_busy && _storeHealthy && !_copyBarrier && !JournalNeedsAttention &&
        !_anyMoveHistory && !_anyRenameHistory && !_anyRejectHistory && !RenameLocksWrites &&
        _sessionStore is not null && _journal is not null &&
        _sessionStore.CopyItems(_work.SessionId).Count == 0;

    private async Task MarkRejectReviewAsync()
    {
        if (!CanStageRejectReview() || _work.Current is not { } photo) return;
        var dlg = new Window { Title="Mark for Reject review — NO files moved", Width=640, Height=260,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White };
        var shell = new StackPanel { Margin=new Thickness(18), Spacing=12 };
        shell.Children.Add(new TextBlock { Text="Mark this photograph for later Reject review?", FontSize=19,
            TextWrapping=TextWrapping.Wrap });
        shell.Children.Add(Muted(Path.GetFileName(photo.Path)+
            " will remain in its original folder. This only saves a reversible Reject-review mark. " +
            "It is NOT Trash, deletion, Move or an approved Reject-to-recovery operation."));
        var buttons = new StackPanel { Orientation=Orientation.Horizontal, Spacing=10,
            HorizontalAlignment=HorizontalAlignment.Right };
        buttons.Children.Add(Button("Keep photograph",()=>dlg.Close(false)));
        buttons.Children.Add(Button("Mark for later review ONLY",()=>dlg.Close(true)));
        shell.Children.Add(buttons); dlg.Content=shell;
        if (!await dlg.ShowDialog<bool>(this)) return;
        try
        {
            if (!CanStageRejectReview()) { Message("Reject review refused: workspace/history changed. No files moved."); return; }
            _busy=true;
            _work.MarkCurrentForRejectReview(); _prepared=null; _hasReviewed=false;
            await SaveSessionAsync();
            Message("Reject review marked and saved. Original file untouched; Undo Reject-review mark to restore the choice.");
        }
        catch(Exception ex) { Message("Reject review refused: " + ex.Message + ". No files moved."); }
        finally { _busy=false; Refresh(); }
    }

    private async Task UndoRejectReviewAsync()
    {
        if (!CanStageRejectReview()) return;
        try
        {
            _busy=true;
            _work.UndoLastRejectReview(); _prepared=null; _hasReviewed=false;
            await SaveSessionAsync();
            Message("Last Reject-review mark undone and saved. Original file was never moved.");
        }
        catch(Exception ex) { Message("Undo Reject-review mark refused: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private async Task UndoRemoveAsync()
    {
        if (_busy || !EnsureWritable()) return;
        if (_sessionStore?.AllCopyHistoryCount()!=0)
        { Message("Undo session removal is not authorized across retained Copy history. No changes."); return; }
        try
        {
            _busy=true;
            _work.UndoLastSessionRemoval(); _prepared=null; _hasReviewed=false;
            await SaveSessionAsync();
            Message("Last removed photo restored to this session with original stable ID. No file changes.");
        }
        catch(Exception ex) { Message("Undo remove refused: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private async Task ShowHelpAsync()
    {
        var dlg = new Window { Title="PhotoSift Reborn — keyboard and safety help", Width=680, Height=540,
            MinWidth=500, MinHeight=360, WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White };
        var text="SORT AND NAVIGATE\n"+
            "←/→ — previous/next photograph\nCtrl+J — jump to photograph number\nCtrl+U — next unassigned (wraps)\n"+
            "A–Z or 0–9 — assign shown photograph to destination and advance\nCtrl+Z — undo last assignment\n"+
            "F12 — inspect original pixels; F11 — full screen; Esc — leave full screen\nF1 — this help\n\n"+
            "PHOTO LIST (SESSION ONLY)\n"+
            "Shuffle order preserves photo IDs and assignments; requires a new Review.\n"+
            "Mark Reject for review stages a reversible choice only; original files never move or delete. Undo Reject mark survives restart.\n"+
            "Review marked photos shows a durable multi-photo Reject-review queue; you can jump to any marked photo or unmark one chosen photo without changing the others. This is not native Reject-to-recovery authorization.\n"+
            "Remove from session is NOT Reject or Delete. Files never move; Undo remove restores the photo, including after restart.\n"+
            "Undo a photo's staged assignment before removing it. Photos with committed copy history are protected.\n\n"+
            "COPY SAFETY\n"+
            "Review shows exact targets and collision numbers; Cancel never approves.\n"+
            "Apply is enabled only after explicit approval. No automatic overwrites.\n"+
            "Copy Undo archives verified owned outputs to recovery; uncertain ownership blocks mutation.\n"+
            "After a complete Undo, use Verify completed Copy Undo → resume sorting before editing or copying again. This never removes recovery history.\n"+
            "Move runs ONLY on generated P5C disposable GUI fixtures; it is disabled for personal photographs. Rename is fixture-only by default; the Windows NTFS trial requires explicit opt-in and disposable test photos until verified. Reject is not enabled for filesystem mutation: Reject review is only a saved reversible mark. Safe legacy XML settings import is available.";
        var panel=new Grid { Margin=new Thickness(19), RowDefinitions=new RowDefinitions("Auto,*,Auto") };
        panel.Children.Add(new TextBlock { Text="Shortcuts and safe photo-session controls", FontSize=21, FontWeight=FontWeight.SemiBold, Margin=new Thickness(0,0,0,12) });
        var scroll=new ScrollViewer { Content=new TextBlock { Text=text, TextWrapping=TextWrapping.Wrap,
            Foreground=Brushes.White, FontSize=14 } };
        Grid.SetRow(scroll,1);panel.Children.Add(scroll);
        var close=Button("Close help",()=>dlg.Close());close.HorizontalAlignment=HorizontalAlignment.Right;
        close.Margin=new Thickness(0,10,0,0);Grid.SetRow(close,2);panel.Children.Add(close);
        dlg.Content=panel;
        await dlg.ShowDialog(this);
    }

    private async Task UndoStageAsync()
    {
        if (_busy || !EnsureWritable()) return;
        try
        {
            _busy=true; _work.UndoDecision(); _hasReviewed=false;
            await SaveSessionAsync();
            Message("Last staged assignment undone and saved. No disk writes.");
        }
        catch (Exception ex) { Message("Undo choice refused: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }
    private void SelectOperationMode(ReviewOperationMode mode)
    {
        if (_busy || !_storeHealthy || _copyBarrier || _anyMoveHistory || RenameLocksWrites) return;
        if (mode==ReviewOperationMode.Move && _anyRenameHistory)
        { Message("Move is not authorized after Rename closeout in this journal epoch; use reviewed Copy."); return; }
        if (mode==ReviewOperationMode.Move && !MoveModeAvailable)
        { Message("Move is unavailable on this filesystem. Use reviewed Copy."); return; }
        _modeGate.Select(mode);
        _pendingRename=null; _reviewedMoveId=null; _prepared=null; _hasReviewed=false;
        _work.RevokeApproval();
        Message(mode==ReviewOperationMode.Copy
            ? "COPY selected. Review exact paths again; source photographs remain unchanged."
            : (_moveFixtureEnabled ? "MOVE selected for GENERATED DISPOSABLE FIXTURE ONLY. Review one exact source, target and recovery archive. No approval inherited." : "ORDINARY MOVE — first local operation only. Review exact original, output and owned archive; approval must be explicit."));
        Refresh();
    }

    private async Task InspectProductionRenameReadinessAsync()
    {
        var store = _sessionStore;
        var photo = _work.Current;
        if (_busy || !_storeHealthy || store is null || photo is null) return;
        // The source is hashed to provide READ-ONLY snapshot evidence. Nothing is
        // approved, journaled, moved, renamed or deleted by this command.
        var proposal = Path.GetFileNameWithoutExtension(photo.Path) + "_renamed" + Path.GetExtension(photo.Path);
        try
        {
            _busy = true; Refresh();
            var report = await Task.Run(() => ProductionRenameReadiness.Inspect(store, _work, photo, proposal));
            var dialog = new Window { Title="Rename readiness — READ ONLY (not authorization)",
                Width=810, Height=490, MinWidth=540, MinHeight=300,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White };
            var content=new Grid { Margin=new Thickness(16),RowDefinitions=new RowDefinitions("Auto,*,Auto") };
            content.Children.Add(new TextBlock { Text="Read-only Rename safety inspection", FontSize=21,
                FontWeight=FontWeight.SemiBold });
            var summary = report.PreliminaryChecksPass
                ? "PRELIMINARY CHECKS PASS. This display does NOT approve execution. Use the separately reviewed Rename action if its native gate permits."
                : "PRELIMINARY CHECKS BLOCKED. No mutation authorized by this report.";
            var details=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
                Background=new SolidColorBrush(Color.Parse("#1A2939")),Foreground=Brushes.White,
                Text=summary+"\n\nSOURCE  "+report.Source+"\nTARGET  "+report.ProposedTarget+
                "\nNATIVE ID  "+(report.ObservedIdentity??"not proved")+
                "\nSHA-256  "+(report.ObservedSha256??"not proved")+
                "\n\nBLOCKERS\n"+(report.Blockers.Count==0?"None in this read-only snapshot.":
                    string.Join("\n",report.Blockers.Select(x=>"• "+x)))+
                "\n\nThis is READ ONLY, not executable approval. First ordinary local Rename has a separate exact review, explicit consent, durable intent and Undo. Later epochs and mixed histories remain blocked."};
            Grid.SetRow(details,1);content.Children.Add(details);
            var close=new Button { Content="Close — no changes",MinHeight=40,
                HorizontalAlignment=HorizontalAlignment.Right };
            close.Click+=(_,_)=>dialog.Close();
            Grid.SetRow(close,2);content.Children.Add(close);dialog.Content=content;
            await dialog.ShowDialog(this);
            Message("Read-only Rename inspection completed. No source or journal changes; separate first-epoch Rename approval is required.");
        }
        catch(Exception ex) { Message("Rename readiness inspection unavailable: "+ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    private bool CanStartNextFixtureRename()
        => !_busy && _storeHealthy && !_copyBarrier && !_anyMoveHistory &&
            !_renameCloseoutVerified && _sessionStore is not null && ((_fixtureRenamer is not null &&
            FixtureRenameGuiGate.CanPrepare(_sessionStore,_work)) ||
            (_windowsRenameTrial is not null &&
            WindowsRenameGuiGate.CanPrepare(_sessionStore,_work)) ||
            (_ordinaryFirstRenamer is not null &&
            OrdinaryFirstRenameGuiGate.CanPrepare(_sessionStore,_work)));

    private bool CanUndoFixtureRename()
        => !_busy && _storeHealthy && !_copyBarrier && !_anyMoveHistory &&
            !_renameCloseoutVerified && _sessionStore is not null && ((_fixtureRenamer is not null &&
            FixtureRenameGuiGate.CanUndo(_sessionStore,_work)) ||
            (_windowsRenameTrial is not null &&
            WindowsRenameGuiGate.CanUndo(_sessionStore,_work)) ||
            (_ordinaryFirstRenamer is not null &&
            OrdinaryFirstRenameGuiGate.CanUndo(_sessionStore,_work)));

    private async Task RenameDialogAsync()
    {
        if (!CanStartNextFixtureRename() || _sessionStore is null ||
            (_fixtureRenamer is null && _windowsRenameTrial is null && _ordinaryFirstRenamer is null) || _work.Current is null) return;
        var engine=_ordinaryFirstRenamer ?? _windowsRenameTrial ?? _fixtureRenamer!;
        bool productionTrial=_windowsRenameTrial is not null;
        bool ordinary=_ordinaryFirstRenamer is not null;
        var photo=_work.Current;
        // Distinct Rename authority: do not keep a pending Copy/Move approval.
        _pendingRename=null;_hasReviewed=false;_prepared=null;_reviewedMoveId=null;
        _modeGate.Select(ReviewOperationMode.Copy);_work.RevokeApproval();
        var dialog=new Window {Title=ordinary?"Rename one ordinary local photograph":productionTrial?"F2 Rename — WINDOWS LOCAL NTFS TRIAL":"F2 Rename — disposable fixture only",Width=780,Height=290,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White};
        var stack=new StackPanel {Spacing=13,Margin=new Thickness(18)};
        stack.Children.Add(new TextBlock {Text=ordinary?"Rename ONE ordinary photograph — explicit approval required":productionTrial?"Rename original in local NTFS folder — explicit trial approval required":"Rename one ORIGINAL generated fixture photo — no change before review",
            FontSize=19,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap});
        stack.Children.Add(Muted(ordinary
            ? "First native Rename on local NTFS/ext4/xfs/btrfs only. Original name changes; exact Undo requires owned identity. Do not test personal photos before owner acceptance."
            : productionTrial
            ? "WINDOWS-ONLY EXPERIMENTAL trial. Use GENERATED ordinary test photographs until acceptance. Same directory and extension; no overwrite; no Linux support."
            : "The exact original folder is preserved. Image extension cannot change. No overwrite, case-only rename or personal-file access."));
        stack.Children.Add(Muted("CURRENT: "+photo.Path));
        var name=new TextBox {Text=Path.GetFileNameWithoutExtension(photo.Path)+"_renamed"+Path.GetExtension(photo.Path),
            PlaceholderText="New image filename (same extension)",MinHeight=38};
        stack.Children.Add(name);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Spacing=9,
            HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button {Content="Cancel",MinHeight=38};
        var next=new Button {Content="Review proposed Rename",MinHeight=38};
        cancel.Click+=(_,_)=>dialog.Close((string?)null);
        next.Click+=(_,_)=>dialog.Close(name.Text);
        buttons.Children.Add(cancel);buttons.Children.Add(next);stack.Children.Add(buttons);
        dialog.Content=stack;
        var proposal=await dialog.ShowDialog<string?>(this);
        if(proposal is null) {Message("Rename cancelled. No file changes.");Refresh();return;}
        try
        {
            _busy=true;Refresh();
            if(!productionTrial && !ordinary) P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);
            var frozen=await Task.Run(()=>(productionTrial || ordinary)
                ? engine.PrepareOwnedFixture(_work,photo,proposal)
                : engine.Prepare(_work,photo,proposal));
            // M6A: ordinary Rename initiated from Changes has a contained,
            // exclusive exact-path Review. Generated fixture/Focus paths retain
            // the accepted standalone native dialog and consent wording.
            bool approved;
            if (ordinary && _studioActivePage == StudioPage.Changes)
            {
                approved=await ShowStudioRenameReviewAsync(frozen);
                if (approved)
                    StudioRenameReviewGate.RequireFrozen(frozen,_work.SessionId,_work.Revision,
                        _work.Current?.Id,_work.Current?.Path);
            }
            else
            {
                var review=new Window {Title="Review exact F2 Rename — no changes yet",Width=880,Height=440,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner,
                    Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White};
                var content=new Grid {Margin=new Thickness(17),RowDefinitions=new RowDefinitions("Auto,*,Auto")};
                content.Children.Add(new TextBlock {Text=ordinary?"Approve this ONE ordinary Rename?":productionTrial?"Approve Windows local Rename TRIAL?":"Approve TEST-FIXTURE Rename?",FontSize=22,FontWeight=FontWeight.SemiBold});
                var details=new TextBox {Text="RENAME / SAME DIRECTORY / ORIGINAL EXTENSION\n"+
                    "SOURCE  "+frozen.Source+"\nTARGET  "+frozen.Target+"\n"+
                    "STABLE PHOTO ID  "+frozen.PhotoId+"\nSOURCE NATIVE ID  "+frozen.NativeIdentity+
                    "\nSOURCE SHA-256  "+frozen.Hash+
                    "\n\nNative no-replace rename; durable journal before mutation. " +
                    "Undo restores exact original name if vacant and identity/hash still match. " +
                    "Partial/interrupted operations require READ-ONLY inspection; NO automatic retry.",
                    IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
                    Background=new SolidColorBrush(Color.Parse("#1A2939")),Foreground=Brushes.White};
                Grid.SetRow(details,1);content.Children.Add(details);
                var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8,
                    HorizontalAlignment=HorizontalAlignment.Right};
                var no=new Button {Content="Cancel — no approval",MinHeight=40};
                var yes=new Button {Content=ordinary?"Approve THIS ONE Rename":productionTrial?"Approve LOCAL RENAME TRIAL":"Approve TEST Rename",MinHeight=40};
                no.Click+=(_,_)=>review.Close(false);
                yes.Click+=(_,_)=>review.Close(true);
                var approvalFooter=new StackPanel {Spacing=8};
                if(productionTrial || ordinary)
                {
                    var confirmation=new CheckBox {Content=ordinary
                        ? "I reviewed the exact original and target; authorize ONE same-directory native Rename on my local collection."
                        : "I reviewed the EXACT original and target. I authorize ONE native Rename on my own LOCAL NTFS folder.",
                        IsChecked=false};
                    yes.IsEnabled=false;
                    confirmation.Click+=(_,_)=>yes.IsEnabled=confirmation.IsChecked==true;
                    approvalFooter.Children.Add(confirmation);
                }
                actions.Children.Add(no);actions.Children.Add(yes);
                approvalFooter.Children.Add(actions);
                Grid.SetRow(approvalFooter,2);content.Children.Add(approvalFooter);
                review.Content=content;
                approved=await review.ShowDialog<bool>(this);
            }
            if(approved) { _pendingRename=frozen;Message(ordinary
                ? "Approved ONE ordinary native Rename. Apply once; original file unchanged until then."
                : productionTrial
                ? "Approved Windows local Rename TRIAL. Apply once; no files changed yet."
                : "Approved TEST-FIXTURE Rename. Click Apply approved TEST Rename; no files changed yet."); }
            else Message("Rename review cancelled; original unchanged.");
        }
        catch(Exception ex) {Message("Rename review REFUSED: "+ex.Message);_pendingRename=null;}
        finally {EndStudioRenameReview();_busy=false;Refresh();}
    }

    private async Task ApplyRenameAsync()
    {
        if (_busy || _pendingRename is null ||
            (_fixtureRenamer is null && _windowsRenameTrial is null && _ordinaryFirstRenamer is null) ||
            _sessionStore is null || !CanStartNextFixtureRename()) return;
        var engine=_ordinaryFirstRenamer ?? _windowsRenameTrial ?? _fixtureRenamer!;
        bool productionTrial=_windowsRenameTrial is not null;
        bool ordinary=_ordinaryFirstRenamer is not null;
        var frozen=_pendingRename;
        _pendingRename=null;
        try
        {
            _busy=true;Refresh();
            Message(ordinary
                ? "Ordinary Rename: writing durable intent then native no-replace same-directory operation…"
                : productionTrial
                ? "Windows local NTFS TRIAL: durable intent, verified native no-replace Rename…"
                : "Renaming one generated fixture photograph with durable intent and native no-replace…");
            var row=await Task.Run(()=>engine.Apply(_work,frozen));
            _anyRenameHistory=true;
            _work.ApplyVerifiedRename(row.PhotoId,row.Source,row.Target);
            await SaveSessionAsync();
            _requestedPhotoId=null;
            Message(ordinary
                ? "ORDINARY RENAME COMMITTED: original filename changed, history saved. Restart for independently verified Undo; no automatic retry."
                : productionTrial
                ? "Local Rename TRIAL committed and journaled. Restart and verified Undo are available; never retry an ambiguous operation."
                : "TEST Rename committed and journaled. Choose another unused fixture photo for a distinct Rename, or restart and Undo newest-first. Personal-file Rename disabled.");
        }
        catch(Exception ex) {Message("Rename operation REFUSED / NEEDS INSPECTION: "+ex.Message);}
        finally
        {
            try { _anyRenameHistory=_sessionStore.AllRenameHistoryCount()!=0; }
            catch {_storeHealthy=false;}
            _busy=false;Refresh();
        }
    }

    private async Task UndoRenameAsync()
    {
        if(_busy || !CanUndoFixtureRename() ||
            (_fixtureRenamer is null && _windowsRenameTrial is null && _ordinaryFirstRenamer is null) || _sessionStore is null) return;
        var engine=_ordinaryFirstRenamer ?? _windowsRenameTrial ?? _fixtureRenamer!;
        try
        {
            _busy=true;Refresh();
            var row=await Task.Run(()=>engine.Undo(_work.SessionId));
            _work.ApplyVerifiedRename(row.PhotoId,row.Target,row.Source);
            await SaveSessionAsync();
            _requestedPhotoId=null;
            Message("RENAME UNDONE: newest committed native Rename restored; journal and prior evidence retained.");
        }
        catch(Exception ex) {Message("Rename Undo REFUSED; preserve all files and inspect journal. "+ex.Message);}
        finally {_busy=false;Refresh();}
    }

    private bool CanCloseoutRename()
    {
        if (_busy || !_storeHealthy || _copyBarrier || !_anyRenameHistory ||
            _renameCloseoutVerified || _anyMoveHistory || _sessionStore is null)
            return false;
        try
        {
            return _sessionStore.CopyItems(_work.SessionId).Count==0 &&
                _sessionStore.RenameItems(_work.SessionId) is {Count: > 0} rows &&
                rows.Count==_sessionStore.AllRenameHistoryCount() &&
                rows.All(row=>row.State=="Undone");
        }
        catch {return false;}
    }

    private async Task CloseoutRenameAsync()
    {
        if(!CanCloseoutRename() || _sessionStore is null) return;
        _busy=true;Refresh();
        try
        {
            // No filesystem mutation. Re-prove the current original files and
            // durable journal, then seal an immutable Copy-only continuation.
            await SaveSessionAsync();
            await Task.Run(()=>_sessionStore.SealFullyUndoneRename(_work));
            _renameCloseoutVerified=_sessionStore.IsRenameCloseoutVerified(_work.SessionId,_work);
            if(!_renameCloseoutVerified) throw new IOException("The Rename closeout proof could not be established.");
            _pendingRename=null;_hasReviewed=false;_prepared=null;_reviewedMoveId=null;
            _modeGate.Select(ReviewOperationMode.Copy);_modeGate.Revoke();_work.RevokeApproval();
            Message("RENAME HISTORY VERIFIED AND CLOSED OUT. Sorting and reviewed Copy are available again. Prior journal records remain; Rename/Move are locked for this historical workspace.");
        }
        catch(Exception ex)
        {
            _renameCloseoutVerified=false;
            Message("Rename closeout REFUSED: "+ex.Message+". Files untouched; inspect recovery evidence.");
        }
        finally {_busy=false;Refresh();}
    }

    private async Task BeginFixtureSuccessorAsync()
    {
        if (_busy || !_storeHealthy || _copyBarrier || !_moveFixtureEnabled ||
            _boundedSuccessorGui || _sessionStore is null) return;
        try
        {
            _busy=true;
            var db=_sessionStore;
            P5CGuiFixturePolicy.VerifyExactWorkspace(db,_work);
            if (!db.InspectTerminalMoveHistory().CanConsiderLaterAuthorization ||
                db.AllMoveHistoryCount()!=1 || _work.Photos.Count!=3)
                throw new IOException("Historical Move Undo is not completely native-verified.");
            bool accepted=await ShowBoundedNextMoveConfirmAsync(
                "The old Move and Undo evidence will remain in SQLite and native recovery. " +
                "Only synthetic photo_02.png will enter the new saved session. " +
                "Copy, personal-photo Move, and further epochs stay locked. This creates no new Move Intent.");
            if(!accepted) {Message("New Move session canceled; all files and the original checkpoint remain.");return;}
            await Task.Run(()=>db.SealTerminalMoveHistory());
            var next=await Task.Run(()=>db.BeginFirstGuiSuccessor(_work.CaptureSession()));
            _work.RestoreSession(next);
            _boundedSuccessorGui=db.InspectGuiSuccessor(_work.CaptureSession()).Verified;
            if(!_boundedSuccessorGui) throw new IOException("Bounded successor checkpoint inspection failed; restart read-only.");
            _modeGate.Revoke();_work.RevokeApproval();_prepared=null;
            _approvedSuccessorTicket=null;_approvedSuccessorPreparation=null;
            _hasReviewed=false;_reviewedMoveId=null;_requestedPhotoId=null;
            Message("NEXT TEST MOVE SESSION SAVED. Original Move journal and Undo archives preserved. Stage photo_02.png; review/approve again.");
        }
        catch(Exception ex)
        {
            // A failure after the atomic commit may still have switched the DB.
            // Never guess which checkpoint owns the workspace: require restart.
            _storeHealthy=false;
            Message("Successor transition failed or uncertain; restart and inspect saved journal. "+ex.Message);
        }
        finally {_busy=false;Refresh();}
    }

    private async Task StageFixtureSuccessorAsync()
    {
        if(_busy || !SuccessorNoMoveRows() || _work.AssignmentCount!=0 ||
            _work.Current is not { } p || Path.GetFileName(p.Path)!="photo_02.png") return;
        try
        {
            _busy=true;
            _work.Assign('F');
            await SaveSessionAsync();
            Message("STAGED: generated photo_02.png → Family. No files written; review and authorize separately.");
        }
        catch(Exception ex) {Message("Successor staging refused: "+ex.Message);}
        finally {_busy=false;Refresh();}
    }

    private bool SuccessorNoMoveRows()
    {
        if(!_boundedSuccessorGui || !_storeHealthy || !_moveFixtureEnabled ||
            _fixtureMover is null || _sessionStore is null || _copyBarrier) return false;
        try {return _sessionStore.MoveItems(_work.SessionId).Count==0 &&
            _sessionStore.InspectGuiSuccessor(_work.CaptureSession()).Verified;}
        catch(Exception) {return false;}
    }

    private DurableMoveItem MakeGuiSuccessorTicket(PreparedCopy p,Guid id)
    {
        if(_sessionStore is null || id==Guid.Empty)
            throw new IOException("Missing bounded successor installation.");
        var installation=_sessionStore.MoveInstallationId();
        string folder=Path.GetDirectoryName(p.Destination)!;
        string operationRoot=Path.Combine(Path.GetDirectoryName(p.Source)!,
            ".photosift-reborn-recovery",installation.ToString("N"),
            p.SessionId.ToString("N"),id.ToString("N"));
        return new DurableMoveItem(id,installation,p.SessionId,p.PhotoId,p.PlanId,p.Revision,
            DateTime.UtcNow.Ticks,p.Source,p.Destination,
            Path.Combine(folder,".photosift-move-"+id.ToString("N")+".part"),
            Path.Combine(operationRoot,Path.GetFileName(p.Source)),
            Path.Combine(operationRoot,"installed-"+Path.GetFileName(p.Destination)),
            p.OutputRoot,p.Hash,p.Identity,p.Length,p.WriteTicks,
            null,null,"Intent",null,p.Key);
    }

    private async Task ReviewFixtureSuccessorAsync()
    {
        if(_busy || !SuccessorNoMoveRows() || _work.AssignmentCount!=1 ||
            _sessionStore is null || _fixtureMover is null) return;
        try
        {
            _busy=true;
            var db=_sessionStore;var mover=_fixtureMover;
            P5CGuiFixturePolicy.VerifyExactWorkspace(db,_work);
            _work.RevokeApproval();
            if(_work.CollisionMode!=CollisionChoice.Refuse)
                throw new IOException("Successor Move requires Refuse collision policy.");
            var entries=_work.ReviewPlan();
            if(entries.Count!=1 || _work.SkippedReview.Count!=0 ||
                Path.GetFileName(entries[0].Source)!="photo_02.png" ||
                entries[0].Key!='F' ||
                !SortingWorkspace.PathComparer.Equals(entries[0].Destination,
                    Path.Combine(_work.DestinationRoot!,"Family","photo_02.png")))
                throw new IOException("Only exact photo_02.png → output/Family can enter the first successor epoch.");
            var id=_work.CurrentPlanId ?? throw new IOException("Missing exact plan ID.");
            var rev=_work.Revision;
            var prep=(await DurableCopyCoordinator.PrepareAsync(_work,entries,id,rev)).Single();
            var ticket=MakeGuiSuccessorTicket(prep,Guid.NewGuid());
            bool reviewed=await ShowExactPathsDialogAsync(entries,true,ticket.Id);
            if(!reviewed) {Message("Successor review canceled; no Move approved.");return;}
            var summary=db.GuiSuccessorPredecessorSummary();
            bool ack=await ShowBoundedNextMoveConfirmAsync(
                "MOVING ORIGINAL: "+ticket.Source+"\nTO: "+ticket.Destination+
                "\nOPERATION: "+ticket.Id+"\n"+summary+
                "\nOriginal recovery: "+ticket.OriginalArchive+
                "\nInstalled-output Undo archive: "+ticket.OutputArchive+
                "\nThis authorizes ONE generated-photo Move. The existing history remains immutable.");
            if(!ack) {Message("Explicit successor authorization canceled; nothing moved.");return;}
            if(!SuccessorNoMoveRows()) throw new IOException("Historical/session proof changed during review.");
            _work.ApprovePlan(id,rev);
            var staged=await Task.Run(()=>db.StageNextMoveEpochProposal(ticket));
            if(!staged.Verified) throw new IOException("Staged B2 ticket failed current native proof.");
            var prepared=await mover.PrepareApprovedStagedSuccessorAsync(_work,entries,ticket);
            mover.ApproveStagedSuccessor(_work,prepared,ticket,true);
            _approvedSuccessorTicket=ticket;
            _approvedSuccessorPreparation=prepared;
            Message("SINGLE-USE MOVE GRANTED. Exact photo_02 original, destination, history and archives bound in SQLite. Execute only once.");
        }
        catch(Exception ex)
        {
            _approvedSuccessorTicket=null;_approvedSuccessorPreparation=null;
            _work.RevokeApproval();
            Message("Successor review refused: "+ex.Message+". Do not reset a staged ticket; inspect recovery.");
        }
        finally {_busy=false;Refresh();}
    }

    private async Task ExecuteFixtureSuccessorAsync()
    {
        if(_busy || !SuccessorNoMoveRows() || _fixtureMover is null || _sessionStore is null ||
            _approvedSuccessorTicket is null || _approvedSuccessorPreparation is null ||
            !_work.IsCurrentPlanApproved) return;
        try
        {
            _busy=true;
            var ticket=_approvedSuccessorTicket;
            var prepared=_approvedSuccessorPreparation;
            P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);
            if(!_sessionStore.InspectMoveEpochGrant().Approved ||
                ticket.SessionId!=_work.SessionId || prepared.PhotoId!=ticket.PhotoId)
                throw new IOException("Approved successor grant is missing or changed.");
            Message("MOVING VERIFIED DISPOSABLE ORIGINAL: B3 grant and Move Intent consumed atomically; preserve recovery…");
            Refresh();
            // CRITICAL: never use the legacy ExecuteApprovedAsync / fresh-epoch bypass.
            var moved=await _fixtureMover.ExecuteApprovedStagedSuccessorAsync(_work,prepared,ticket);
            _work.RetireCommitted(new[]{moved.PhotoId});
            _approvedSuccessorTicket=null;_approvedSuccessorPreparation=null;
            _work.RevokeApproval();_hasReviewed=false;
            await SaveSessionAsync();
            _requestedPhotoId=null;
            Message("SECOND TEST FIXTURE MOVE COMMITTED. Original retained in owned recovery: "+
                moved.OriginalArchive+". Close/restart and Undo verified Move. No personal-photo Move.");
        }
        catch(Exception ex)
        {
            _approvedSuccessorTicket=null;_approvedSuccessorPreparation=null;
            _work.RevokeApproval();
            Message("SECOND MOVE UNCERTAIN / REFUSED. Do not retry. Preserve source, destination and archives; inspect journal. "+ex.Message);
        }
        finally {_busy=false;Refresh();}
    }

    private async Task<bool> ShowBoundedNextMoveConfirmAsync(string explanation)
    {
        var popup=new Window { Title="EXPLICIT RECOVERY / MOVE AUTHORIZATION — generated fixture only",
            Width=850,Height=455,MinWidth=650,MinHeight=360,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")),Foreground=Brushes.White };
        var panel=new StackPanel {Margin=new Thickness(22),Spacing=12};
        panel.Children.Add(new TextBlock {Text="DESTRUCTIVE OPERATION SAFETY CHECK",
            FontSize=21,FontWeight=FontWeight.Bold,Foreground=new SolidColorBrush(Color.Parse("#FFB38E"))});
        var body=new TextBox {Text=explanation,IsReadOnly=true,AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,MinHeight=175,MaxHeight=245};
        panel.Children.Add(body);
        var consent=new CheckBox {Content="I understand: generated photo fixture ONLY, exact reviewed path, retained recovery and no automatic retry."};
        panel.Children.Add(consent);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        var cancel=new Button {Content="Cancel — preserve everything",MinHeight=42};
        var confirm=new Button {Content="Authorize this one fixture step",MinHeight=42,IsEnabled=false};
        // Avalonia CheckBox has an IsChecked property, not WinForms-style
        // Checked / Unchecked CLR events. Subscribe to the styled property.
        // Null and false MUST leave the mutation consent button disabled.
        consent.PropertyChanged += (_, change) =>
        {
            if (change.Property == ToggleButton.IsCheckedProperty)
                confirm.IsEnabled = consent.IsChecked == true;
        };
        cancel.Click+=(_,_)=>popup.Close(false);
        confirm.Click+=(_,_)=>popup.Close(consent.IsChecked==true);
        actions.Children.Add(cancel);actions.Children.Add(confirm);
        panel.Children.Add(actions);popup.Content=panel;
        return await popup.ShowDialog<bool>(this);
    }

    private bool CanUndoFixtureMove()
    {
        if (!MoveModeAvailable || _fixtureMover is null || _sessionStore is null || !_anyMoveHistory ||
            !_storeHealthy || _copyBarrier) return false;
        try
        {
            var items=_sessionStore.MoveItems(_work.SessionId);
            return items.Count==1 && items[0].State=="Committed" &&
                _sessionStore.CopyItems(_work.SessionId).Count==0;
        }
        catch (Exception) { _storeHealthy=false; return false; }
    }

    private async Task UndoMoveAsync()
    {
        if (_busy || !CanUndoFixtureMove() || _fixtureMover is null || _sessionStore is null) return;
        _busy=true;
        try
        {
            if(_moveFixtureEnabled) P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);
            _modeGate.Revoke(); _hasReviewed=false; _prepared=null; _reviewedMoveId=null;
            Message("Verifying retained original and installed output before Move Undo. No overwrite allowed…"); Refresh();
            var result=await Task.Run(()=>_fixtureMover.UndoAsync(_work.SessionId));
            if (result.RouteKey is >= 'A' and <= 'Z' or >= '0' and <= '9')
                _work.RestoreUndone(result.PhotoId,result.RouteKey);
            await SaveSessionAsync();
            if(_boundedThirdGui)
            {
                // Undo is a real native file operation. Only AFTER success may
                // its immutable terminal receipt be appended to the ledger.
                // Failure here is uncertain completion, never permission to redo Undo.
                var sealedHistory=await Task.Run(()=>_sessionStore.SealMoveEpochLedger());
                if(!sealedHistory.Verified || sealedHistory.SealedRows!=3 || sealedHistory.MoveRows!=3)
                    throw new IOException("Third native Undo finished, but terminal ledger receipt is not verified. Stop; do NOT repeat Undo.");
            }
            // The source path exists again; force a new preview even though its photo ID and frozen metadata are unchanged.
            _requestedPhotoId = null;
            Message("MOVE UNDONE: original restored by native no-replace operation; installed output retained in owned recovery archive. Historical journal preserved.");
        }
        catch (Exception ex)
        {
            Message("Move Undo or terminal closeout REFUSED / UNCERTAIN. NO RETRY. Preserve files and inspect native journal: " + ex.Message);
        }
        finally
        {
            _anyMoveHistory = _sessionStore.AllMoveHistoryCount()!=0;
            _busy=false; Refresh();
        }
    }

    private async Task ReviewAsync()
    {
        if (_busy || !EnsureWritable()) return;
        _pendingRename = null;
        _hasReviewed = false;
        _prepared = null;
        _reviewedMoveId = null;
        _modeGate.Revoke();
        _busy = true;
        StudioReviewAttempt? attempt = null;
        try
        {
            bool forMove = _modeGate.Selected == ReviewOperationMode.Move;
            if (forMove)
            {
                if (_sessionStore is null || _fixtureMover is null || !MoveModeAvailable)
                    throw new IOException("Move requires an available verified native coordinator.");
                if (_moveFixtureEnabled) P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);
                else if (!OrdinaryFirstMovePolicy.CanOffer(_sessionStore,_work))
                    throw new IOException("Ordinary Move requires a fresh local flat-source native workspace without prior operation history.");
                if (_work.CollisionMode != CollisionChoice.Refuse)
                    throw new IOException("Move requires Collisions: Refuse. Change the policy and review again.");
            }
            _reviewHint.IsVisible = false;
            var entries = _work.ReviewPlan().ToArray();
            var skipped = _work.SkippedReview.ToArray();
            if (forMove && (entries.Length != 1 || skipped.Length != 0))
                throw new IOException("Move requires exactly one staged photograph and no skipped paths.");
            Guid? proposalId = forMove ? Guid.NewGuid() : null;
            long reviewedRevision = _work.Revision;
            Guid reviewedId = _work.CurrentPlanId ?? throw new IOException("Missing review identity.");
            Guid reviewedSession = _work.SessionId;
            CollisionChoice reviewedCollision = _work.CollisionMode;
            ReviewOperationMode reviewedMode = _modeGate.Selected;
            attempt = BeginStudioReview(reviewedId, reviewedRevision, reviewedMode, reviewedCollision);
            // Freeze the included *and* skipped sets before awaiting native proof.
            var display = CaptureStudioExactReview(entries,skipped,forMove,proposalId,
                reviewedId,reviewedRevision,reviewedSession,reviewedCollision);
            var initial = await Task.Run(() => DurableCopyCoordinator.PrepareAsync(
                _work,entries,reviewedId,reviewedRevision));
            RequireStudioReviewCurrent(attempt);
            if (initial.Count != entries.Length)
                throw new IOException("Native preparation did not cover every included reviewed path.");
            _prepared = initial;

            _plan.ItemsSource = entries.Select(p =>
                $"{Path.GetFileName(p.Source)}  →  {p.Key} · {Path.GetFileName(Path.GetDirectoryName(p.Destination))}/{Path.GetFileName(p.Destination)} (path preview · check approval above)")
                .Concat(skipped.Select(p=>"SKIPPED: "+p.OriginalDestination)).ToArray();
            Message($"Reviewing {entries.Length} exact output paths. No file operation has begun.");
            Refresh(preservePlan:true);
            RequireStudioReviewCurrent(attempt);
            bool approved = await ShowExactPathsDialogAsync(entries,forMove,proposalId,display);
            RequireStudioReviewCurrent(attempt);
            if (approved)
            {
                // The view supplies only a yes/no request. Fresh native proof
                // must match the original frozen preparation; never substitute
                // a silently replaced or newly numbered destination.
                var rechecked = await Task.Run(() => DurableCopyCoordinator.PrepareAsync(
                    _work,entries,reviewedId,reviewedRevision));
                RequireStudioReviewCurrent(attempt);
                if (rechecked.Count != initial.Count || !rechecked.SequenceEqual(initial))
                    throw new IOException("File identity or exact reviewed destination changed. Approve only after a new review.");
                _work.ApprovePlan(reviewedId, reviewedRevision);
                if (forMove)
                {
                    _prepared = new[] { await _fixtureMover!.PrepareApprovedAsync(_work,entries) };
                    RequireStudioReviewCurrent(attempt);
                    _reviewedMoveId = proposalId;
                }
                else _prepared = rechecked;
                _modeGate.RecordApproval();
            }
            else _work.RevokeApproval();
            _hasReviewed = _work.IsCurrentPlanApproved;
            if (!_hasReviewed) _prepared = null;
            Message(_hasReviewed
                ? (forMove
                    ? (_moveFixtureEnabled
                        ? "Approved exactly one generated TEST Move. Apply is a separate action."
                        : "Approved one ordinary Move. Apply separately relocates the original after native checks.")
                    : $"Approved {entries.Length} exact Copy paths. No files changed; Apply is separate and checks again.")
                : "Review cancelled without approval. No Apply permission was granted.");
        }
        catch (Exception ex)
        {
            _hasReviewed = false;
            _prepared = null;
            _work.RevokeApproval(); _modeGate.Revoke(); _reviewedMoveId = null;
            Message("Review refused: " + ex.Message);
            _reviewHint.Text = ex.Message.Contains("collision",StringComparison.OrdinalIgnoreCase)
                ? "A destination already exists. Change collision policy and review again. No operation was approved."
                : "Review cannot continue: " + ex.Message + " No operation was approved.";
            _reviewHint.IsVisible = true;
        }
        finally
        {
            if (attempt is not null) EndStudioReview(attempt);
            EndStudioEmbeddedReview();
            _busy = false;
            Refresh(preservePlan:true);
        }
    }

    private async Task<bool> ShowExactPathsDialogAsync(IReadOnlyList<SortPlanItem> entries,
        bool move=false, Guid? moveId=null, StudioExactReviewSnapshot? frozen=null)
    {
        var snapshot = frozen ?? CaptureStudioExactReview(entries,_work.SkippedReview.ToArray(),
            move,moveId,_work.CurrentPlanId,_work.Revision,_work.SessionId,_work.CollisionMode);
        // M4B: Copy AND the existing native Move use the same contained view
        // when the user is in Changes. The Move fallback modal preserves its
        // historical Focus-path behavior. Neither path is an Apply grant.
        if (!move || _studioActivePage == StudioPage.Changes)
            return await ShowStudioEmbeddedFamilyReviewAsync(snapshot,move && _moveFixtureEnabled);
        var dialog = new Window
        {
            Title = "Review exact output paths — PhotoSift Reborn",
            Width = 940, Height = 620, MinWidth = 680, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = StudioPalette.Shell,
            Foreground = StudioPalette.Text
        };
        var review = new StudioExactReviewView(snapshot,_moveFixtureEnabled);
        bool explicitlyApproved = false;
        review.ApproveRequested += (_,_) => { explicitlyApproved = true; dialog.Close(); };
        review.DeclineRequested += (_,_) => { explicitlyApproved = false; dialog.Close(); };
        dialog.KeyDown += (_,e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                explicitlyApproved = false;
                dialog.Close();
            }
        };
        // X/window closing is *always* declined unless the dedicated Approve
        // button handler set its private result. No default Enter approval.
        dialog.Content = review;
        dialog.Opened += (_,_) => review.FocusCancel();
        await dialog.ShowDialog(this);
        return explicitlyApproved;
    }

    private async Task ApplyAsync()
    {
        if (_busy || !_hasReviewed || !EnsureWritable()) return;
        try
        {
            _busy=true;
            if (_sessionStore is null || _journal is null || _prepared is null)
                throw new IOException("Missing approved proof, journal or session store.");
            var entries=_work.GetApprovedPlan();
            var prepared=_prepared.ToArray();
            if (prepared.Length != entries.Count ||
                prepared.Any(p => p.PlanId!=_work.CurrentPlanId || p.Revision!=_work.Revision || p.SessionId!=_work.SessionId))
                throw new IOException("The original reviewed/approved plan has changed.");
            var selectedMode=_modeGate.Selected;
            if (!_modeGate.CanApply(selectedMode,_work.IsCurrentPlanApproved,
                _anyMoveHistory,MoveModeAvailable))
                throw new IOException("Operation mode or approval changed. Review again.");
            if (_sessionStore.AllMoveHistoryCount()!=0 ||
                (_sessionStore.AllRenameHistoryCount()!=0 &&
                 !_sessionStore.IsRenameCloseoutVerified(_work.SessionId,_work)))
                throw new IOException("Move history or unclosed Rename history blocks new file operations.");
            if (selectedMode==ReviewOperationMode.Move && _sessionStore.AllRenameHistoryCount()!=0)
                throw new IOException("Move after Rename closeout is not yet authorized; only reviewed Copy may continue.");
            if (selectedMode==ReviewOperationMode.Move)
            {
                if (_fixtureMover is null || _reviewedMoveId is null || prepared.Length!=1)
                    throw new IOException("Move requires exactly one planned, approved operation.");
                if(_moveFixtureEnabled) P5CGuiFixturePolicy.VerifyExactWorkspace(_sessionStore,_work);
                else OrdinaryFirstMovePolicy.RequireExact(_sessionStore,_work,prepared[0]);
                Message(_moveFixtureEnabled
                    ? "Moving GENERATED FIXTURE photo: output proof, durable intent, original archive…"
                    : "Moving ONE VERIFIED ORIGINAL: durable Intent, atomic no-replace output, then owned source archive. NO RETRY after uncertainty."); Refresh();
                var moved=await Task.Run(()=>_fixtureMover.ExecuteApprovedAsync(_work,prepared[0],
                    reviewedOperationId:_reviewedMoveId));
                _anyMoveHistory=true;
                _work.RetireCommitted(new[] {moved.PhotoId});
                _hasReviewed=false; _prepared=null; _reviewedMoveId=null;
                _modeGate.Revoke(); _work.RevokeApproval();
                await SaveSessionAsync();
                // Invalidate a cached source preview after its original has been relocated.
                _requestedPhotoId = null;
                Message((_moveFixtureEnabled ? "TEST FIXTURE MOVED" : "ORDINARY MOVE COMMITTED")+
                    ": original retained at "+moved.OriginalArchive+
                    ". Close/restart, verify history and Undo. No automatic retry.");
                return;
            }
            _hasReviewed=false; _prepared=null; _reviewedMoveId=null;
            _modeGate.Revoke(); _work.RevokeApproval();
            Message("Copying with durable per-file intent and native no-replace publication…"); Refresh();
            // Explicit continuation permission is consumed by this executable
            // attempt: a future epoch requires fresh owner approval again.
            _copyContinuationApproved=false;
            var result=await Task.Run(()=>_journal.ApplyAsync(prepared));
            var freshlyReviewed=prepared.Select(x=>x.PhotoId).ToHashSet();
            var committed=_journal.CompletedItems(_work.SessionId);
            _work.RetireCommitted(committed.Where(x=>freshlyReviewed.Contains(x.PhotoId)).Select(x=>x.PhotoId));
            await SaveSessionAsync();
            Message(result.Detail is null
                ? $"Committed {result.Completed} verified copies; originals unchanged. Close/reopen to Undo safely."
                : $"PARTIAL: {result.Completed} completed. {result.NeedsAttention} need attention. {result.Detail}");
        }
        catch (Exception e) { Message("Operation refused: " + e.Message + ". Inspect recovery if any file operation began."); }
        finally
        {
            try { if (_sessionStore is not null) _anyMoveHistory=_sessionStore.AllMoveHistoryCount()!=0; }
            catch { _storeHealthy=false; }
            _busy=false; Refresh();
        }
    }
    private async Task UndoAppliedAsync()
    {
        if (_busy || !_storeHealthy || _copyBarrier || _journal is null || _anyMoveHistory) return;
        try
        {
            _busy=true;
            Message("Verifying original/output file identity and bytes before reversible Undo…"); Refresh();
            var previously=_journal.CompletedItems(_work.SessionId);
            var result=await Task.Run(()=>_journal.UndoAsync(_work.SessionId));
            var now=_journal.CompletedItems(_work.SessionId).Select(i=>i.Id).ToHashSet();
            foreach(var item in previously.Where(i=>!now.Contains(i.Id)))
                _work.RestoreUndone(item.PhotoId, item.Key);
            await SaveSessionAsync();
            Message(result.Detail is null
                ? $"Archived {result.Completed} verified copies into app recovery. Originals unchanged; destinations empty."
                : $"UNDO PARTIAL: {result.Completed} archived, remaining preserved. {result.Detail}");
        }
        catch (Exception ex) { Message("Undo refused: " + ex.Message); }
        finally { _busy=false; Refresh(); }
    }

    // Forensic inspection is read-only with respect to the filesystem. A
    // historical Undone operation is NOT proof that its archive still exists.
    private string RecoveryReport(IReadOnlyList<RecoveryCase> items, IReadOnlyList<MoveObservation>? moves=null,
        IReadOnlyList<RenameObservation>? renames=null, IReadOnlyList<DurableRejectItem>? rejects=null)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("PHOTOSIFT REBORN — FILE-OPERATION INSPECTION");
        report.AppendLine("States describe recorded history. EVIDENCE describes files observed NOW.");
        report.AppendLine("Rescan may update journal observations for unresolved operations, but does NOT change files.");
        report.AppendLine("No automatic deletion, overwrite, copy replay, Undo or manual journal reset is authorized.");
        if (_copyBarrier) report.AppendLine("BLOCKED: older P3A copy lacks individual ownership proof.");
        int archivesChecked=items.Count(x=>x.State=="Undone");
        int archivesVerified=items.Count(x=>x.Evidence=="ARCHIVE_VERIFIED");
        int archiveAlerts=archivesChecked-archivesVerified;
        report.AppendLine($"SESSION: {_work.SessionId}");
        report.AppendLine(StudioRecoveryInspectionPresentation.SessionHeading(
            items.Count,moves?.Count ?? 0,renames?.Count ?? 0,rejects?.Count ?? 0));
        if(items.Count>0)
        {
            report.AppendLine(StudioRecoveryInspectionPresentation.CopyEvidence(
                archivesChecked,archivesVerified,_journal?.NeedsAttention(_work.SessionId) ?? 0));
            if(archiveAlerts>0) report.AppendLine("WARNING: Some archived copies are missing, replaced, changed or have reappearing destinations. Do not infer current archive health from the historical Undone state.");
        }
        foreach (var r in items)
        {
            report.AppendLine(); report.AppendLine($"OPERATION {r.OperationId} — {r.State} (historical)");
            report.AppendLine("EVIDENCE NOW: " + r.Evidence);
            report.AppendLine("GUIDANCE: " + r.Guidance);
            if (!string.IsNullOrWhiteSpace(r.LastError)) report.AppendLine("LAST ERROR: " + r.LastError);
            report.AppendLine("SOURCE: " + r.Source);
            report.AppendLine("DESTINATION: " + r.Destination);
            report.AppendLine("TEMPORARY: " + r.Temporary);
            report.AppendLine("RECOVERY ARCHIVE: " + r.Recovery);
        }
        if (items.Count==0 && (moves?.Count ?? 0)==0 && (renames?.Count ?? 0)==0 && (rejects?.Count ?? 0)==0)
            report.AppendLine("No recorded operations in this session; foreign-session recovery may still block file actions.");
        report.AppendLine();
        report.AppendLine("Current evidence never authorizes restoring, deleting, cleaning up, replay or history reset.");
        if (moves is { Count: > 0 })
        {
            report.AppendLine(); report.AppendLine("MOVE OPERATIONS (read-only observed; NEVER automatically replayed)");
            foreach (var m in moves)
            {
                report.AppendLine($"MOVE {m.Id} — {m.State} (historical)");
                report.AppendLine("EVIDENCE NOW: "+m.Evidence);
                if (!string.IsNullOrWhiteSpace(m.Error)) report.AppendLine("ERROR: "+m.Error);
                report.AppendLine("SOURCE: "+m.Source);
                report.AppendLine("INSTALLED TARGET: "+m.Destination);
                report.AppendLine("ORIGINAL RECOVERY: "+m.OriginalArchive);
                report.AppendLine("UNDONE OUTPUT ARCHIVE: "+m.OutputArchive);
                report.AppendLine("GUIDANCE: "+StudioRecoveryInspectionPresentation.NextStep(StudioNativeFamily.Move,m.State));
            }
        }
        if (renames is {Count:>0})
        {
            report.AppendLine(); report.AppendLine("RENAME OPERATIONS (read-only evidence; no automatic replay)");
            foreach(var r in renames)
            {
                report.AppendLine($"RENAME {r.Id} — {r.State} (historical)");
                report.AppendLine("EVIDENCE NOW: "+r.Evidence);
                if(!string.IsNullOrWhiteSpace(r.Error)) report.AppendLine("ERROR: "+r.Error);
                report.AppendLine("ORIGINAL NAME: "+r.Source);
                report.AppendLine("RENAMED NAME: "+r.Target);
                report.AppendLine("GUIDANCE: "+StudioRecoveryInspectionPresentation.NextStep(StudioNativeFamily.Rename,r.State));
            }
        }
        report.AppendLine("Preserve all questionable files and the SQLite state directory.");
        return report.ToString();
    }

    private async Task ShowRecoveryAsync()
    {
        if (_journal is null || _sessionStore is null)
        { Message("The operation journal is unavailable. No changes made."); return; }
        var session=_work.SessionId;
        // Hashing archives can be expensive; never block the UI dispatcher on it.
        IReadOnlyList<RecoveryCase> first;
        IReadOnlyList<MoveObservation> firstMoves;
        IReadOnlyList<RenameObservation> firstRenames;
        IReadOnlyList<DurableRejectItem> firstRejects;
        try
        {
            first=await Task.Run(()=>_journal.Inspect(session));
            firstMoves=await Task.Run(()=>DurableMoveCoordinator.InspectRecorded(_sessionStore,session));
            firstRenames=await Task.Run(()=>DurableRenameCoordinator.Inspect(_sessionStore,session));
            firstRejects=await Task.Run(()=>_sessionStore.RejectItems().Where(x=>x.SessionId==session).ToArray());
        }
        catch(Exception ex) { Message("Cannot inspect journal: " + ex.Message); return; }
        // Refresh native evidence for read-only display, never replay a cut.
        if (_anyRejectHistory) InspectNativeRejectGuiEvidence();
        var detail = new TextBox
        {
            Text=RecoveryReport(first,firstMoves,firstRenames,firstRejects) + NativeRejectInspectionReport(),
            IsReadOnly=true, AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap, HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch,
            Background=new SolidColorBrush(Color.Parse("#101C28")),
            Foreground=Brushes.White, MinHeight=180
        };
        var dialog = new Window
        {
            Title="PhotoSift Reborn — Recovery inspection (read-only)",
            Width=890, Height=625, MinWidth=620, MinHeight=400,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#172839")),
            Foreground=Brushes.White
        };
        var layout=new Grid { RowDefinitions=new RowDefinitions("Auto,*,Auto,Auto"), Margin=new Thickness(18) };
        layout.Children.Add(new TextBlock
        {
            Text="Recovery inspection · historical state vs. current file evidence",
            Foreground=Brushes.White, FontSize=16, FontWeight=FontWeight.SemiBold,
            Margin=new Thickness(0,0,0,10)
        });
        Grid.SetRow(detail,1); layout.Children.Add(detail);
        var rescanStatus = new TextBlock
        {
            Text="Not rescanned in this window. The listing above is a current read-only inspection.",
            TextWrapping=TextWrapping.Wrap, Foreground=new SolidColorBrush(Color.Parse("#D3E5F8")),
            Margin=new Thickness(0,10,0,0)
        };
        Grid.SetRow(rescanStatus,2); layout.Children.Add(rescanStatus);
        var footer = new StackPanel { Orientation=Orientation.Horizontal, Spacing=10,
            HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };
        var rescan = new Button { Content="Rescan evidence (no file changes)", MinHeight=36 };
        rescan.Click += async (_,_) =>
        {
            rescan.IsEnabled=false;
            rescanStatus.Text="Rescanning recorded operations and archived file evidence…";
            try
            {
                if (_anyMoveHistory || _anyRejectHistory || RenameLocksWrites)
                    _journalSummary="Operation-history authority — read-only inspection; no Copy reconcile or file replay.";
                else
                    _journalSummary = await Task.Run(() => _journal.Reconcile(session));
                // An observed installed operation retires an old staged choice
                // before the user can start another Apply.
                if (!_anyMoveHistory && !_anyRejectHistory && !RenameLocksWrites)
                {
                    var installed=_journal.CompletedItems(session);
                    var assignedInstalled=installed
                        .Where(item => _work.Photos.Any(photo => photo.Id==item.PhotoId &&
                            _work.CategoryFor(photo.Path) is not null))
                        .Select(item => item.PhotoId).ToArray();
                    if (assignedInstalled.Length>0)
                    {
                        _work.RetireCommitted(assignedInstalled);
                        await SaveSessionAsync();
                    }
                }
                var observed=await Task.Run(()=>_journal.Inspect(session));
                var observedMoves=await Task.Run(()=>DurableMoveCoordinator.InspectRecorded(_sessionStore,session));
                var observedRenames=await Task.Run(()=>DurableRenameCoordinator.Inspect(_sessionStore,session));
                var observedRejects=await Task.Run(()=>_sessionStore.RejectItems().Where(x=>x.SessionId==session).ToArray());
                InspectNativeRejectGuiEvidence();
                detail.Text=RecoveryReport(observed,observedMoves,observedRenames,observedRejects) + NativeRejectInspectionReport();
                var checkedCount=observed.Count(x=>x.State=="Undone");
                var verifiedCount=observed.Count(x=>x.Evidence=="ARCHIVE_VERIFIED");
                var warnings=checkedCount-verifiedCount;
                rescanStatus.Text=$"Rescan completed at {DateTimeOffset.Now:HH:mm:ss}: {observed.Count} Copy / {observedMoves.Count} Move / {observedRenames.Count} Rename / {observedRejects.Length} Reject historical operation(s); " +
                    (observed.Count>0 ? $"Copy archive evidence {verifiedCount}/{checkedCount} verified, {warnings} Copy alert(s). " : "") +
                    "This is not a cross-family safety clearance or approval.";
                Message("Journal rescan completed. "+_journalSummary);
                Refresh();
            }
            catch(Exception ex)
            {
                rescanStatus.Text="Rescan FAILED: "+ex.Message+". No automatic file changes.";
                Message("Journal inspection failed. Files were not changed: "+ex.Message);
            }
            finally { rescan.IsEnabled=true; }
        };
        footer.Children.Add(rescan);
        footer.Children.Add(Button("Close inspection", () => dialog.Close()));
        Grid.SetRow(footer,3);layout.Children.Add(footer);
        dialog.Content=layout;
        await dialog.ShowDialog(this);
    }

    // This is a presentation action only; it cannot bypass Closing, saved
    // decision authority, or the busy/review safety gates.
    private void FitStudioNormalWindowToWorkArea()
    {
        if (WindowState != WindowState.Normal) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var work = screen.WorkingArea;
        var placement = StudioWindowPlacementPolicy.CenterInWorkArea(
            work.X, work.Y, work.Width, work.Height, screen.Scaling,
            Width, Height, MinWidth, MinHeight);
        Width=placement.WidthDip;
        Height=placement.HeightDip;
        Position=new PixelPoint(placement.XPixel,placement.YPixel);
    }

    private void ToggleStudioWindowMaximize()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState=WindowState.Normal;
            _fullScreen.Content="Full screen (F11)";
            FitStudioNormalWindowToWorkArea();
            return;
        }
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;
        if (WindowState == WindowState.Normal) FitStudioNormalWindowToWorkArea();
    }

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        _fullScreen.Content=WindowState == WindowState.FullScreen ? "Exit full (F11)" : "Full screen (F11)";
    }

    // P4A: photo inspection gains inspect-scale zoom, pan by scrolling and
    // view-only quarter-turn rotations. The source file is never transformed.
    // The main viewer owns its cached preview; original pixels are decoded only
    // on explicit request through the same single native decode worker.
    private async Task ShowPhotoInspectionAsync()
    {
        var photo=_work.Current;
        var previewBitmap=_currentBitmap;
        if (photo is null || previewBitmap is null || !_displayReady || _displayedPhotoId!=photo.Id)
        { Message("Wait until a photograph is visible before inspecting it."); return; }
        var dialog=new Window
        {
            Title="Inspect photo — "+Path.GetFileName(photo.Path),
            Width=980, Height=640, MinWidth=690, MinHeight=440,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.Parse("#101C28")), Foreground=Brushes.White
        };
        var panel=new Grid { RowDefinitions=new RowDefinitions("Auto,*,Auto"), Margin=new Thickness(13) };
        var header=new StackPanel { Spacing=4, Margin=new Thickness(0,0,0,8) };
        header.Children.Add(new TextBlock { Text=Path.GetFileName(photo.Path), FontSize=21, FontWeight=FontWeight.SemiBold });
        var dimensions=Muted($"Preview: {previewBitmap.PixelSize.Width} × {previewBitmap.PixelSize.Height} px · {photo.Length:N0} source bytes · unedited original");
        header.Children.Add(dimensions);
        // P6G1: header-only camera/EXIF metadata. No GPS coordinates and no
        // decoder delegates; the imported original's native identity is checked.
        var technical=Muted("Camera/date/orientation: reading bounded original header…");
        technical.TextWrapping=TextWrapping.Wrap;
        header.Children.Add(technical);
        var pathBox=new TextBox { Text=photo.Path, IsReadOnly=true,
            Foreground=Brushes.White, Background=new SolidColorBrush(Color.Parse("#101C28")) };
        header.Children.Add(pathBox);
        panel.Children.Add(header);

        using var cancellation=new CancellationTokenSource();
        Bitmap? originalBitmap=null;
        Bitmap shown=previewBitmap;
        bool usingOriginal=false;
        bool closed=false;
        int quarterTurns=0;
        double zoom=1;
        bool fit=true;
        var fitViewer=new Viewbox { Stretch=Stretch.Uniform,
            HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch };
        var scrollViewer=new ScrollViewer { IsVisible=false,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        var surface=new Grid { Background=new SolidColorBrush(Color.Parse("#1A2939")),
            Margin=new Thickness(0,2) };
        surface.Children.Add(fitViewer); surface.Children.Add(scrollViewer);
        Grid.SetRow(surface,1); panel.Children.Add(surface);

        var caption=Muted("Fit · original image unchanged");
        var controls=new WrapPanel { Orientation=Orientation.Horizontal };
        Canvas MakeCanvas(double scale)
        {
            var g=InspectionGeometry.Calculate(shown.PixelSize.Width,shown.PixelSize.Height,quarterTurns,scale);
            var transforms=new TransformGroup();
            transforms.Children.Add(new RotateTransform(g.RotationDegrees));
            transforms.Children.Add(new TranslateTransform(g.OffsetX,g.OffsetY));
            var view=new Image { Source=shown, Width=g.ImageWidth, Height=g.ImageHeight,
                Stretch=Stretch.Fill,
                RenderTransformOrigin=new RelativePoint(0,0,RelativeUnit.Absolute),
                RenderTransform=transforms };
            var canvas=new Canvas { Width=g.CanvasWidth, Height=g.CanvasHeight, ClipToBounds=true };
            canvas.Children.Add(view);
            return canvas;
        }
        void RefreshInspection()
        {
            fitViewer.IsVisible=fit; scrollViewer.IsVisible=!fit;
            if (fit) { scrollViewer.Content=null; fitViewer.Child=MakeCanvas(1); }
            else { fitViewer.Child=null; scrollViewer.Content=MakeCanvas(zoom); }
            var mode=usingOriginal ? "ORIGINAL pixels" : "preview pixels (2048 cap)";
            caption.Text=fit
                ? $"Fit · {mode} · rotated {quarterTurns*90}°"
                : $"{mode} · zoom {zoom*100:0}% · rotated {quarterTurns*90}° · scroll to pan";
        }
        void AddControl(string label,Action click)
        {
            var button=Button(label,click); button.Margin=new Thickness(0,0,7,5); controls.Children.Add(button);
        }
        AddControl("Fit",()=>{fit=true;RefreshInspection();});
        AddControl("Preview 100%",()=>{shown=previewBitmap;usingOriginal=false;fit=false;zoom=1;RefreshInspection();});
        Button actual=null!;
        actual=Button("Original 1:1 (load)",(Func<Task>)(async ()=>
        {
            if (closed) return;
            actual.IsEnabled=false;
            caption.Text="Loading original pixels through bounded single decoder…";
            try
            {
                if (originalBitmap is null)
                {
                    var req=new PreviewWorker.Request(photo.Id,photo.Path,photo.Length,photo.LastWriteUtcTicks,_viewGeneration);
                    var encoded=await _previewWorker.DecodeOriginalAsync(req,cancellation.Token);
                    if (closed || cancellation.IsCancellationRequested) return;
                    using var stream=new MemoryStream(encoded,writable:false);
                    originalBitmap=new Bitmap(stream);
                }
                if (closed) return;
                shown=originalBitmap; usingOriginal=true; fit=false; zoom=1;
                RefreshInspection();
                dimensions.Text=$"Original oriented pixels: {shown.PixelSize.Width} × {shown.PixelSize.Height} · {photo.Length:N0} source bytes; display only";
            }
            catch(OperationCanceledException) { if (!closed) caption.Text="Original load cancelled; Fit preview remains available."; }
            catch(Exception ex) { if (!closed) caption.Text="Original 1:1 unavailable: "+ex.Message+" (Fit preview still works)."; }
            finally { if (!closed) actual.IsEnabled=true; }
        }));
        actual.Margin=new Thickness(0,0,7,5); controls.Children.Add(actual);
        AddControl("−",()=>{fit=false;zoom=Math.Max(0.25,zoom/2);RefreshInspection();});
        AddControl("+",()=>{fit=false;zoom=Math.Min(4,zoom*2);RefreshInspection();});
        AddControl("↶ Rotate",()=>{quarterTurns=(quarterTurns+3)%4;RefreshInspection();});
        AddControl("Rotate ↷",()=>{quarterTurns=(quarterTurns+1)%4;RefreshInspection();});
        AddControl("Reset",()=>{quarterTurns=0;zoom=1;fit=true;shown=previewBitmap;usingOriginal=false;RefreshInspection();});
        AddControl("Close",()=>dialog.Close());
        var foot=new StackPanel { Spacing=5, Margin=new Thickness(0,7,0,0) };
        foot.Children.Add(caption); foot.Children.Add(controls);
        Grid.SetRow(foot,2);panel.Children.Add(foot);
        dialog.Content=panel;
        dialog.Closed+=(_,_)=> { closed=true; cancellation.Cancel();
            fitViewer.Child=null;scrollViewer.Content=null;
            originalBitmap?.Dispose(); originalBitmap=null; };
        dialog.Opened+=async (_,_)=>
        {
            try
            {
                var info=await Task.Run(()=>PhotoTechnicalMetadataReader.Inspect(photo));
                if(!closed) technical.Text=info.Description;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
                NotSupportedException or ArgumentException)
            {
                if(!closed)technical.Text="Metadata unavailable: "+ex.Message+
                    " Source was not modified.";
            }
        };
        RefreshInspection();
        await dialog.ShowDialog(this);
    }

    private void Refresh(bool preservePlan = false)
    {
        _sourceLabel.Text = StudioReadablePathPresentation.Summary("Source",_work.SourceRoot,"choose a folder");
        _outputLabel.Text = StudioReadablePathPresentation.Summary("Results",_work.DestinationRoot,"choose another folder");
        ToolTip.SetTip(_sourceLabel,_work.SourceRoot ?? "No source selected");
        ToolTip.SetTip(_outputLabel,_work.DestinationRoot ?? "No results selected");
        _studioExactLocations.Text = StudioReadablePathPresentation.Exact(_work.SourceRoot,_work.DestinationRoot);
        _routeList.Text = string.Join(Environment.NewLine, _work.Routes.Select(r =>
        {
            string expanded;
            try { expanded=_work.ExpandRouteDirectory(r.Key); }
            catch (IOException) { expanded="choose output folder first"; }
            return StudioReadablePathPresentation.Route(r.Key,r.Kind.ToString(),r.Label,expanded);
        }));
        // Full expanded route paths are accessible without being used as
        // execution or approval authority by this display-only lane.
        ToolTip.SetTip(_routeList,string.Join(Environment.NewLine,
            _work.Routes.Select(r => {
                try { return $"{r.Key} · {r.Label}: {_work.ExpandRouteDirectory(r.Key)}"; }
                catch(IOException) { return $"{r.Key}: output folder needed"; }
            })));
        _routePreview.Text = "Destination settings: select Default, Relative or Absolute. Absolute routes are chosen using the native picker; all expanded paths appear above and again in Review.";
        _quickRoutes.Children.Clear();
        foreach (var route in _work.Routes.Take(4))
        {
            char key = route.Key;
            var routeButton = Button($"{key} · {(route.Kind == RouteKind.Absolute ? Path.GetFileName(route.Location?.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)) : route.Label)}", (Func<Task>)(async () => await AssignAsync(key)));
            routeButton.Margin = new Thickness(0,0,7,7);
            routeButton.Classes.Add("studio-nav");
            _quickRoutes.Children.Add(routeButton);
        }
        int journalApplied = _journal?.Committed(_work.SessionId) ?? _work.Applied.Count;
        RefreshGuidedWorkspace(journalApplied);
        var editAllowed=!_busy && _storeHealthy && !_copyBarrier && !JournalNeedsAttention && !_anyMoveHistory && !_anyRejectHistory && !RenameLocksWrites &&
            (_sessionStore is null || _sessionStore.AllCopyHistoryCount()==0 || _copyContinuationApproved);
        _modeStatus.Text = _boundedThirdGui
            ? "THIRD MOVE: GENERATED FIXTURE photo_03 ONLY. Original first/second history retained. Dedicated third-Move actions only."
            : _boundedSuccessorGui
            ? "SECOND MOVE: GENERATED FIXTURE photo_02 ONLY. Existing journal retained. Only dedicated successor actions are authorized."
            : _modeGate.Selected==ReviewOperationMode.Move
            ? (_moveFixtureEnabled ? "MOVE — generated disposable fixture only." :
                "MOVE — single ordinary local first epoch. Original relocated after durable review; restart-safe Undo. No automatic retry.")
            : StudioChangesContextPresentation.Explain(_storeHealthy,
                _modeGate.Selected==ReviewOperationMode.Move,
                _anyMoveHistory, RenameLocksWrites, _anyRejectHistory,
                _copyBarrier, JournalNeedsAttention,
                _sessionStore is not null && _sessionStore.AllCopyHistoryCount()!=0 && !_copyContinuationApproved);
        _chooseCopy.IsEnabled=editAllowed;
        _chooseMove.IsEnabled=editAllowed && MoveModeAvailable && !_anyRenameHistory;
        // Selected state and tooltips carry the full explanation. Keep captions
        // short enough for two equal columns at 150–175% Windows scaling.
        _chooseCopy.Content=_modeGate.Selected==ReviewOperationMode.Copy ? "● Copy" : "Copy";
        _chooseMove.Content=_modeGate.Selected==ReviewOperationMode.Move
            ? (_moveFixtureEnabled ? "● Move (test)" : "● Move")
            : (_moveFixtureEnabled ? "Move (test)" : "Move");
        _apply.Content=_modeGate.Selected==ReviewOperationMode.Move
            ? (_moveFixtureEnabled ? "Apply test Move" : "APPLY approved Move") : "Apply copies";
        _review.Content=_modeGate.Selected==ReviewOperationMode.Move ? "Review Move" : "Review paths";
        _undoMove.IsEnabled=!_busy && CanUndoFixtureMove();
        _undoMove.Content=_moveFixtureEnabled ? "Undo verified Move (fixture)" : "Undo verified Move";
        bool successorReady=false;
        try { successorReady=_boundedSuccessorGui && _sessionStore is not null &&
            _sessionStore.InspectGuiSuccessor(_work.CaptureSession()).Verified &&
            _sessionStore.MoveItems(_work.SessionId).Count==0; }
        catch(Exception) { successorReady=false; }
        bool canBegin=false;
        try { canBegin=!_boundedSuccessorGui && _moveFixtureEnabled && _anyMoveHistory &&
            _sessionStore is not null && _sessionStore.AllMoveHistoryCount()==1 &&
            _sessionStore.MoveItems(_work.SessionId) is {Count:1} rows && rows[0].State=="Undone"; }
        catch(Exception) { canBegin=false; }
        _beginSuccessorGui.IsVisible=_moveFixtureEnabled && _anyMoveHistory && !_boundedSuccessorGui && !_boundedThirdGui;
        _beginSuccessorGui.IsEnabled=!_busy && _storeHealthy && !_copyBarrier && canBegin;
        _stageSuccessorGui.IsVisible=_boundedSuccessorGui;
        _reviewSuccessorGui.IsVisible=_boundedSuccessorGui;
        _executeSuccessorGui.IsVisible=_boundedSuccessorGui;
        _stageSuccessorGui.IsEnabled=!_busy && _storeHealthy && successorReady &&
            _work.AssignmentCount==0 && _work.Current is { } nextPhoto &&
            Path.GetFileName(nextPhoto.Path)=="photo_02.png";
        _reviewSuccessorGui.IsEnabled=!_busy && _storeHealthy && successorReady &&
            _work.AssignmentCount==1 && _approvedSuccessorTicket is null;
        _executeSuccessorGui.IsEnabled=!_busy && _storeHealthy && successorReady &&
            _approvedSuccessorTicket is not null && _approvedSuccessorPreparation is not null &&
            _work.IsCurrentPlanApproved;
        bool thirdReady=ThirdReadyNoMoveRows();
        bool canBeginThird=false;
        try { canBeginThird=_boundedSuccessorGui && !_boundedThirdGui && _moveFixtureEnabled &&
            _sessionStore is not null && _sessionStore.AllMoveHistoryCount()==2 &&
            _sessionStore.MoveItems(_work.SessionId) is {Count:1} predecessor &&
            predecessor[0].State=="Undone" &&
            _sessionStore.InspectGuiSuccessor(_work.CaptureSession()).Verified; }
        catch(Exception) {canBeginThird=false;}
        _beginThirdGui.IsVisible=canBeginThird;
        _beginThirdGui.IsEnabled=!_busy && _storeHealthy && !_copyBarrier && canBeginThird;
        _stageThirdGui.IsVisible=_boundedThirdGui;
        _reviewThirdGui.IsVisible=_boundedThirdGui;
        _executeThirdGui.IsVisible=_boundedThirdGui;
        _stageThirdGui.IsEnabled=!_busy && thirdReady && _work.AssignmentCount==0 &&
            _work.Current is { } thirdPhoto && Path.GetFileName(thirdPhoto.Path)=="photo_03.png";
        _reviewThirdGui.IsEnabled=!_busy && thirdReady && _work.AssignmentCount==1 &&
            _approvedThirdTicket is null;
        _executeThirdGui.IsEnabled=!_busy && thirdReady &&
            _approvedThirdTicket is not null && _approvedThirdPreparation is not null &&
            _work.IsCurrentPlanApproved;
        _auditRename.IsEnabled=!_busy && _storeHealthy && _sessionStore is not null && _work.Current is not null;
        _ordinaryOperationsSafety.IsEnabled=!_busy && _storeHealthy && _sessionStore is not null && _work.Current is not null;
        _renameFixture.Content=_ordinaryFirstRenamer is not null ? "Rename photo (one local operation)" :
            _windowsRenameTrial is null?"F2 Rename (fixture only)":"F2 Rename (local NTFS trial)";
        _applyRename.Content=_ordinaryFirstRenamer is not null ? "APPLY approved Rename" :
            _windowsRenameTrial is null?"Apply TEST Rename":"Apply LOCAL Rename trial";
        _undoRename.Content=_ordinaryFirstRenamer is not null ? "Undo verified Rename" :
            _windowsRenameTrial is null?"Undo verified Rename (fixture)":"Undo verified Rename (local NTFS)";
        _renameFixture.IsEnabled=CanStartNextFixtureRename();
        _applyRename.IsEnabled=CanStartNextFixtureRename() && _pendingRename is not null && _work.Current?.Id==_pendingRename.PhotoId &&
            _work.Revision==_pendingRename.Revision;
        _undoRename.IsEnabled=!_busy && CanUndoFixtureRename();
        _closeoutRename.IsEnabled=CanCloseoutRename();
        _closeoutRename.IsVisible=_anyRenameHistory && !_renameCloseoutVerified;
        _resumeVerifiedCopy.IsVisible=_sessionStore is not null &&
            _sessionStore.AllCopyHistoryCount()!=0 && !_copyContinuationApproved;
        _resumeVerifiedCopy.IsEnabled=!_busy && _storeHealthy && !_copyBarrier &&
            !_anyRejectHistory && !_anyMoveHistory && !_anyRenameHistory &&
            !JournalNeedsAttention && _journal is not null &&
            _journal.Committed(_work.SessionId)==0;

        _collisionMode.Content=$"Collisions: {_work.CollisionMode} (click to change)";
        _collisionMode.IsEnabled=editAllowed;
        var now = _work.Current;
        // The common review dashboard is usable with ordinary user photos.
        // Fixture-only recovery controls are visually secondary, but MUST be
        // expanded when native Reject history needs the owner's attention.
        if (_anyRejectHistory && (_moveFixtureEnabled || OrdinaryRejectGuiProbePolicy.IsRequested) && !_fixtureRecoveryExpander.IsExpanded)
            _fixtureRecoveryExpander.IsExpanded = true;
        try
        {
            var overview = CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            _decisionHeadline.Text = overview.Headline;
            _decisionCenter.Content = $"Review collection ({overview.Rows.Count})…";
        }
        catch (Exception ex)
        {
            _decisionHeadline.Text = "Collection decisions unavailable: " + ex.Message;
        }
        _decisionCenter.IsEnabled = !_busy && _storeHealthy && _work.Photos.Count > 0;
        _studioCollection.IsEnabled = _decisionCenter.IsEnabled;
        _studioChanges.IsEnabled = !_busy && _storeHealthy && _work.Photos.Count > 0;
        _studioHistory.IsEnabled = true; // Read-only status remains reachable even on store failure.
        _studioCollection.IsEnabled = !_busy && _storeHealthy;
        _studioChanges.IsEnabled = !_busy && _storeHealthy;
        RefreshStudioCollection();
        _studioAdd.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyMoveHistory &&
            !_anyRejectHistory && !RenameLocksWrites && !JournalNeedsAttention &&
            (_sessionStore is null || _sessionStore.AllCopyHistoryCount() == 0 || _copyContinuationApproved);
        if (_studioChooseOutput is not null)
        {
            _studioChooseOutput.IsEnabled = _studioAdd.IsEnabled;
            ToolTip.SetTip(_studioChooseOutput, _work.DestinationRoot is null
                ? "Choose a separate results folder. This saves the destination only."
                : "Current results folder: " + _work.DestinationRoot + ". Choose another only when history permits.");
        }
        _studioResultsHint.Text = StudioImportGuidance.Describe(_work.Photos.Count,
            !string.IsNullOrWhiteSpace(_work.DestinationRoot),
            _copyBarrier || _anyMoveHistory || _anyRejectHistory || RenameLocksWrites || JournalNeedsAttention);
        _rejectOverview.Text = _anyRejectHistory
            ? "Native Reject recovery history retained · normal sorting locked · use the exact-path recovery controls"
            : $"Reject review: {_work.PendingRejectCount} marked · no files moved · undoable after restart";
        UpdateNativeRejectGuiButtons();
        UpdateOrdinaryRejectGuiProbeButtons();
        UpdateOrdinaryFirstRejectButtons();
        _markReject.IsEnabled = CanStageRejectReview() && _work.CanMarkCurrentForReject;
        _undoReject.IsEnabled = CanStageRejectReview() && _work.CanUndoPendingReject;
        _reviewRejectQueue.IsEnabled = CanStageRejectReview() && _work.PendingRejectCount>0;
        _reviewRejectQueue.Content = $"Review marked photos ({_work.PendingRejectCount})…";
        _progress.Text = $"{_work.AssignmentCount} staged · {_work.PendingRejectCount} marked for Reject review · {journalApplied} applied · {_work.Photos.Count} imported / {_work.SourceRoots.Count} source root(s)" +
            (_copyBarrier || JournalNeedsAttention || _anyMoveHistory || _anyRejectHistory || RenameLocksWrites ? " · FILE-OPERATION HISTORY LOCK" :
             _renameCloseoutVerified ? " · RENAME CLOSEOUT VERIFIED · COPY ONLY" : "");
        // Refresh the read-only historical projection after Commit, Undo,
        // Restart and explicit closeout. Never infer CURRENT filesystem proof
        // from the recorded journal state.
        if (_studioActivePage is StudioPage.Changes or StudioPage.History)
            RefreshStudioRecordedCopyHistory();
        _photoLabel.Text = now is null ? (_work.AtEnd
            ? $"Finished reviewing {_work.Photos.Count} photos · {_work.AssignmentCount} staged. Review paths or go back to correct a choice."
            : "Open or add JPEG, PNG, BMP or GIF photos; drop local files/folders here.") :
            $"{_work.Index + 1} / {_work.Photos.Count}  ·  {Path.GetFileName(now.Path)}  ·  Route: {(_anyRejectHistory ? "NATIVE REJECT HISTORY (see recovery status)" : _work.IsPendingReject(now.Id) ? "REJECT REVIEW (no file moved)" : _work.CategoryFor(now.Path)?.ToString() ?? "unassigned")}";
        if (!preservePlan)
            _plan.ItemsSource = _work.Photos.Where(p => _work.CategoryFor(p.Path) != null)
                .Select(p => $"{Path.GetFileName(p.Path)}  →  {_work.CategoryFor(p.Path)} (staged)").ToArray();
        // Empty state is purely visual. Durable approvals, saved choices,
        // pending Reject and unreadable history retain their original owners.
        var showPlanEmpty = !preservePlan && _work.AssignmentCount==0;
        _plan.IsVisible = !showPlanEmpty;
        if (_studioChangesEmptyPanel is not null)
            _studioChangesEmptyPanel.IsVisible=showPlanEmpty;
        _prev.IsEnabled = !_busy && EnsureViewerNavigationAllowed() && _work.Photos.Count > 0 && _work.Index > 0;
        _next.IsEnabled = !_busy && EnsureViewerNavigationAllowed() && _work.Photos.Count > 0 && _work.Index < _work.Photos.Count - 1;
        _undoStage.IsEnabled = editAllowed && _work.CanUndoDecision;
        _jump.IsEnabled = !_busy && EnsureViewerNavigationAllowed() && _work.Photos.Count>0;
        _nextUnassigned.IsEnabled = _jump.IsEnabled;
        _shuffle.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyRejectHistory && !JournalNeedsAttention && !_anyMoveHistory && !RenameLocksWrites && _work.Photos.Count>1;
        _removeSession.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyRejectHistory && !JournalNeedsAttention && !_anyMoveHistory && !RenameLocksWrites && now is not null;
        _undoRemove.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyRejectHistory && !JournalNeedsAttention && !_anyMoveHistory && !RenameLocksWrites && _work.CanUndoRemoval;
        _review.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyRejectHistory && !JournalNeedsAttention && !_anyMoveHistory && !RenameLocksWrites && editAllowed && _work.AssignmentCount > 0;
        _apply.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyRejectHistory && !JournalNeedsAttention && !_anyMoveHistory && !RenameLocksWrites && editAllowed && _prepared is not null &&
            _modeGate.CanApply(_modeGate.Selected,_work.IsCurrentPlanApproved,_anyMoveHistory,MoveModeAvailable);
        _undoApplied.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !_anyRejectHistory && !JournalNeedsAttention && !_anyMoveHistory && !RenameLocksWrites && journalApplied>0;
        _inspectRecovery.IsEnabled = !_busy && _storeHealthy && _journal is not null;
        _inspectPhoto.IsEnabled = !_busy && _displayReady && _work.Current?.Id==_displayedPhotoId;
        UpdateViewerState(now);
        if (_requestedPhotoId != now?.Id || _requestedLength != now?.Length || _requestedTicks != now?.LastWriteUtcTicks)
            RequestPreview(now);
    }

    private void UpdateViewerState(Photo? current)
    {
        bool complete = _work.AtEnd;
        bool empty = _work.Photos.Count == 0;
        bool loading = current is not null && (!_displayReady || _displayedPhotoId != current.Id);
        _viewerMessage.IsVisible = empty || complete || loading;
        _photo.IsVisible = !empty && !complete && !loading;
        _viewerBack.IsVisible = complete;
        _viewerReview.IsVisible = complete && _work.AssignmentCount > 0;
        _viewerReview.IsEnabled = !_busy && _storeHealthy && !_copyBarrier && !JournalNeedsAttention && !_anyMoveHistory;
        _viewerBack.IsEnabled = !_busy && _storeHealthy && !_copyBarrier;
        if (complete)
        {
            _viewerHeading.Text = "Collection reviewed";
            _viewerDescription.Text = $"{_work.Photos.Count} photos seen · {_work.AssignmentCount} assigned · {_work.PendingRejectCount} marked for Reject review. " +
                (_anyRejectHistory ? "Native Reject history is separately journalled. See recovery controls; ordinary sorting stays locked."
                    : "Only assigned photos enter Copy Review; review-only Reject marks never move files. Go back to change choices.");
        }
        else if (empty)
        {
            _viewerHeading.Text = "Your photos, your folders";
            _viewerDescription.Text = "Open/add photo files or folders (optionally recursive), or drop local paths; choose an output directory, then sort.";
        }
        else if (loading && _previewFailure.TryGet(current!.Id, _viewGeneration, out var failureHeading, out var failureDetail))
        {
            // A known missing/changed/undecodable file is a terminal state, not an in-progress decode.
            // Refresh can run for unrelated UI events without erasing the explanation.
            _viewerHeading.Text = failureHeading;
            _viewerDescription.Text = failureDetail;
        }
        else
        {
            _viewerHeading.Text = "Loading photograph…";
            _viewerDescription.Text = $"{_work.Index + 1} of {_work.Photos.Count} · {Path.GetFileName(current!.Path)}";
        }
    }

    // Historical journal state only informs the viewer; it is NOT proof of current recovery ownership
    // and cannot authorize a file operation. The recovery inspector and Undo coordinator verify that.
    private bool HasRecordedCommittedMove(Photo photo)
    {
        if (!_anyMoveHistory || _sessionStore is null) return false;
        try
        {
            return _sessionStore.MoveItems(_work.SessionId).Any(item =>
                item.State == "Committed" && item.PhotoId == photo.Id &&
                SortingWorkspace.PathComparer.Equals(item.Source, photo.Path));
        }
        catch (Exception ex)
        {
            _storeHealthy = false;
            Message("Move history could not be verified for preview. Mutations locked: " + ex.Message);
            return false;
        }
    }

    // P6F1: read-only recovery-aware preview. An absent imported source is
    // NEVER itself evidence that the file is safely archived. Re-inspect the
    // *latest* owned native Reject operation and correlate its IDs and paths
    // before using reassuring recovery language. No mutations or approvals.
    private bool HasVerifiedCommittedRejectPreview(Photo photo)
    {
        if (!_anyRejectHistory || _sessionStore is null) return false;
        RejectRecoveryObservation? verified;
        if (_ordinaryRejectProbe is not null)
        {
            InspectOrdinaryRejectGuiProbe(); // Fresh owned-archive proof, no mutation.
            verified=_ordinaryRejectProbeObservation;
        }
        else
        {
            if (_nativeReject is null) return false;
            InspectNativeRejectGuiEvidence(); // Fresh native/owned-marker check.
            verified=_nativeRejectObservation;
        }
        if (verified is not {State:"Committed",Evidence:"OWNED_ARCHIVE_VERIFIED"}) return false;
        try
        {
            var rows = _sessionStore.RejectItems();
            return rows.Count > 0 && RecoveryPreviewPresentation.VerifiedCommittedRejectForPhoto(
                photo, _work.SessionId, rows[^1], verified);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _storeHealthy = false;
            Message("Reject history could not be read for preview. Mutations locked: " + ex.Message);
            return false;
        }
    }

    private void ShowPreviewFailure(Photo photo, long generation, string heading, string detail)
    {
        _previewFailure.Record(photo.Id, generation, heading, detail);
        UpdateViewerState(photo);
    }

    private void RequestPreview(Photo? photo)
    {
        long generation = ++_viewGeneration;
        _previewFailure.Clear();
        _requestedPhotoId = photo?.Id;
        _requestedLength = photo?.Length ?? 0;
        _requestedTicks = photo?.LastWriteUtcTicks ?? 0;
        _displayedPhotoId = null;
        _displayReady = false;
        _inspectPhoto.IsEnabled = false;
        _previewMetrics.Text = $"Decoder: active {_previewWorker.ActiveCount}, queued {_previewWorker.PendingCount}, peak {_previewWorker.MaxActiveObserved} (max permitted 1) · {_previewWorker.CacheMetrics}";
        _photo.Source = null;
        _currentBitmap?.Dispose(); _currentBitmap = null;
        UpdateViewerState(photo);
        if (photo is null) return;
        // A version check on re-display prevents stale source metadata from being accepted.
        try
        {
            var info = new FileInfo(photo.Path);
            if (!info.Exists)
            {
                if (HasRecordedCommittedMove(photo))
                {
                    ShowPreviewFailure(photo, generation, "Original moved — recovery recorded",
                        "The original is no longer at its source path because this session records a committed Move. " +
                        "Use Inspect recovery journal to verify CURRENT recovery evidence, then Undo verified Move (fixture) if available. " +
                        "Do not manually move or reimport recovery files.");
                    Message("Recorded Move: original source is absent. Inspect recovery evidence or use verified fixture Undo.");
                }
                else if (HasVerifiedCommittedRejectPreview(photo))
                {
                    ShowPreviewFailure(photo, generation,
                        RecoveryPreviewPresentation.Heading, RecoveryPreviewPresentation.Detail);
                    Message("Verified native Reject recovery: generated photo is held in its owned archive. " +
                        "Use the separately confirmed Undo to restore it; normal sorting remains locked.");
                }
                else
                {
                    ShowPreviewFailure(photo, generation, "Photograph missing",
                        "The original file is no longer at its imported path. Inspect recovery and locate the original before proceeding.");
                    Message("Photograph missing at its imported path. No file changes authorized.");
                }
                return;
            }
            if (info.Length != photo.Length || info.LastWriteTimeUtc.Ticks != photo.LastWriteUtcTicks)
            {
                ShowPreviewFailure(photo, generation, "Photo changed",
                    "The file no longer matches the imported version. Reimport before assigning.");
                Message("Photo changed on disk. Reimport before routing this file.");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowPreviewFailure(photo, generation, "Cannot open photograph", ex.Message);
            Message("Cannot inspect current photo: " + ex.Message);
            return;
        }
        _previewWorker.Enqueue(new PreviewWorker.Request(photo.Id, photo.Path, photo.Length, photo.LastWriteUtcTicks, generation));
    }

    private void AcceptPreview(PreviewWorker.Request request, Bitmap? loaded, string? error)
    {
        if (AcceptStudioSheetPreview(request,loaded,error)) return;
        if (AcceptStudioCollectionPreview(request,loaded,error)) return;
        var current = _work.Current;
        if (current is null || request.Generation != _viewGeneration || current.Id != request.PhotoId ||
            current.Length != request.Length || current.LastWriteUtcTicks != request.WriteTicks)
        { loaded?.Dispose(); return; }
        if (error is not null || loaded is null)
        {
            loaded?.Dispose();
            ShowPreviewFailure(current, request.Generation, "Cannot preview photograph",
                "This item remains in the collection. " + (error ?? "Unknown decoder error"));
            Message("Cannot preview this photograph: " + (error ?? "Unknown decoder error"));
            return;
        }
        _photo.Source = null;
        _currentBitmap?.Dispose(); _currentBitmap = loaded;
        _displayedPhotoId = request.PhotoId;
        _displayReady = true;
        _photo.Source = loaded;
        _inspectPhoto.IsEnabled = !_busy;
        UpdateViewerState(current);
        _previewMetrics.Text = $"Decoder: active {_previewWorker.ActiveCount}, queued {_previewWorker.PendingCount}, peak {_previewWorker.MaxActiveObserved} (max permitted 1) · {_previewWorker.CacheMetrics}";
    }

    private static bool IsEditingSource(object? source)
    {
        var visual = source as Avalonia.Visual;
        while (visual is not null)
        {
            if (visual is TextBox) return true;
            visual = visual.GetVisualParent();
        }
        return false;
    }

    private async void OnKeyDown(object? _, KeyEventArgs e)
    {
        // Route keys and photo-navigation shortcuts never fire from Collection,
        // Changes, History, Routes or Diagnostics. Their focused controls own
        // Enter, Space, arrows and editable text.
        if (_studioActivePage != StudioPage.Focus) return;
        if (_busy || IsEditingSource(e.Source)) return;
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F1)
        { e.Handled=true; await ShowHelpAsync(); return; }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.J)
        { e.Handled=true; await JumpDialogAsync(); return; }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.U)
        { e.Handled=true; await JumpUnassignedAsync(); return; }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Home)
        { e.Handled=true; if(_work.Photos.Count>0) await JumpToAsync(1); return; }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.End)
        { e.Handled=true; if(_work.Photos.Count>0) await JumpToAsync(_work.Photos.Count); return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F2)
        { e.Handled=true; await RenameDialogAsync(); return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F11)
        { e.Handled=true; ToggleFullScreen(); return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F12)
        { e.Handled=true; await ShowPhotoInspectionAsync(); return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        { e.Handled=true; ToggleFullScreen(); return; }
        if (!_storeHealthy || _copyBarrier) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z)
        { e.Handled = true; await UndoStageAsync(); return; }
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (!_heldKeys.Add(e.Key)) { e.Handled = true; return; } // Repeated KeyDown is not a new decision.
        if (e.Key == Key.Left) { e.Handled = true; await NavigateAsync(-1); return; }
        if (e.Key == Key.Right) { e.Handled = true; await NavigateAsync(1); return; }
        var key = KeyRouting.RouteFor(e.Key);
        if (key is { } route)
        { e.Handled = true; await AssignAsync(route); }
    }
}
