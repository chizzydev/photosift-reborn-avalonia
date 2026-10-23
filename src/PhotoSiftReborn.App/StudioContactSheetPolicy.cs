using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSiftReborn;

// M5D: four-slot maximum, deliberate paged photographic contact sheet.
// Rows are saved-session inspection projections, never filesystem permissions.
internal sealed record StudioContactSheetPage(int Offset, int Total,
    IReadOnlyList<CollectionDecisionRow> Rows)
{
    internal string Label => Total == 0 ? "No photographs match the current filter." :
        $"Photographs {Offset + 1}–{Offset + Rows.Count} of {Total} · up to four on-demand previews.";
}

internal static class StudioContactSheetPolicy
{
    internal const int PageSize = 4;

    internal static StudioContactSheetPage Page(
        IReadOnlyList<CollectionDecisionRow> visible,
        IReadOnlyList<CollectionDecisionRow> fresh, int offset)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(fresh);
        if (offset < 0 || offset % PageSize != 0 ||
            (visible.Count == 0 ? offset != 0 : offset >= visible.Count))
            throw new InvalidDataException("Contact sheet page is no longer valid.");
        var ids = new HashSet<Guid>();
        var positions = new HashSet<int>();
        foreach (var row in visible)
        {
            if (row is null || row.PhotoId == Guid.Empty || row.Position < 1 ||
                !ids.Add(row.PhotoId) || !positions.Add(row.Position))
                throw new InvalidDataException("Contact sheet has duplicate or invalid photo identities.");
            StudioCollectionLens.RequireFresh(row, fresh);
        }
        return new StudioContactSheetPage(offset,visible.Count,
            visible.Skip(offset).Take(PageSize).ToArray());
    }

    internal static bool SamePage(StudioContactSheetPage pinned,
        IReadOnlyList<CollectionDecisionRow> visible,
        IReadOnlyList<CollectionDecisionRow> fresh)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        try
        {
            var now = Page(visible,fresh,pinned.Offset);
            if (pinned.Total != now.Total || pinned.Rows.Count != now.Rows.Count) return false;
            return !pinned.Rows.Where((row,i) =>
                row.PhotoId != now.Rows[i].PhotoId || row.Position != now.Rows[i].Position ||
                !SortingWorkspace.PathComparer.Equals(row.Path,now.Rows[i].Path) ||
                row.State != now.Rows[i].State || row.RouteKey != now.Rows[i].RouteKey ||
                !string.Equals(row.RouteLabel,now.Rows[i].RouteLabel,StringComparison.Ordinal)).Any();
        }
        catch (InvalidDataException) { return false; }
    }

    internal static int? Step(StudioContactSheetPage page, int direction)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));
        var offset = page.Offset + direction * PageSize;
        return offset >= 0 && offset < page.Total ? offset : null;
    }
}
