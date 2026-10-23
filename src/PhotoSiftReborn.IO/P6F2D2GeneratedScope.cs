namespace PhotoSiftReborn;

// P6F2D2B: compiled-in known disposable image bytes, not user-provided
// manifest hashes. Prevent a fabricated ordinary root/manifest from widening
// the generated GUI gate to arbitrary personal photographs.
public static class P6F2D2GeneratedScope
{
    public const string Prefix="PHOTOSIFT_P6F2D2_ORDINARY_GUI_PROBE_";
    public const string Photo01Sha256="10583FF9C6DED73F890F7D84510FA6D58A2B9899E2A0854DFE709F82446A6631";
    public const string Photo02Sha256="A0D0C022E999335A2356A476ADD502E4E2C4E1249300AC9D9CB1BA42BE216138";
    public const string Sentinel="P6F2D2_PROTECTED_OUTPUT_SENTINEL\n";
    public static bool IsExactGeneratedRootName(string leaf) =>
        leaf.StartsWith(Prefix,StringComparison.Ordinal) &&
        leaf.Length==Prefix.Length+32 && leaf[Prefix.Length..].All(Uri.IsHexDigit);
    public static void RequireGeneratedDigests(string first,string second,string sentinel)
    {
        if(!string.Equals(first,Photo01Sha256,StringComparison.OrdinalIgnoreCase) ||
           !string.Equals(second,Photo02Sha256,StringComparison.OrdinalIgnoreCase) ||
           sentinel!=Sentinel)
            throw new IOException("D2B scope does not match the two compiled-in synthetic sample files.");
    }
}
