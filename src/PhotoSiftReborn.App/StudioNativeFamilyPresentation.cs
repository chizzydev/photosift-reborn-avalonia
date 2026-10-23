using System;

namespace PhotoSiftReborn;

// Studio M4A is a presentation-only catalog of the supported native families.
// This data MUST NOT be used as an operation capability or an approval gate.
// Existing Copy/Move/Rename/Reject coordinators make all native decisions.
internal enum StudioNativeFamily { Copy, Move, Rename, Reject }

internal sealed record StudioFamilyDescription(string Name, string Scope,
    string OriginalEffect, string ReviewAndConsent, string Recovery,
    bool ProposalCanBeDurableBeforeFileMovement);

internal static class StudioNativeFamilyPresentation
{
    internal static StudioFamilyDescription Describe(StudioNativeFamily family) => family switch
    {
        StudioNativeFamily.Copy => new(
            "Copy", "Saved destination choices; native included/excluded paths must be reviewed.",
            "Original photographs remain in place.",
            "Review exact sources and destinations; Approve does not Apply.",
            "Verified Copy Undo archives owned outputs; it never deletes originals.", false),
        StudioNativeFamily.Move => new(
            "Move", "Currently limited to one supported first-epoch local photo, Refuse collisions and no skips.",
            "A successful Move vacates the original location after a verified output and retains recovery ownership.",
            "Select Move explicitly. Its exact native review and approval are independent of Copy.",
            "Use the existing verified Move Undo; no arbitrary family chains.", false),
        StudioNativeFamily.Rename => new(
            "Rename", "One native-supported local photograph, within its existing operation-history policy.",
            "The original path changes its name; photograph bytes are not intentionally edited.",
            "Inspect the exact before/after names and paths; separately Apply only after Rename approval.",
            "Use verified Rename Undo only when the old path is safe and vacant.", false),
        StudioNativeFamily.Reject => new(
            "Reject", "One durably saved Reject-review mark in a supported fresh local session.",
            "A completed Reject moves the ORIGINAL into app-owned recovery, never OS Trash.",
            "Stage writes a durable proposal without moving the file; Approve and Execute are separate.",
            "Undo needs independently verified owned recovery; cancelled Stage/Approved history remains recorded.", true),
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };
}
