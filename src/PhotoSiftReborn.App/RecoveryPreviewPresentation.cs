namespace PhotoSiftReborn;

// A presentation-only mapping. It must never grant consent, infer ownership
// from file absence, or change any journal, session or filesystem object.
internal static class RecoveryPreviewPresentation
{
    internal const string Heading = "Photograph held in verified recovery";
    internal const string Detail =
        "PhotoSift intentionally relocated this generated photograph into its " +
        "verified recovery archive. Its original path is empty until you " +
        "choose Undo native Reject · restore original. No restore happens " +
        "automatically. Normal sorting stays locked. If anything looks wrong, " +
        "inspect the recovery journal before proceeding.";

    // Reject status from one row alone is not evidence of current ownership.
    // The coordinator's fresh native inspection must report the *same* latest
    // operation, source and archive with OWNED_ARCHIVE_VERIFIED.
    internal static bool VerifiedCommittedRejectForPhoto(
        Photo photo, Guid sessionId, DurableRejectItem? latest,
        RejectRecoveryObservation? freshEvidence)
    {
        if (latest is null || freshEvidence is null ||
            sessionId == Guid.Empty || photo.Id == Guid.Empty ||
            latest.State != "Committed" ||
            freshEvidence.State != "Committed" ||
            freshEvidence.Evidence != "OWNED_ARCHIVE_VERIFIED")
            return false;
        return latest.Id == freshEvidence.Operation &&
            latest.PhotoId == photo.Id && latest.SessionId == sessionId &&
            SortingWorkspace.PathComparer.Equals(latest.Source, photo.Path) &&
            SortingWorkspace.PathComparer.Equals(latest.Source, freshEvidence.Source) &&
            SortingWorkspace.PathComparer.Equals(latest.Archive, freshEvidence.Archive);
    }
}
