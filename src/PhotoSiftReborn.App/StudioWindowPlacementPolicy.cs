using System;

namespace PhotoSiftReborn;

// M6E: presentation-only window placement. Physical monitor work-area pixels
// and logical Avalonia DIPs are kept distinct. This policy never reads disk,
// changes a journal, or grants permission for any photograph operation.
internal readonly record struct StudioWindowPlacement(
    double WidthDip, double HeightDip, int XPixel, int YPixel);

internal static class StudioWindowPlacementPolicy
{
    internal static StudioWindowPlacement CenterInWorkArea(
        int workX, int workY, int workWidthPx, int workHeightPx,
        double scaling, double requestedWidthDip, double requestedHeightDip,
        double minimumWidthDip, double minimumHeightDip)
    {
        if (workWidthPx <= 0 || workHeightPx <= 0 || !double.IsFinite(scaling) || scaling <= 0 ||
            !double.IsFinite(requestedWidthDip) || requestedWidthDip <= 0 ||
            !double.IsFinite(requestedHeightDip) || requestedHeightDip <= 0 ||
            !double.IsFinite(minimumWidthDip) || minimumWidthDip <= 0 ||
            !double.IsFinite(minimumHeightDip) || minimumHeightDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(workWidthPx), "Invalid monitor dimensions or display scale.");
        var usableWidth = workWidthPx / scaling;
        var usableHeight = workHeightPx / scaling;
        // Use normal-window margins where possible; never demand a width
        // exceeding a smaller monitor. The caller uses a compact shell.
        var width = Math.Min(Math.Max(requestedWidthDip, minimumWidthDip),
            Math.Max(1, usableWidth - 24));
        var height = Math.Min(Math.Max(requestedHeightDip, minimumHeightDip),
            Math.Max(1, usableHeight - 24));
        var physicalWidth = Math.Min(workWidthPx, (int)Math.Ceiling(width * scaling));
        var physicalHeight = Math.Min(workHeightPx, (int)Math.Ceiling(height * scaling));
        return new StudioWindowPlacement(width, height,
            checked(workX + (workWidthPx - physicalWidth) / 2),
            checked(workY + (workHeightPx - physicalHeight) / 2));
    }
}
