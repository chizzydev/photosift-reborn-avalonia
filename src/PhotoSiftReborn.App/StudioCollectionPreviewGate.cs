using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoSiftReborn;

// M5B: immutable, per-selection request identity for ON-DEMAND photographic
// inspection. This is not a file-operation permit and not a durable journal.
// The existing single bounded PreviewWorker remains the ONLY image decoder.
internal sealed record StudioCollectionPreviewTicket(
    Guid SessionId, CollectionDecisionRow Selection,
    long Generation, long Length, long WriteTicks)
{
    internal static StudioCollectionPreviewTicket Issue(
        Guid sessionId, CollectionDecisionRow selection, Photo original, long generation)
    {
        if (sessionId == Guid.Empty || generation <= 0 || original is null ||
            original.Length < 0 || original.LastWriteUtcTicks <= 0 ||
            original.Id != selection.PhotoId ||
            !SortingWorkspace.PathComparer.Equals(original.Path, selection.Path))
            throw new InvalidDataException("Photo preview identity is unavailable.");
        StudioCollectionLens.RequireFresh(selection, new[]{selection});
        return new StudioCollectionPreviewTicket(sessionId,selection,generation,
            original.Length,original.LastWriteUtcTicks);
    }

    internal bool Matches(Guid currentSessionId, CollectionDecisionRow? currentSelection,
        IReadOnlyList<CollectionDecisionRow> freshRows, Photo? currentPhoto,
        PreviewWorker.Request request)
    {
        if (SessionId != currentSessionId || currentSelection is null ||
            currentPhoto is null || request is null ||
            Generation != request.Generation ||
            Selection.PhotoId != currentSelection.PhotoId ||
            Selection.PhotoId != currentPhoto.Id ||
            Selection.PhotoId != request.PhotoId ||
            Length != currentPhoto.Length || Length != request.Length ||
            WriteTicks != currentPhoto.LastWriteUtcTicks || WriteTicks != request.WriteTicks ||
            !SortingWorkspace.PathComparer.Equals(Selection.Path, currentPhoto.Path) ||
            !SortingWorkspace.PathComparer.Equals(Selection.Path,request.Path))
            return false;
        try
        {
            StudioCollectionLens.RequireFresh(Selection,new[]{currentSelection});
            StudioCollectionLens.RequireFresh(Selection,freshRows);
            return true;
        }
        catch (InvalidDataException) { return false; }
    }
}
