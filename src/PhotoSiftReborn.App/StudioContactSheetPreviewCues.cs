using System;

namespace PhotoSiftReborn;

// M6H2: text-only visual cues; no IO, decoder requests, session changes,
// prefetch, native-operation capability or permission implications.
internal enum StudioSheetVisualState { NotRequested, Loading, Ready, Unavailable }
internal static class StudioContactSheetPreviewCues
{
    internal static string Tile(StudioSheetVisualState state) => state switch
    {
        StudioSheetVisualState.NotRequested => "Preview not loaded\nUse Load four thumbnails above",
        StudioSheetVisualState.Loading => "Loading preview on request...",
        StudioSheetVisualState.Ready => string.Empty,
        StudioSheetVisualState.Unavailable => "Preview unavailable\nOriginal remains unchanged",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
