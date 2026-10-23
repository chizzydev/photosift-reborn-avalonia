using System;

namespace PhotoSiftReborn;

// M6D: Presentation ONLY. These helpers neither read photo bytes nor query
// durable journals; no status below is a current-file verification grant.
internal enum StudioCollectionSurface { Gallery, Inspector }

internal static class StudioExperiencePresentation
{
    internal static string ContactSheetCompletion(int total, int failures)
    {
        if (total < 0 || failures < 0 || failures > total)
            throw new ArgumentOutOfRangeException(nameof(failures), "Invalid thumbnail completion counts.");
        if (total == 0) return "No photographs on this page.";
        if (failures == total) return $"Could not preview {failures} of {total} photographs. Originals were not changed.";
        if (failures != 0) return $"{total - failures} previews ready · {failures} unavailable. Originals were not changed.";
        return $"{total} previews ready · original photographs unchanged.";
    }

    internal static string CollectionViewLabel(StudioCollectionSurface view) => view switch
    {
        StudioCollectionSurface.Gallery => "Gallery · select a photo to inspect it",
        StudioCollectionSurface.Inspector => "Inspector · preview and choose a folder",
        _ => throw new ArgumentOutOfRangeException(nameof(view))
    };

    // Width is the MEASURED contact-sheet WrapPanel available width in DIPs,
    // not a screenshot pixel count or window-width guess. Card padding/margins
    // are included. Four per page is a stable source-identity policy, not a
    // decoding schedule. Changing widths never queues another decoder.
    internal static double GalleryTileWidth(double availablePanelWidth)
    {
        if (!double.IsFinite(availablePanelWidth) || availablePanelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(availablePanelWidth));
        int columns = availablePanelWidth >= 1130 ? 4 :
                      availablePanelWidth >= 835 ? 3 :
                      availablePanelWidth >= 540 ? 2 : 1;
        // Each child has 16 DIP StudioCard padding and a 10 DIP tile margin.
        const double exterior = 26;
        return Math.Clamp(Math.Floor((availablePanelWidth - columns * exterior) / columns), 170, 390);
    }

    internal static bool ShowEmptyHistory(int operationCount, bool historyAvailable)
    {
        if (operationCount < 0) throw new ArgumentOutOfRangeException(nameof(operationCount));
        return historyAvailable && operationCount == 0;
    }
}
