namespace PhotoSiftReborn;

// Pure in-memory view filter. It cannot stage, approve, move or mutate any
// saved choice. The returned rows retain their original stable PhotoId and
// collection Position, even when the visible ordering is narrowed.
internal static class CollectionDecisionFilter
{
    internal static CollectionDecisionRow[] Apply(
        IReadOnlyList<CollectionDecisionRow> rows, int stateFilter, string? filenameQuery)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (stateFilter is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(stateFilter));
        var term = (filenameQuery ?? string.Empty).Trim();
        var seen = new HashSet<Guid>();
        var results = new List<CollectionDecisionRow>();
        foreach (var row in rows)
        {
            if (row is null || row.PhotoId == Guid.Empty ||
                !seen.Add(row.PhotoId) || string.IsNullOrWhiteSpace(row.Path) ||
                row.State is not ("Assigned" or "RejectReview" or "Unassigned"))
                throw new InvalidDataException("Collection review contains an invalid photo row.");
            bool stateMatches = stateFilter switch
            {
                1 => row.State == "Unassigned",
                2 => row.State == "Assigned",
                3 => row.State == "RejectReview",
                _ => true
            };
            // Search only the filename, not private parent folder paths. No
            // filesystem reads/probes are needed to apply this display filter.
            if (stateMatches && (term.Length == 0 ||
                Path.GetFileName(row.Path).Contains(term,
                    StringComparison.OrdinalIgnoreCase)))
                results.Add(row);
        }
        return results.ToArray();
    }
}
