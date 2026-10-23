using System;

namespace PhotoSiftReborn;

// M6F: deterministic photo-card placement within the one measured Gallery grid.
// This decides presentation only; it has no photo byte, journal or approval access.
internal readonly record struct StudioGalleryGridPlan(int Columns, int Rows, int ItemCount)
{
    internal int ColumnOf(int index)
    {
        if (index < 0 || index >= ItemCount || Columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index % Columns;
    }
    internal int RowOf(int index)
    {
        if (index < 0 || index >= ItemCount || Columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index / Columns;
    }
}

internal static class StudioGalleryGridPolicy
{
    internal static StudioGalleryGridPlan Plan(double measuredWidth, int itemCount)
    {
        if (!double.IsFinite(measuredWidth) || measuredWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(measuredWidth));
        if (itemCount < 0 || itemCount > StudioContactSheetPolicy.PageSize)
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        int columns = measuredWidth >= 1130 ? 4 : measuredWidth >= 835 ? 3 :
            measuredWidth >= 540 ? 2 : 1;
        columns = Math.Min(columns, Math.Max(itemCount, 1));
        int rows = itemCount == 0 ? 0 : (itemCount + columns - 1) / columns;
        return new StudioGalleryGridPlan(columns, rows, itemCount);
    }
}
