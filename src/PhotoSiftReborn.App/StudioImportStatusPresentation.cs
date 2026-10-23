using System;

namespace PhotoSiftReborn;

// UI-only import feedback after the preexisting validated import and save.
// No claim that any native operation has been authorized or executed.
internal static class StudioImportStatusPresentation
{
    internal static string Short(int added, int total, bool resultsReady, int warningCount)
    {
        if(added<0 || total<0 || added>total || warningCount<0)
            throw new ArgumentOutOfRangeException(nameof(added));
        string status=warningCount>0
            ? $"Import complete with {warningCount} warning(s) · {total} photographs in Collection."
            : added==0 ? $"Collection unchanged · {total} photographs ready."
            : $"{added} photographs added · {total} in Collection · Originals unchanged.";
        return status + (resultsReady ? " Results folder ready." : " Next: choose Results folder.") +
            " Full import counts on hover.";
    }
}
