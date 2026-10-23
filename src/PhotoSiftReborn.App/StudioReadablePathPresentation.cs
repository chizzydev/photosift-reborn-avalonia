using System;
using System.IO;

namespace PhotoSiftReborn;

// M6H3: presentation ONLY. Never normalizes, resolves, opens or modifies a
// source/output path. Exact source, destination and native approval values
// remain the original saved strings, available from read-only disclosure.
internal static class StudioReadablePathPresentation
{
    internal static string Summary(string role, string? exactPath, string missing)
    {
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("A role is required.", nameof(role));
        if (string.IsNullOrWhiteSpace(exactPath)) return role + ": " + missing;
        string trimmed=exactPath.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string leaf=Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(leaf)) leaf=exactPath;
        // This is non-authoritative display text: paths are NEVER taken back
        // from the compact summary for any filesystem operation or approval.
        return role + ": " + leaf + " (view exact path below)";
    }

    internal static string Exact(string? source, string? output)
    {
        return "SOURCE (read-only):" + Environment.NewLine +
            (source ?? "Not chosen") + Environment.NewLine + Environment.NewLine +
            "RESULTS (read-only):" + Environment.NewLine +
            (output ?? "Not chosen") + Environment.NewLine + Environment.NewLine +
            "These are saved settings, not proof that any file exists or was modified.";
    }

    internal static string Route(char key, string kind, string label, string expanded)
    {
        if (string.IsNullOrWhiteSpace(expanded)) throw new ArgumentException("Destination required.",nameof(expanded));
        // The human-readable label is NOT passed to route evaluation.
        var final=Path.GetFileName(expanded.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(final)) final=expanded;
        return key + " · " + kind + ": " + label + " → " + final;
    }
}
