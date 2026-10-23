using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PhotoSiftReborn;

// Presentation-only assistance for the real Recovery Command Center.
// The optional sample is constructed in memory and is NEVER an inventory,
// a journal row, a review ticket, an operation grant or a repair candidate.
internal static class StudioRecoveryCommandCenterUiState
{
    internal static string EmptyHeading(StudioRecoveryDossier? dossier, int shown,
        int familyIndex, int evidenceIndex, string? search)
    {
        if (dossier is null) return "Inspection hasn't completed";
        if (shown != 0) return "Recorded operations";
        if (dossier.Cases.Count == 0) return "No saved operations yet";
        if (familyIndex != 0 || evidenceIndex != 0 || !string.IsNullOrWhiteSpace(search))
            return "No operations match these filters";
        return "No operation rows available";
    }

    internal static string EmptyExplanation(StudioRecoveryDossier? dossier, int shown,
        int familyIndex, int evidenceIndex, string? search)
    {
        if (dossier is null)
            return "Choose Inspect current native evidence to read recorded history. Nothing is changed by viewing this panel.";
        if (shown != 0) return "Choose a saved operation to inspect its native proof.";
        if (dossier.Cases.Count == 0)
            return "This installation has no recorded Copy, Move, Rename or Reject journals. This is not proof that every file is safe to change. Future operations will appear here after an inspection.";
        if (familyIndex != 0 || evidenceIndex != 0 || !string.IsNullOrWhiteSpace(search))
            return "There are saved operations, but none meet the current family, evidence and search selections. Choose All operations and All evidence to see the complete inspected list.";
        return "No records can be displayed from this observation. Reinspect; do not treat an empty list as authorization.";
    }

    internal static string RowLabel(StudioRecoveryCase item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Family + "  ·  " + item.State +
               "\n" + item.Key + "  ·  " + item.Proof;
    }

    internal static string OperationPrimary(StudioRecoveryCase item) =>
        item.Family.ToUpperInvariant() + "  ·  " + item.State;

    internal static string OperationSecondary(StudioRecoveryCase item)
    {
        string shortId = item.Key.Length >= 8 ? item.Key[..8].ToUpperInvariant() : item.Key;
        return "Evidence: " + item.Proof + "  ·  " + shortId + "  ·  View evidence →";
    }

    // Every row has a visible evidence-opening affordance; selection remains
    // the existing ListBox interaction, NOT any filesystem or journal action.
    internal static StackPanel OperationRow(StudioRecoveryCase item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var panel = new StackPanel { Spacing = 5, Margin = new Thickness(6, 7, 4, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = OperationPrimary(item),
            FontWeight = FontWeight.SemiBold, FontSize = 15,
            Foreground = StudioPalette.Text, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = OperationSecondary(item),
            Foreground = StudioPalette.Secondary, TextWrapping = TextWrapping.Wrap
        });
        ToolTip.SetTip(panel, "Select to inspect the saved operation. Full identity appears in the evidence panel. This action is read-only.");
        return panel;
    }

    internal static string Summary(StudioRecoveryDossier dossier, int shown)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        int unverified = dossier.Cases.Count(x => x.Proof == UnifiedRowProof.NotVerified);
        int unfinished = dossier.Cases.Count(x => x.State is not ("Committed" or "Undone"));
        string noun = dossier.Heading.StartsWith("SAMPLE VIEW", StringComparison.Ordinal)
            ? " example operations" : " saved";
        return dossier.Cases.Count + noun + "  ·  " + unverified + " unverified  ·  " +
            unfinished + " unfinished  ·  " + dossier.Conflicts.Count +
            " conflicts  ·  " + shown + " shown";
    }

    internal static StudioRecoveryDossier ExampleOnly()
    {
        // All IDs, states and descriptions below are explicitly fictitious.
        // No SQLite, native inspector or filesystem API is called.
        var rows = new[]
        {
            new StudioRecoveryCase(new string('1',32), "Copy", "Committed",
                new string('a',32), UnifiedRowProof.NativeActiveVerified,
                "SAMPLE ONLY — imagined committed Copy; NOT a native ownership proof."),
            new StudioRecoveryCase(new string('2',32), "Move", "UndoRestoreIntent",
                new string('a',32), UnifiedRowProof.NotVerified,
                "SAMPLE ONLY — simulated interrupted Move. This is NOT a recoverable file."),
            new StudioRecoveryCase(new string('3',32), "Rename", "Undone",
                new string('b',32), UnifiedRowProof.NativeTerminalVerified,
                "SAMPLE ONLY — imagined terminal Rename. NOT checked on disk."),
            new StudioRecoveryCase(new string('4',32), "Reject", "Staged",
                new string('c',32), UnifiedRowProof.NotVerified,
                "SAMPLE ONLY — simulated unfinished Reject. There are no source files.")
        };
        var edge = new M6JDependencyEdge("Copy", rows[0].Key,
            "Move", rows[1].Key, M6JConflictKind.SameRecordedPath,
            "SAMPLE ONLY — an illustrative shared path conflict. No native scan occurred.");
        return new StudioRecoveryDossier(
            "SAMPLE VIEW — FOUR FICTITIOUS OPERATIONS",
            "SAMPLE DATA ONLY — NOT YOUR FILES OR JOURNALS",
            "Use the family buttons, evidence filter, search and selection to preview the interface. Select Reinspect live evidence to return to real saved history.",
            "SAMPLE ONLY — one illustrative conflict. NOT a filesystem or database inspection.",
            rows, new[] {edge},
            "SAMPLE DATA ONLY — NEVER A NATIVE RECOVERY REPORT.\n" +
            "No disk or SQLite evidence was read to create these rows.\n" +
            StudioRecoveryCommandCenterPresentation.EvidenceLimit);
    }
}
