using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PhotoSiftReborn;

/// <summary>
/// Time-local pinned native file-content/identity observations, NEVER
/// a reusable native capability, operation epoch, consent or commit receipt.
/// All three streams remain open through the cross-file check. Parent identities
/// are checked before AND after; this is not a whole-filesystem atomic snapshot.
/// Linux uses descriptor statx, no-follow directory walks and advisory locks;
/// these are not mandatory exclusion of hostile writers. Unsupported OSes refuse.
/// </summary>
public sealed record M6JP4PinnedFileExpectation(string Path, string Identity,
    string UpperSha256, long Length, long ExpectedWriteUtcTicks = 0);

public sealed record M6JP4FreshPredecessorObservation(bool IsFreshWitness,
    string Explanation, string? ObservationFingerprint = null)
{
    public bool AuthorizesNativeOperation => false;
    public bool GrantsSuccessorEpoch => false;
    public bool CanWriteMoveIntent => false;
    public bool AuthorizesSelectiveUndo => false;
    public bool IsDurableProof => false;
}

public static partial class M6JP4FreshPinnedPredecessor
{
    private const long MaxEvidenceBytes = 512L * 1024 * 1024;
    private const uint AttributeDirectory = 0x10;
    private const uint AttributeReparse = 0x400;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInfo
    {
        public uint Attributes;
        public NativeTime Created, Accessed, Written;
        public uint Volume, HighSize, LowSize, LinkCount, HighIndex, LowIndex;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out NativeInfo info);

    private static string NativeId(NativeInfo item) =>
        $"win:{item.Volume:X8}:{item.HighIndex:X8}{item.LowIndex:X8}";

    private static string ParentIdentity(string path) =>
        NativeFileCapability.DirectoryIdentity(Path.GetDirectoryName(path)
            ?? throw new IOException("No parent folder."));

    private static void Validate(M6JP4PinnedFileExpectation item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Path) ||
            !Path.IsPathFullyQualified(item.Path) ||
            item.Path.IndexOf('\0') >= 0 ||
            !string.Equals(Path.GetFullPath(item.Path),item.Path,StringComparison.Ordinal) ||
            !string.Equals(item.Path.Normalize(System.Text.NormalizationForm.FormC),item.Path,StringComparison.Ordinal) ||
            item.Identity is null || !item.Identity.StartsWith("win:",StringComparison.Ordinal) ||
            item.Identity.Length != 29 ||
            item.UpperSha256 is null || item.UpperSha256.Length!=64 ||
            item.UpperSha256.Any(c=> !(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'F')) ||
            item.Length < 0 || item.Length > MaxEvidenceBytes || item.ExpectedWriteUtcTicks < 0 ||
            SortingWorkspace.HasSymlinkOrReparsePoint(item.Path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(item.Path)!))
            throw new IOException("Noncanonical/linked/oversized/malformed pinned file expectation.");
        for(int index=4; index<item.Identity.Length; index++)
        {
            if(index==12)
            {
                if(item.Identity[index]!=':') throw new IOException("Malformed native identity separator.");
            }
            else if(!Uri.IsHexDigit(item.Identity[index]) || char.IsLower(item.Identity[index]))
                throw new IOException("Malformed native file identity.");
        }
    }

    private sealed class Pin : IDisposable
    {
        private readonly FileStream _stream;
        private readonly M6JP4PinnedFileExpectation _expected;
        private readonly string _parent;
        public string Identity { get; }
        public string Fingerprint { get; }
        public Pin(M6JP4PinnedFileExpectation expected)
        {
            _expected=expected;
            Validate(expected);
            _parent=ParentIdentity(expected.Path);
            _stream=new FileStream(expected.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
            try
            {
                var before=ReadInfo();
                if (before.LinkCount!=1 || (before.Attributes & (AttributeDirectory|AttributeReparse))!=0 ||
                    (((ulong)before.HighSize<<32)|before.LowSize)!=(ulong)expected.Length ||
                    NativeId(before)!=expected.Identity)
                    throw new IOException("Pinned handle disagrees with expected native file identity, size or link count.");
                string digest=Convert.ToHexString(SHA256.HashData(_stream));
                var after=ReadInfo();
                if (NativeId(after)!=NativeId(before) || after.LinkCount!=1 ||
                    (after.Attributes & (AttributeDirectory|AttributeReparse))!=0 ||
                    (((ulong)after.HighSize<<32)|after.LowSize)!=(ulong)expected.Length ||
                    before.Written.Low!=after.Written.Low || before.Written.High!=after.Written.High ||
                    !string.Equals(digest,expected.UpperSha256,StringComparison.Ordinal))
                    throw new IOException("Pinned file changed or bytes did not match exact expected SHA256.");
                if(expected.ExpectedWriteUtcTicks>0 &&
                    File.GetLastWriteTimeUtc(expected.Path).Ticks!=expected.ExpectedWriteUtcTicks)
                    throw new IOException("Saved file timestamp changed.");
                Identity=NativeId(after);
                Fingerprint=Identity+":"+digest+":"+expected.Length;
                CheckPath();
            }
            catch { _stream.Dispose();throw; }
        }
        private NativeInfo ReadInfo()
        {
            if(!GetFileInformationByHandle(_stream.SafeFileHandle,out var result))
                throw new IOException("Pinned Windows file handle information unavailable: "+
                    new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return result;
        }
        public void CheckPath()
        {
            if (SortingWorkspace.HasSymlinkOrReparsePoint(_expected.Path) ||
                SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(_expected.Path)!) ||
                ParentIdentity(_expected.Path)!=_parent ||
                NativeFileCapability.Identity(_expected.Path)!=Identity ||
                !NativeFileCapability.IsSingleLinkRegularFile(_expected.Path))
                throw new IOException("Path, parent handle or link ownership changed after pinning.");
            var state=ReadInfo();
            if(NativeId(state)!=Identity || state.LinkCount!=1 ||
                (state.Attributes & (AttributeDirectory|AttributeReparse))!=0)
                throw new IOException("Pinned object ownership changed during observation.");
        }
        public void Dispose() => _stream.Dispose();
    }

    // P4-R7: non-durable four-handle witness for the EXACT already-committed
    // generated Copy + Move selective Undo. The Copy source/output and Move
    // original archive/output are held together through the final proof.
    // Importantly, a positive result is NOT a durable lock/Undo permission;
    // UndoCore must verify it again immediately before the first relocation.
    public static M6JP4FreshPredecessorObservation InspectGeneratedSelectiveMoveUndo(
        M6JP4PinnedFileExpectation copySource,
        M6JP4PinnedFileExpectation copyDestination,
        M6JP4PinnedFileExpectation moveSourceArchive,
        M6JP4PinnedFileExpectation moveDestination)
    {
        if (OperatingSystem.IsLinux())
            return InspectLinux(new[]{copySource,copyDestination,moveSourceArchive,moveDestination});
        if (!OperatingSystem.IsWindows())
            return new(false,"Four-handle Undo inspection is Windows-only; no cross-platform fallback.");
        try
        {
            var items=new[]{copySource,copyDestination,moveSourceArchive,moveDestination};
            foreach(var item in items) Validate(item);
            if(items.Select(x=>x.Path).Distinct(SortingWorkspace.PathComparer).Count()!=4 ||
               items.Select(x=>x.Identity).Distinct(StringComparer.Ordinal).Count()!=4)
                throw new IOException("Saved paths or native identities alias another owned file.");
            // A runtime path alias can still bypass lexical validation. Holding
            // all four FileStreams prevents participating writers on Windows
            // while final IDs, hashes and parent identities are rechecked.
            using var a=new Pin(copySource);
            using var b=new Pin(copyDestination);
            using var c=new Pin(moveSourceArchive);
            using var d=new Pin(moveDestination);
            if(new[]{a.Identity,b.Identity,c.Identity,d.Identity}
                    .Distinct(StringComparer.Ordinal).Count()!=4)
                throw new IOException("Pinned handles alias a shared native file.");
            a.CheckPath();b.CheckPath();c.CheckPath();d.CheckPath();
            var bytes=System.Text.Encoding.UTF8.GetBytes(
                "P4-R7-GENERATED-SELECTIVE-UNDO-FOUR-PINNED-v1\n"+
                a.Fingerprint+"\n"+b.Fingerprint+"\n"+c.Fingerprint+"\n"+d.Fingerprint);
            return new(true,"Four exact live Windows file handles, hashes, native identities and parents verified once. " +
                "Not atomic across the filesystem, not durable and not a general Undo grant.",
                Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.Security.SecurityException)
        {
            return new(false,"Four-handle selective Undo native evidence REFUSED ("+
                ex.GetType().Name+"); preserve journals and files.");
        }
    }

    public static M6JP4FreshPredecessorObservation Inspect(
        M6JP4PinnedFileExpectation copySource,
        M6JP4PinnedFileExpectation copyDestination,
        M6JP4PinnedFileExpectation prospectiveMoveSource)
    {
        if (OperatingSystem.IsLinux())
            return InspectLinux(new[]{copySource,copyDestination,prospectiveMoveSource});
        if (!OperatingSystem.IsWindows())
            return new(false,"Pinned three-file predecessor verification is Windows-only; no fallback or grant.");
        try
        {
            Validate(copySource);
            Validate(copyDestination);
            Validate(prospectiveMoveSource);
            if (SortingWorkspace.PathComparer.Equals(copySource.Path,copyDestination.Path) ||
                SortingWorkspace.PathComparer.Equals(copySource.Path,prospectiveMoveSource.Path) ||
                SortingWorkspace.PathComparer.Equals(copyDestination.Path,prospectiveMoveSource.Path) ||
                copySource.Identity==copyDestination.Identity ||
                copySource.Identity==prospectiveMoveSource.Identity ||
                copyDestination.Identity==prospectiveMoveSource.Identity)
                throw new IOException("File paths/identities are not disjoint.");
            // Keep all three OS file handles open simultaneously while rechecking
            // the complete set. These local locks are NOT durable after return.
            using var first=new Pin(copySource);
            using var second=new Pin(copyDestination);
            using var third=new Pin(prospectiveMoveSource);
            if(first.Identity==second.Identity || first.Identity==third.Identity || second.Identity==third.Identity)
                throw new IOException("Different pathnames alias one native file.");
            first.CheckPath();second.CheckPath();third.CheckPath();
            string statement="P4-R2-PINNED-WINDOWS-THREE-FILES-v1\n"+
                first.Fingerprint+"\n"+second.Fingerprint+"\n"+third.Fingerprint;
            string fingerprint=Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(statement)));
            return new(true,"Three live Windows handles and current file-content/parent-identity observations verified once; " +
                "not atomic across the filesystem, not durable, no Move intent/Undo authority.",fingerprint);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.Security.SecurityException)
        {
            return new(false,"Fresh pinned predecessor evidence REFUSED ("+ex.GetType().Name+"); preserve all locks.");
        }
    }
}
