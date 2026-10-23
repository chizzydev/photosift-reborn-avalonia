using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoSiftReborn;

// M5C: user-initiated, single-step photographic review across the CURRENT
// filtered saved collection. This does not decode, approve or alter any file.
// The owning Window must still recheck original disk identity and use the
// one existing bounded PreviewWorker after a deliberate click.
internal sealed record StudioDeckStep(int Index, CollectionDecisionRow Row);

internal static class StudioCollectionReviewDeck
{
    internal static StudioDeckStep? Adjacent(
        IReadOnlyList<CollectionDecisionRow> visible,
        CollectionDecisionRow selected,
        IReadOnlyList<CollectionDecisionRow> fresh,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(fresh);
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction), "Only one deliberate photo step is allowed.");
        // Do not interpret an ordinal or basename as stable imported identity.
        // Two rows with the same ID or displayed position are an unsafe list.
        var ids = new HashSet<Guid>();
        var positions = new HashSet<int>();
        int currentIndex = -1;
        for (int i = 0; i < visible.Count; ++i)
        {
            var row = visible[i] ?? throw new InvalidDataException("Missing visible photograph.");
            if (row.PhotoId == Guid.Empty || row.Position < 1 ||
                !ids.Add(row.PhotoId) || !positions.Add(row.Position))
                throw new InvalidDataException("Collection contains duplicate or invalid photographic identity.");
            if (row.PhotoId == selected.PhotoId) currentIndex = i;
        }
        if (currentIndex < 0)
            throw new InvalidDataException("Selected photograph is not in this filtered Collection.");
        // Even at a boundary, a stale selected decision is never silently valid.
        StudioCollectionLens.RequireFresh(selected, new[] { visible[currentIndex] });
        StudioCollectionLens.RequireFresh(selected, fresh);
        int destination = currentIndex + direction;
        if (destination < 0 || destination >= visible.Count) return null;
        var next = StudioCollectionLens.RequireFresh(visible[destination], fresh);
        return new StudioDeckStep(destination, next);
    }

    internal static string Position(IReadOnlyList<CollectionDecisionRow> visible, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(visible);
        if (selectedIndex < -1 || selectedIndex >= visible.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        return selectedIndex < 0
            ? $"{visible.Count} photographs in this filtered Collection. Select one to preview."
            : $"Photograph {selectedIndex + 1} of {visible.Count} in this filtered Collection. " +
              "Previous/Next previews are deliberate, read-only requests; saved decisions stay unchanged.";
    }
}
