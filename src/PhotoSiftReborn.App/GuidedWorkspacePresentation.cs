namespace PhotoSiftReborn;

/// <summary>
/// Describes the next visible step without granting native filesystem authority.
/// History permissions, durable saved revisions and file ownership remain solely
/// with the existing native coordinators; these strings are never a safety gate.
/// </summary>
public sealed record GuidedWorkspaceMessage(string Heading, string NextStep, bool Attention);

public static class GuidedWorkspacePresentation
{
    public static GuidedWorkspaceMessage Describe(int photos, int assignments, int marked,
        int committed, bool hasOutput, bool historyAttention, bool storeHealthy, bool busy)
    {
        if (photos < 0 || assignments < 0 || marked < 0 || committed < 0 ||
            assignments > photos || marked > photos - assignments)
            throw new InvalidDataException("Collection counts are inconsistent; guidance unavailable.");
        if (!storeHealthy)
            return new("Saved work needs attention",
                "Saving isn't available. Don't make more decisions; inspect the workspace status.", true);
        if (historyAttention)
            return new("Review previous file changes",
                "Previous operations are retained. Open Recovery and file tools to check their exact state before continuing.", true);
        if (busy)
            return new("Working on your collection…",
                "Please wait for the current action to finish before making another change.", false);
        if (photos == 0)
            return new("1 · Add your photographs",
                "Open a folder, add files or drop photos here. No originals are changed when importing.", false);
        if (!hasOutput)
            return new("2 · Choose an output folder",
                "Pick a different folder for results. You can assign destination keys without moving originals.", false);
        if (assignments > 0)
            return new("3 · Review your choices",
                $"{assignments} photos have saved folder choices. Use Review paths before Apply; originals stay in place in Copy mode.", false);
        if (marked > 0)
            return new("Review photographs marked for recovery",
                $"{marked} photos are marked for review only. Nothing has been rejected or deleted by marking.", false);
        if (committed > 0)
            return new("File changes are recorded",
                "Use Recovery and file tools for the exact operation state, or add more photographs when permitted.", false);
        return new("2 · Sort with destination keys",
            "Use F, T, W or another configured destination key. Choices save without moving originals.", false);
    }
}
