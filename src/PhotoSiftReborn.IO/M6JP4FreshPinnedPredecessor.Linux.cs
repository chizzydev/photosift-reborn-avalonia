using System.Security.Cryptography;
using System.Text;

namespace PhotoSiftReborn;

public static partial class M6JP4FreshPinnedPredecessor
{
    private static void ValidateLinux(M6JP4PinnedFileExpectation item)
    {
        if(item is null || string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathFullyQualified(item.Path) ||
            item.Path.IndexOf('\0')>=0 || Path.GetFullPath(item.Path)!=item.Path ||
            item.Path.Normalize(NormalizationForm.FormC)!=item.Path ||
            item.Identity is null || item.Identity.Length!=40 || !item.Identity.StartsWith("linux:",StringComparison.Ordinal) ||
            item.Identity[14]!=':' || item.Identity[23]!=':' ||
            item.UpperSha256 is null || item.UpperSha256.Length!=64 ||
            item.UpperSha256.Any(c=>!(c>='0' && c<='9') && !(c>='A' && c<='F')) ||
            item.Length<0 || item.Length>MaxEvidenceBytes || item.ExpectedWriteUtcTicks<0 ||
            SortingWorkspace.HasSymlinkOrReparsePoint(item.Path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(item.Path)!))
            throw new IOException("Noncanonical/linked/malformed Linux pinned expectation.");
        for(int i=6;i<40;i++)
            if(i!=14 && i!=23 && !(item.Identity[i]>='0' && item.Identity[i]<='9') &&
                !(item.Identity[i]>='A' && item.Identity[i]<='F'))
                throw new IOException("Malformed Linux native identity.");
    }

    private static M6JP4FreshPredecessorObservation InspectLinux(
        M6JP4PinnedFileExpectation[] items,Action? whileAllHeld=null)
    {
        var pins=new List<NativeFileCapability.LinuxPinnedRead>();
        try
        {
            if(!OperatingSystem.IsLinux() || items.Length is not (3 or 4)) throw new IOException("Unsupported pin set.");
            foreach(var item in items) ValidateLinux(item);
            if(items.Select(x=>x.Path.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=items.Length ||
                items.Select(x=>x.Identity).Distinct(StringComparer.Ordinal).Count()!=items.Length)
                throw new IOException("Pinned participants alias a path or native identity.");
            foreach(var item in items) pins.Add(new NativeFileCapability.LinuxPinnedRead(item));
            if(pins.Select(x=>x.Identity).Distinct(StringComparer.Ordinal).Count()!=items.Length ||
                pins.Select(x=>x.Device).Distinct(StringComparer.Ordinal).Count()!=1)
                throw new IOException("Pinned participants alias or cross devices.");
            whileAllHeld?.Invoke();
            // Every descriptor remains held throughout BOTH final passes. Each
            // pass hashes through the descriptor and compares full statx state.
            foreach(var pin in pins) pin.CheckPath();
            foreach(var pin in pins) pin.CheckPath();
            // Domain-separated Linux v1 statement. Keep the public digest at 64
            // characters for existing observation consumers; Windows bytes unchanged.
            string statement=$"R30-R3-LINUX-STATX-{items.Length}-PINNED-v1\n"+
                string.Join("\n",pins.Select(x=>x.Fingerprint));
            return new(true,$"R30-R3-LINUX-STATX-{items.Length}-PINNED-v1: descriptor hashes, native identities, timestamps, links and retained parents verified. "+
                "Ephemeral cooperative-local observation; advisory flock does not exclude hostile writers. No durable or operation authority.",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(statement))));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or System.Security.SecurityException or OverflowException)
        {
            return new(false,"Linux pinned evidence HOLD ("+ex.GetType().Name+"): "+ex.Message);
        }
        finally { for(int i=pins.Count-1;i>=0;i--) pins[i].Dispose(); }
    }

    // Internal deterministic adversarial seam; the callback runs only after ALL
    // descriptors have been acquired, with the real production final checks.
    // It cannot issue a grant, mutate a journal or bypass an ownership predicate.
    internal static M6JP4FreshPredecessorObservation InspectHeldForTest(
        M6JP4PinnedFileExpectation[] items,Action whileAllHeld)
    {
        if(OperatingSystem.IsLinux()) return InspectLinux(items,whileAllHeld);
        if(!OperatingSystem.IsWindows() || items.Length is not (3 or 4))
            return new(false,"Unsupported native pin set.");
        var pins=new List<Pin>();
        try
        {
            foreach(var item in items) pins.Add(new Pin(item));
            if(pins.Select(p=>p.Identity).Distinct(StringComparer.Ordinal).Count()!=items.Length)
                throw new IOException("Aliased native participants.");
            whileAllHeld();
            foreach(var pin in pins) pin.CheckPath();
            return new(true,"Test-only ephemeral Windows pinned observation; no authority.");
        }
        catch(IOException ex) { return new(false,"Native pinned evidence refused: "+ex.Message); }
        finally { for(int i=pins.Count-1;i>=0;i--) pins[i].Dispose(); }
    }
}
