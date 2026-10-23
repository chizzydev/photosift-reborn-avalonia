using System;

namespace PhotoSiftReborn;

// M6H3: text-only Inspector state. It NEVER decodes, requests or modifies files.
internal static class StudioInspectorEmptyPresentation
{
    internal static string Message(string? reason)
    {
        if(string.IsNullOrWhiteSpace(reason))
            return "Preview on request"+Environment.NewLine+
                "Select a photograph, then choose Preview selected photo."+Environment.NewLine+
                "Originals stay unchanged.";
        if(reason.StartsWith("Selection changed",StringComparison.Ordinal))
            return "Photograph changed"+Environment.NewLine+
                "Preview this selected photograph to load it safely.";
        return "Preview not loaded"+Environment.NewLine+
            "Use Preview selected photo. Exact status is shown below.";
    }
}
