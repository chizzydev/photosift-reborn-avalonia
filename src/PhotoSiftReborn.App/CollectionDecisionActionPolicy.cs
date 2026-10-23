namespace PhotoSiftReborn;

// Selection gating for the ordinary collection review. This is a UI policy,
// NEVER a durable operation grant, native journal token or disk permission.
// The actual handler rechecks live workspace/session/history before mutation.
internal enum CollectionDecisionAction { Show, ClearFolderChoice, UnmarkRejectReview, AssignFilteredBatch }
internal static class CollectionDecisionActionPolicy
{
    // Previewed filtered batch selection is a staged-choice convenience only.
    // The caller must still check live session revision, native history locks,
    // configured route and the exact original stable identities on approval.
    internal static bool CanOfferBatch(IReadOnlyList<CollectionDecisionRow>? rows,
        bool canEditCollection)
    {
        if (!canEditCollection || rows is null || rows.Count is < 1 or > 100)
            return false;
        var seen = new HashSet<Guid>();
        return rows.All(row => row is not null && row.PhotoId != Guid.Empty &&
            row.State == "Unassigned" && !string.IsNullOrWhiteSpace(row.Path) &&
            seen.Add(row.PhotoId));
    }

    internal static bool CanOffer(CollectionDecisionRow? row,
        CollectionDecisionAction action, bool canNavigate,
        bool canClearFolderChoice, bool canUnmarkRejectReview)
    {
        if (row is null || row.PhotoId == Guid.Empty) return false;
        return action switch
        {
            CollectionDecisionAction.Show => canNavigate,
            CollectionDecisionAction.ClearFolderChoice =>
                row.State == "Assigned" && canClearFolderChoice,
            CollectionDecisionAction.UnmarkRejectReview =>
                row.State == "RejectReview" && canUnmarkRejectReview,
            _ => false
        };
    }
}
