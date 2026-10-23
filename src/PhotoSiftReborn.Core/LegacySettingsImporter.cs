using System.Xml;
using System.Xml.Linq;

namespace PhotoSiftReborn;

/// <summary>
/// Conservative, independent reader for original PhotoSift AppSettings XML. No
/// XmlSerializer of WinForms types, no external entities, no path creation and
/// no automatic application of settings from a file merely being opened.
/// </summary>
public sealed record LegacySettingsProposal(
    WorkspaceSessionSnapshot Candidate, string Report, int RoutesChanged, bool CollisionChanged)
{
    public bool HasChanges => RoutesChanged > 0 || CollisionChanged;
}

public static class LegacySettingsImporter
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly HashSet<string> SimpleFields = new(StringComparer.Ordinal)
    {
        "FileMode", "ExistingFiles", "DeleteMode", "TargetFolder", "AddInRandomOrder",
        "ResetViewModeOnPictureChange", "MediumJump", "LargeJump", "CloseOnEscape",
        "EnlargeSmallImages"
    };

    // XML picker filters are a convenience only: validating the *chosen file*
    // again in Core also protects drag/drop, direct paths and future callers.
    public static void ValidateSupportedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported file type. Choose the original PhotoSift Settings.xml (an .xml file), not a ZIP, folder or installer. Nothing was imported.");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > MaximumBytes)
            throw new InvalidDataException("Can't read this Settings.xml: the file must exist and be between 1 byte and 1 MiB. Nothing was imported.");
        using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[4];
        int n = stream.Read(header);
        if (n >= 2 && header[0] == (byte)'P' && header[1] == (byte)'K')
            throw new InvalidDataException("This is a ZIP archive, not a PhotoSift Settings.xml file. Extract the ZIP and select fixtures/legacy_settings_valid.xml, or select your original Settings.xml. Nothing was imported.");
    }

    public static LegacySettingsProposal Prepare(string xmlPath, SortingWorkspace existing)
    {
        ValidateSupportedFile(xmlPath);
        var fi = new FileInfo(xmlPath);
        if (!fi.Exists || fi.Length is < 1 or > MaximumBytes)
            throw new InvalidDataException("Settings.xml must be a readable XML file of 1 byte to 1 MiB.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        XDocument xml;
        using (var file = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = XmlReader.Create(file, settings))
            xml = XDocument.Load(reader, LoadOptions.None);
        var root = xml.Root;
        if (root is null || root.Name.LocalName != "AppSettings" || root.Name.NamespaceName.Length != 0)
            throw new InvalidDataException("Not an original PhotoSift AppSettings document.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        int ignored = 0;
        foreach (var child in root.Elements())
        {
            var key = child.Name.LocalName;
            bool supported = child.Name.NamespaceName.Length == 0 &&
                (SimpleFields.Contains(key) || IsKeyField(key));
            if (!supported) { ignored++; continue; }
            if (child.HasElements) throw new InvalidDataException("A supported legacy setting is not scalar: " + key);
            if (!fields.TryAdd(key, child.Value))
                throw new InvalidDataException("Duplicate legacy setting refused: " + key);
        }

        // Work on an isolated workspace. The live session, files and SQLite DB
        // are untouched through parsing and preview. No approvals are carried.
        var candidate = new SortingWorkspace();
        candidate.RestoreSession(existing.CaptureSession());
        var report = new List<string>
        {
            "Legacy PhotoSift Settings.xml — preview only",
            "File: " + fi.FullName,
            "Current photos, source folders and output root WILL NOT be replaced.",
            "Imported routes can change staged destinations; a new exact Review is mandatory.",
            ""
        };
        int changed = 0, skipped = 0;
        foreach (char key in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            if (!fields.TryGetValue("KeyFolder_" + key, out var raw)) continue;
            if (string.IsNullOrWhiteSpace(raw)) continue; // Original default: leave current route unchanged.
            if (raw.Length > 320) { report.Add($"[{key}] NOT imported: path exceeds safety limit."); skipped++; continue; }
            try
            {
                // The original accepted absolute Windows paths. Those are never
                // inferred/cross-mapped: use the existing native absolute picker.
                if (LooksRootedAnywhere(raw))
                    throw new ArgumentException("absolute/foreign path requires native relinking");
                candidate.SetRelativeRoute(key, raw);
                var route = candidate.Routes.Single(r => r.Key == key);
                report.Add($"[{key}] Import Relative: {route.Label}");
                changed++;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                report.Add($"[{key}] NOT imported: {ex.Message}");
                skipped++;
            }
        }

        bool collisionChanged = false;
        if (fields.TryGetValue("ExistingFiles", out var collision))
        {
            CollisionChoice? choice = collision.Trim() switch
            {
                "Overwrite" => CollisionChoice.Refuse, // NEVER enable overwrite.
                "AppendNumber" => CollisionChoice.Number,
                "Skip" => CollisionChoice.Skip,
                _ => null
            };
            if (choice is { } safeChoice)
            {
                collisionChanged = candidate.CollisionMode != safeChoice;
                candidate.SetCollisionChoice(safeChoice);
                report.Add($"ExistingFiles={collision.Trim()} → Collisions: {safeChoice}" +
                    (collision.Trim() == "Overwrite" ? " (unsafe Overwrite converted to Refuse)" : ""));
            }
            else report.Add("ExistingFiles value unrecognized; existing collision policy unchanged.");
        }
        if (fields.TryGetValue("FileMode", out var mode))
            report.Add($"FileMode={Short(mode)} → NOT imported. PhotoSift Reborn remains Copy-only.");
        if (fields.TryGetValue("DeleteMode", out var deletion))
            report.Add($"DeleteMode={Short(deletion)} → NOT imported. No permanent deletion or OS Trash activated.");
        if (fields.TryGetValue("TargetFolder", out var target) && !string.IsNullOrWhiteSpace(target))
            report.Add("TargetFolder → NOT imported. Choose output using the native picker.");
        report.Add($"Ignored original appearance/navigation/private fields: {ignored + fields.Keys.Count(k => SimpleFields.Contains(k) && k is not ("FileMode" or "ExistingFiles" or "DeleteMode" or "TargetFolder"))}.");
        report.Add($"Routes to import: {changed}. Unsafe/conflicting routes skipped: {skipped}.");
        report.Add("Original XML remains unchanged. No source photos or destinations are touched.");
        report.Add("Approve only if the listed, safe changes are intended. Closing this dialog makes NO changes.");
        return new LegacySettingsProposal(candidate.CaptureSession(), string.Join(Environment.NewLine, report), changed, collisionChanged);
    }

    private static bool IsKeyField(string key) => key.Length == 11 &&
        key.StartsWith("KeyFolder_", StringComparison.Ordinal) &&
        SortingWorkspace.IsValidKey(key[10]);

    private static bool LooksRootedAnywhere(string path) =>
        Path.IsPathFullyQualified(path) || path.StartsWith('/') || path.StartsWith('\\') ||
        (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');

    private static string Short(string value) => value.Length <= 80 ? value : value[..80] + "…";
}
