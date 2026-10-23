namespace PhotoSiftReborn;

/// <summary>
/// A bounded, read-only paired native-history observation. The observer never
/// grants an operation or asserts a filesystem transaction: another process can
/// replace native files immediately after inspection.
/// </summary>
public sealed record M6JRecoveryObservation(
    UnifiedHistoryInventory Inventory,
    M6JDependencyAssessment Dependencies,
    bool ConsistentDatabaseObservation,
    string ConsistencyExplanation)
{
    public bool AuthorizesNativeOperation => false;
    public bool AuthorizesJournalRepair => false;
    public bool AuthorizesSelectiveUndo => false;
}

public sealed partial class WorkspaceSessionStore
{
    /// <summary>
    /// Hold the store's in-process database gate across both pre-existing
    /// read-only scans. Check SQLite data_version for writes from another
    /// connection. A mismatch makes the report INCOMPLETE, never authoritative.
    /// This does NOT lock native files or confer any write/recovery permission.
    /// </summary>
    public M6JRecoveryObservation InspectM6JP3PairedRecoveryObservation()
    {
        lock (_dbGate)
        {
            long before = M6JDataVersion();
            var inventory = InspectUnifiedHistoryInventory();
            var dependencies = InspectM6JReadOnlyDependencies();
            long after = M6JDataVersion();
            bool consistent = before == after && inventory.ScanComplete &&
                inventory.Installation.TotalRows == inventory.Rows.Count &&
                (dependencies.Kind == M6JDependencyKind.IncompleteOrUnverified ||
                 dependencies.ExaminedRows == inventory.Rows.Count);
            string explanation = consistent
                ? "Paired read-only scans passed database-version and journal-count consistency checks. Native file identities may change after inspection."
                : "Paired read-only scans were incomplete or conflicted. Preserve all history; no observation may authorize a write.";
            if (!consistent)
            {
                inventory = inventory with
                {
                    ScanComplete = false,
                    Explanation = explanation + " " + inventory.Explanation
                };
                dependencies = M6JDependencyInspector.Refuse(explanation);
            }
            return new M6JRecoveryObservation(inventory, dependencies, consistent, explanation);
        }
    }
}
