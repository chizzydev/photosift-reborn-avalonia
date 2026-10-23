using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhotoSiftReborn;

public static partial class NativeFileCapability
{
    // statx is a fixed Linux UAPI structure, independent of libc's struct stat.
    // Read metadata through the descriptor (AT_EMPTY_PATH), never /proc links.
    internal readonly record struct LinuxPinnedState(string Identity, string Device,
        uint Links, int Mode, ulong Length, long ModifiedSeconds, uint ModifiedNanos,
        long ChangedSeconds, uint ChangedNanos)
    {
        internal long WriteTicks => checked(DateTime.UnixEpoch.Ticks +
            ModifiedSeconds * TimeSpan.TicksPerSecond + ModifiedNanos / 100);
    }

    private static LinuxPinnedState LinuxPinnedStat(int fd)
    {
        IntPtr buffer=Marshal.AllocHGlobal(256);
        try
        {
            Marshal.Copy(new byte[256],0,buffer,256);
            if(Statx(fd,"",AT_EMPTY_PATH,STATX_BASIC_STATS,buffer)!=0)
                throw new IOException("Pinned Linux statx failed: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            uint mask=unchecked((uint)Marshal.ReadInt32(buffer,0));
            // type, nlink, mtime, ctime, inode and size must all be supplied.
            if((mask & 0x3c5)!=0x3c5) throw new IOException("Incomplete pinned statx evidence.");
            uint major=unchecked((uint)Marshal.ReadInt32(buffer,0x88));
            uint minor=unchecked((uint)Marshal.ReadInt32(buffer,0x8c));
            ulong inode=unchecked((ulong)Marshal.ReadInt64(buffer,0x20));
            var result=new LinuxPinnedState($"linux:{major:X8}:{minor:X8}:{inode:X16}",
                $"linux:{major:X8}:{minor:X8}",unchecked((uint)Marshal.ReadInt32(buffer,0x10)),
                Marshal.ReadInt16(buffer,0x1c)&0xf000,unchecked((ulong)Marshal.ReadInt64(buffer,0x28)),
                Marshal.ReadInt64(buffer,0x70),unchecked((uint)Marshal.ReadInt32(buffer,0x78)),
                Marshal.ReadInt64(buffer,0x60),unchecked((uint)Marshal.ReadInt32(buffer,0x68)));
            if(result.ModifiedNanos>=1_000_000_000 || result.ChangedNanos>=1_000_000_000 || inode==0)
                throw new IOException("Malformed pinned statx evidence.");
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("libc",SetLastError=true,EntryPoint="flock")]
    private static extern int LinuxFlock(int fd,int operation);

    // Each component is opened relative to the previous still-open directory.
    // O_NOFOLLOW on the leaf alone would not protect against linked ancestors.
    internal sealed class LinuxPinnedRead : IDisposable
    {
        private readonly List<(string Path, SafeFileHandle Handle, string Identity)> _directories=new();
        private readonly M6JP4PinnedFileExpectation _expected;
        private FileStream? _stream;
        private LinuxPinnedState _saved;
        internal string Identity => _saved.Identity;
        internal string Device => _saved.Device;
        internal string Fingerprint => Identity+":"+_expected.UpperSha256+":"+_expected.Length+
            ":"+_saved.ModifiedSeconds+":"+_saved.ModifiedNanos+":"+_saved.ChangedSeconds+":"+_saved.ChangedNanos+
            ":"+_directories[^1].Identity;

        internal LinuxPinnedRead(M6JP4PinnedFileExpectation expected)
        {
            if(!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            _expected=expected;
            try
            {
                string parent=Path.GetDirectoryName(expected.Path)!;
                string current="/";
                AddDirectory(current,LinuxOpen(current,O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC));
                foreach(string component in parent.Split('/',StringSplitOptions.RemoveEmptyEntries))
                {
                    CheckAlias(current,component);
                    int next=LinuxOpenAt(Fd(_directories[^1].Handle),component,O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC);
                    current=Path.Combine(current,component);
                    AddDirectory(current,next);
                }
                CheckAlias(parent,Path.GetFileName(expected.Path));
                // O_NONBLOCK prevents a substituted FIFO from blocking the process.
                int fd=LinuxOpenAt(Fd(_directories[^1].Handle),Path.GetFileName(expected.Path),
                    O_NOFOLLOW|O_CLOEXEC|0x800);
                if(fd<0) throw new IOException("Cannot open pinned Linux regular file without following links.");
                var handle=new SafeFileHandle((IntPtr)fd,true);
                try
                {
                    _saved=LinuxPinnedStat(fd);
                    RequireExpected(_saved);
                    if(_saved.Device!=LinuxPinnedStat(Fd(_directories[^1].Handle)).Device)
                        throw new IOException("Pinned Linux file and parent are on different devices.");
                    // Advisory only: conflicting cooperative exclusive owners refuse.
                    // External writers ignoring flock are addressed by ctime/hash
                    // rechecks, not by a false claim of mandatory writer exclusion.
                    if(LinuxFlock(fd,1|4)!=0) throw new IOException("Pinned Linux advisory ownership lock refused.");
                    _stream=new FileStream(handle,FileAccess.Read);
                }
                catch { handle.Dispose();throw; }
                CheckStableHash();
                CheckPath();
            }
            catch { Dispose();throw; }
        }

        private static int Fd(SafeFileHandle handle)=>checked((int)handle.DangerousGetHandle());

        private void AddDirectory(string path,int fd)
        {
            if(fd<0) throw new IOException("Cannot pin Linux ancestor directory without following links.");
            var handle=new SafeFileHandle((IntPtr)fd,true);
            try
            {
                var info=LinuxPinnedStat(fd);
                if(info.Mode!=0x4000) throw new IOException("Pinned ancestor is not a directory.");
                _directories.Add((path,handle,info.Identity));
            }
            catch { handle.Dispose();throw; }
        }

        private static void CheckAlias(string parent,string leaf)
        {
            int examined=0;
            foreach(string entry in Directory.EnumerateFileSystemEntries(parent))
            {
                if(++examined>4096) throw new IOException("Pinned directory alias scan exceeds bound.");
                string name=Path.GetFileName(entry);
                if(!string.Equals(name,leaf,StringComparison.Ordinal) &&
                    string.Equals(name.Normalize(NormalizationForm.FormC),leaf.Normalize(NormalizationForm.FormC),
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Case/Unicode alias of pinned component exists.");
            }
        }

        private void RequireExpected(LinuxPinnedState state)
        {
            if(state.Mode!=0x8000 || state.Links!=1 || state.Identity!=_expected.Identity ||
                state.Length!=(ulong)_expected.Length ||
                (_expected.ExpectedWriteUtcTicks>0 && state.WriteTicks!=_expected.ExpectedWriteUtcTicks))
                throw new IOException("Pinned Linux type/link/identity/size/timestamp differs from review.");
        }

        private void CheckStableHash()
        {
            var stream=_stream ?? throw new ObjectDisposedException(nameof(LinuxPinnedRead));
            var before=LinuxPinnedStat(Fd(stream.SafeFileHandle));
            RequireExpected(before);
            if(before!=_saved) throw new IOException("Pinned Linux metadata changed during observation.");
            stream.Position=0;
            string hash=Convert.ToHexString(SHA256.HashData(stream));
            var after=LinuxPinnedStat(Fd(stream.SafeFileHandle));
            RequireExpected(after);
            if(before!=after || hash!=_expected.UpperSha256)
                throw new IOException("Pinned Linux bytes or metadata changed during hash.");
        }

        internal void CheckPath()
        {
            foreach(var dir in _directories)
            {
                var pinned=LinuxPinnedStat(Fd(dir.Handle));
                if(pinned.Mode!=0x4000 || pinned.Identity!=dir.Identity || DirectoryIdentity(dir.Path)!=dir.Identity)
                    throw new IOException("Pinned Linux parent/ancestor was substituted.");
                if(dir.Path!="/") CheckAlias(Path.GetDirectoryName(dir.Path)!,Path.GetFileName(dir.Path));
            }
            if(SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(_expected.Path)!) ||
                SortingWorkspace.HasSymlinkOrReparsePoint(_expected.Path))
                throw new IOException("Pinned Linux path became linked.");
            // Compare a NEW no-follow openat descriptor with the retained one.
            int fd=LinuxOpenAt(Fd(_directories[^1].Handle),Path.GetFileName(_expected.Path),O_NOFOLLOW|O_CLOEXEC|0x800);
            if(fd<0) throw new IOException("Pinned Linux name disappeared.");
            using var name=new SafeFileHandle((IntPtr)fd,true);
            var pathState=LinuxPinnedStat(fd);
            RequireExpected(pathState);
            if(pathState!=_saved) throw new IOException("Pinned Linux pathname no longer names the observed inode.");
            CheckAlias(Path.GetDirectoryName(_expected.Path)!,Path.GetFileName(_expected.Path));
            CheckStableHash();
        }

        public void Dispose()
        {
            _stream?.Dispose();_stream=null;
            for(int i=_directories.Count-1;i>=0;i--) _directories[i].Handle.Dispose();
            _directories.Clear();
        }
    }
}
