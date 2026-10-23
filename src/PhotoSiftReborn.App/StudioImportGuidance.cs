namespace PhotoSiftReborn;

/// <summary>
/// M6H source/setup copy only. Never grants import/write authority, never
/// inspects files, and never stands in for native path, identity or review gates.
/// </summary>
public enum StudioImportSource { Cancel = 0, Files = 1, Folder = 2 }

public static class StudioImportGuidance
{
    public static StudioImportSource RequireSource(int raw) => raw switch
    {
        0 => StudioImportSource.Cancel,
        1 => StudioImportSource.Files,
        2 => StudioImportSource.Folder,
        _ => throw new ArgumentOutOfRangeException(nameof(raw), "Unknown source choice refused.")
    };

    public static string Describe(int photographs, bool resultsFolderConfigured, bool historyLocked)
    {
        if (photographs < 0) throw new ArgumentOutOfRangeException(nameof(photographs));
        if (historyLocked)
            return "Previous file changes need review. Open History & recovery before any new sorting.";
        if (!resultsFolderConfigured)
            return photographs == 0
                ? "Add your photos, then select a different results folder. No file changes yet."
                : "Next: select a results folder. Imported originals stay where they are.";
        if (photographs == 0)
            return "Results folder ready. Choose individual photos or a folder to begin.";
        return "Results folder ready. Choose a destination key, then review exact paths in Changes.";
    }
}
