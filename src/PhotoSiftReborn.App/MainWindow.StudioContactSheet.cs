using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// M5D: a real paged photographic contact sheet using the ALREADY-OWNED one-slot
// replaceable PreviewWorker. It decodes only after a deliberate Load four click,
// then chains one request at a time from the previous verified callback.
// Never queue four at once: PreviewWorker intentionally replaces pending requests.
public sealed partial class MainWindow
{
    private sealed class StudioSheetSlot
    {
        internal required CollectionDecisionRow Row { get; init; }
        internal required Image Image { get; init; }
        internal required TextBlock Placeholder { get; init; }
        internal required StackPanel Tile { get; init; }
        internal required Border Card { get; init; }
        internal required Border Bed { get; init; }
        internal required TextBlock FileName { get; init; }
        internal required TextBlock Status { get; init; }
        internal Bitmap? Bitmap { get; set; }
    }

    private Grid? _studioSheetTiles;
    private TextBlock? _studioSheetStatus;
    private TextBlock? _studioSheetPageLabel;
    private Button? _studioSheetLoad;
    private Button? _studioSheetPrevious;
    private Button? _studioSheetNext;
    private StudioContactSheetPage? _studioSheetPage;
    private Guid _studioSheetSessionId;
    private readonly List<StudioSheetSlot> _studioSheetSlots = [];
    private StudioCollectionPreviewTicket? _studioSheetPending;
    private int _studioSheetNextToDecode;
    private bool _studioSheetLoading;
    private int _studioSheetOffset;
    private int _studioSheetFailedCount;

    // M6F: the bounded four-photo Gallery uses explicit Grid row/column tracks.
    // WrapPanel's desired-width negotiation let the fourth card wrap even when
    // the M6E width helper predicted four columns. The Grid is constrained by
    // its actual host, and every card is attached to a deterministic cell.
    // Reflow only existing controls and owned bitmaps; NEVER enqueue a decode.
    private void UpdateStudioGalleryTileSizing()
    {
        if (_studioSheetTiles is null || _studioSheetSlots.Count == 0 || Bounds.Width <= 0) return;
        double available = _studioSheetTiles.Bounds.Width > 0 ? _studioSheetTiles.Bounds.Width :
            Math.Max(240, Bounds.Width - 88);
        if (!double.IsFinite(available) || available <= 0) return;
        var layout = StudioGalleryGridPolicy.Plan(available, _studioSheetSlots.Count);
        if (_studioSheetTiles.ColumnDefinitions.Count != layout.Columns)
        {
            _studioSheetTiles.ColumnDefinitions.Clear();
            for (int col=0; col<layout.Columns; ++col)
                _studioSheetTiles.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        }
        if (_studioSheetTiles.RowDefinitions.Count != layout.Rows)
        {
            _studioSheetTiles.RowDefinitions.Clear();
            for (int row=0; row<layout.Rows; ++row)
                _studioSheetTiles.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        }
        double width=StudioExperiencePresentation.GalleryTileWidth(available);
        for (int i=0; i<_studioSheetSlots.Count; ++i)
        {
            var slot=_studioSheetSlots[i];
            Grid.SetColumn(slot.Card,layout.ColumnOf(i));
            Grid.SetRow(slot.Card,layout.RowOf(i));
            slot.Tile.Width=width;
            slot.Bed.Height=Math.Round(width*0.59);
            slot.FileName.MaxWidth=width-12;
        }
    }

    private void ClearStudioContactSheet(string message)
    {
        // Retire any pending decoder result; do not cancel native Magick mid-call.
        if (_studioSheetPending is not null) ++_viewGeneration;
        _studioSheetPending = null;
        _studioSheetLoading = false;
        foreach (var slot in _studioSheetSlots)
        {
            slot.Image.Source = null;
            slot.Bitmap?.Dispose();
            slot.Bitmap = null;
        }
        _studioSheetSlots.Clear();
        _studioSheetPage = null;
        _studioSheetSessionId = Guid.Empty;
        _studioSheetNextToDecode = 0;
        _studioSheetFailedCount = 0;
        _studioSheetTiles?.Children.Clear();
        _studioSheetTiles?.RowDefinitions.Clear();
        _studioSheetTiles?.ColumnDefinitions.Clear();
        if (_studioSheetStatus is not null) _studioSheetStatus.Text = message;
        UpdateStudioSheetButtons();
    }

    private void UpdateStudioSheetButtons()
    {
        var ready = _studioSheetPage is not null && !_studioSheetLoading &&
            !_busy && _storeHealthy && !_studioEmbeddedLease.Active &&
            _studioActivePage == StudioPage.Collection;
        if (_studioSheetLoad is not null) _studioSheetLoad.IsEnabled = ready &&
            _studioSheetPage!.Rows.Count > 0;
        if (_studioSheetPrevious is not null)
            _studioSheetPrevious.IsEnabled = ready && StudioContactSheetPolicy.Step(_studioSheetPage!,-1).HasValue;
        if (_studioSheetNext is not null)
            _studioSheetNext.IsEnabled = ready && StudioContactSheetPolicy.Step(_studioSheetPage!,1).HasValue;
    }

    private void RenderStudioContactSheet()
    {
        if (_studioActivePage != StudioPage.Collection || _studioSheetTiles is null) return;
        try
        {
            var fresh = CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            var page = StudioContactSheetPolicy.Page(_studioCollectionVisible,fresh.Rows,_studioSheetOffset);
            _studioSheetPage = page;
            _studioSheetSessionId = _work.SessionId;
            if (_studioSheetPending is not null) ++_viewGeneration;
            _studioSheetPending=null;
            _studioSheetLoading=false;
            foreach (var stale in _studioSheetSlots)
            {
                stale.Image.Source=null;
                stale.Bitmap?.Dispose();
                stale.Bitmap=null;
            }
            _studioSheetTiles.Children.Clear();
            _studioSheetSlots.Clear();
            foreach (var row in page.Rows)
            {
                // Responsive photo-dominant tiles, still only four per page.
                // Resizing never queues work; it merely affects the next render.
                var tileWidth=StudioExperiencePresentation.GalleryTileWidth(
                    _studioSheetTiles.Bounds.Width > 0 ? _studioSheetTiles.Bounds.Width :
                    Math.Max(240, Bounds.Width - 88));
                var tile = new StackPanel { Spacing=5, Width=tileWidth,
                    Margin=new Thickness(0,0,10,8) };
                var photo = new Image { Stretch=Stretch.Uniform,
                    HorizontalAlignment=HorizontalAlignment.Stretch,
                    VerticalAlignment=VerticalAlignment.Stretch };
                // The explicit placeholder says WHY the image region is blank.
                // It never starts decoding; previews remain manual and serial.
                var placeholder = new TextBlock {
                    Text=StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.NotRequested),
                    TextAlignment=TextAlignment.Center, TextWrapping=TextWrapping.Wrap,
                    Foreground=StudioPalette.Secondary, FontSize=13,
                    VerticalAlignment=VerticalAlignment.Center,
                    HorizontalAlignment=HorizontalAlignment.Center,
                    Margin=new Thickness(8) };
                var bedContent = new Grid();
                bedContent.Children.Add(photo);
                bedContent.Children.Add(placeholder);
                var bed = new Border { Background=StudioPalette.PhotoBed,
                    CornerRadius=new CornerRadius(7), Height=Math.Round(tileWidth*0.59), Padding=new Thickness(4),
                    Child=bedContent };
                tile.Children.Add(bed);
                var fileName=new TextBlock { Text=Path.GetFileName(row.Path),
                    TextTrimming=TextTrimming.CharacterEllipsis, MaxWidth=tileWidth-12,
                    Foreground=StudioPalette.Text };
                tile.Children.Add(fileName);
                tile.Children.Add(Muted(row.State=="Assigned" ?
                    "Folder: " + row.RouteLabel : row.State=="RejectReview" ?
                    "Marked for Reject review" : "Needs a folder decision"));
                var info = Muted("Not loaded");
                tile.Children.Add(info);
                var chosen = row;
                tile.Children.Add(Button("Select photograph",()=>SelectStudioSheetPhoto(chosen)));
                var card=StudioCard(tile,8);
                _studioSheetTiles.Children.Add(card);
                _studioSheetSlots.Add(new StudioSheetSlot {
                    Row=row, Image=photo, Placeholder=placeholder, Tile=tile, Card=card, Bed=bed, FileName=fileName, Status=info });
            }
            UpdateStudioGalleryTileSizing(); // exact grid cells; never calls preview decoder
            if (_studioSheetPageLabel is not null) _studioSheetPageLabel.Text = page.Label;
            if (_studioSheetStatus is not null)
                _studioSheetStatus.Text = "Previews load only when requested. No background scanning.";
            UpdateStudioSheetButtons();
        }
        catch (Exception ex)
        {
            ClearStudioContactSheet("Contact sheet refused: " + ex.Message);
            if (_studioSheetPageLabel is not null)
                _studioSheetPageLabel.Text = "Collection changed. Re-select a valid photograph.";
        }
    }

    private void StepStudioContactSheet(int direction)
    {
        if (_studioSheetPage is null || _studioSheetLoading || _busy || !_storeHealthy ||
            _studioActivePage!=StudioPage.Collection || _studioEmbeddedLease.Active) return;
        try
        {
            var fresh=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            if (_studioSheetSessionId!=_work.SessionId ||
                !StudioContactSheetPolicy.SamePage(_studioSheetPage,_studioCollectionVisible,fresh.Rows))
                throw new InvalidDataException("The filtered photo list changed.");
            var next=StudioContactSheetPolicy.Step(_studioSheetPage,direction);
            if (!next.HasValue) return;
            ClearStudioContactSheet("Changing contact sheet page.");
            _studioSheetOffset=next.Value;
            RenderStudioContactSheet(); // no image decode; explicit Load remains required
        }
        catch(Exception ex) { ClearStudioContactSheet("Page change refused: " + ex.Message); }
    }

    private void SelectStudioSheetPhoto(CollectionDecisionRow selected)
    {
        if (_busy || !_storeHealthy || _studioEmbeddedLease.Active ||
            _studioActivePage!=StudioPage.Collection || _studioCollectionList is null ||
            _studioSheetPage is null) return;
        try
        {
            var fresh=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            if (_studioSheetSessionId!=_work.SessionId ||
                !StudioContactSheetPolicy.SamePage(_studioSheetPage,_studioCollectionVisible,fresh.Rows))
                throw new InvalidDataException("Contact sheet identity changed.");
            var row=StudioCollectionLens.RequireFresh(selected,fresh.Rows);
            int index=Array.FindIndex(_studioCollectionVisible,x=>x.PhotoId==row.PhotoId);
            if (index<0 || index<_studioSheetPage.Offset ||
                index>=_studioSheetPage.Offset+_studioSheetPage.Rows.Count)
                throw new InvalidDataException("Selection is outside this page.");
            _studioCollectionList.SelectedIndex=index; // no decoding and no file operation
            ShowStudioCollectionSurface(StudioCollectionSurface.Inspector);
        }
        catch(Exception ex)
        {
            ClearStudioContactSheet("Selection refused: " + ex.Message);
            RefreshStudioCollection();
        }
    }

    private void LoadStudioContactSheet()
    {
        if (_studioSheetLoading || _busy || !_storeHealthy || _studioEmbeddedLease.Active ||
            _studioActivePage!=StudioPage.Collection || _studioSheetPage is null) return;
        try
        {
            var fresh=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            if (_studioSheetSessionId!=_work.SessionId ||
                !StudioContactSheetPolicy.SamePage(_studioSheetPage,_studioCollectionVisible,fresh.Rows))
                throw new InvalidDataException("Contact sheet changed since it was shown.");
            // A page load and a selected-image load are mutually exclusive;
            // both reuse one serial worker, not a new parallel decoder.
            ClearStudioCollectionPreview("Gallery is loading. Select Inspector to request a larger preview.");
            _studioSheetFailedCount=0;
            foreach(var slot in _studioSheetSlots)
            {
                slot.Image.Source=null;
                slot.Bitmap?.Dispose(); slot.Bitmap=null;
                slot.Status.Text="Queued · original unchanged";
                slot.Placeholder.Text=StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.Loading);
                slot.Placeholder.IsVisible=true;
            }
            _studioSheetNextToDecode=0;
            _studioSheetLoading=true;
            UpdateStudioSheetButtons();
            QueueNextStudioSheetPreview();
        }
        catch(Exception ex) { ClearStudioContactSheet("Thumbnail load refused: " + ex.Message); }
    }

    private void QueueNextStudioSheetPreview()
    {
        if (!_studioSheetLoading || _studioSheetPage is null ||
            _studioSheetNextToDecode >= _studioSheetSlots.Count)
        {
            _studioSheetLoading=false;
            if (_studioSheetStatus is not null && _studioSheetPage is not null)
                _studioSheetStatus.Text=StudioExperiencePresentation.ContactSheetCompletion(
                    _studioSheetSlots.Count,_studioSheetFailedCount);
            // The large-photo status is not a live gallery progress indicator:
            // never leave it falsely claiming that a completed sheet is loading.
            if (_studioCollectionPreviewStatus is not null && _studioSheetPage is not null)
                _studioCollectionPreviewStatus.Text="Gallery request finished. Select Inspector for a larger preview.";
            UpdateStudioSheetButtons();
            return;
        }
        try
        {
            var fresh=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            if (_studioActivePage!=StudioPage.Collection || _studioSheetSessionId!=_work.SessionId ||
                !StudioContactSheetPolicy.SamePage(_studioSheetPage,_studioCollectionVisible,fresh.Rows))
                throw new InvalidDataException("Saved photographs changed during thumbnail loading.");
            var slot=_studioSheetSlots[_studioSheetNextToDecode];
            var row=StudioCollectionLens.RequireFresh(slot.Row,fresh.Rows);
            var photos=_work.Photos.Where(p=>p.Id==row.PhotoId).ToArray();
            if (photos.Length!=1)
                throw new InvalidDataException("Thumbnail photo does not have one imported identity.");
            var photo=photos[0];
            var info=new FileInfo(photo.Path);
            if (!info.Exists || info.Length!=photo.Length ||
                info.LastWriteTimeUtc.Ticks!=photo.LastWriteUtcTicks)
                throw new IOException("Original changed since import.");
            var generation=++_viewGeneration;
            _studioSheetPending=StudioCollectionPreviewTicket.Issue(_work.SessionId,row,photo,generation);
            slot.Status.Text="Loading one bounded preview…";
            if (_studioSheetStatus is not null)
                _studioSheetStatus.Text=$"Decoding thumbnail {_studioSheetNextToDecode+1} of {_studioSheetSlots.Count} sequentially.";
            _previewWorker.Enqueue(new PreviewWorker.Request(photo.Id,photo.Path,
                photo.Length,photo.LastWriteUtcTicks,generation));
        }
        catch(Exception ex) { ClearStudioContactSheet("Preview queue refused: " + ex.Message); }
    }

    // Called FIRST by the sole PreviewWorker completed handler.
    // Every callback either consumes its exact current sheet ticket, or is
    // handled/disposed by the existing selected-photo/Focus callback chain.
    private bool AcceptStudioSheetPreview(PreviewWorker.Request req,Bitmap? loaded,string? error)
    {
        var pending=_studioSheetPending;
        if (pending is null || req.Generation!=pending.Generation) return false;
        _studioSheetPending=null;
        try
        {
            if (!_studioSheetLoading || _studioSheetPage is null ||
                _studioActivePage!=StudioPage.Collection || _busy || !_storeHealthy ||
                _studioSheetSessionId!=_work.SessionId || _studioEmbeddedLease.Active)
                throw new InvalidDataException("Contact sheet is no longer the active owner.");
            var fresh=CollectionDecisionPresentation.FromSnapshot(_work.CaptureSession());
            if (!StudioContactSheetPolicy.SamePage(_studioSheetPage,_studioCollectionVisible,fresh.Rows))
                throw new InvalidDataException("Contact sheet session or saved decision changed.");
            var slot=_studioSheetSlots[_studioSheetNextToDecode];
            var photos=_work.Photos.Where(p=>p.Id==req.PhotoId).ToArray();
            if (photos.Length!=1 || !pending.Matches(_work.SessionId,slot.Row,fresh.Rows,photos[0],req))
                throw new InvalidDataException("Thumbnail callback does not match imported identity.");
            var info=new FileInfo(req.Path);
            if (!info.Exists || info.Length!=req.Length ||
                info.LastWriteTimeUtc.Ticks!=req.WriteTicks)
                throw new IOException("Photo changed during preview; no thumbnail displayed.");
            if (error is not null || loaded is null)
            {
                loaded?.Dispose();loaded=null;
                ++_studioSheetFailedCount;
                slot.Status.Text="Preview unavailable";
                slot.Placeholder.Text=StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.Unavailable);
                slot.Placeholder.IsVisible=true;
            }
            else
            {
                slot.Bitmap=loaded;
                slot.Image.Source=loaded;
                slot.Status.Text="Preview ready";
                slot.Placeholder.Text=StudioContactSheetPreviewCues.Tile(StudioSheetVisualState.Ready);
                slot.Placeholder.IsVisible=false;
                loaded=null;
            }
            ++_studioSheetNextToDecode;
            QueueNextStudioSheetPreview(); // enqueue ONLY AFTER prior completion
        }
        catch(Exception ex)
        {
            loaded?.Dispose();
            ClearStudioContactSheet("Contact sheet callback rejected: " + ex.Message);
        }
        return true;
    }
}
