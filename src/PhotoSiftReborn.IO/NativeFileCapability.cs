using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhotoSiftReborn;

/// <summary>
/// Narrow local-file ownership evidence, not universal race-proof filesystem locking.
/// Windows: BY_HANDLE_FILE_INFORMATION on a live handle. Linux: statx with NOFOLLOW;
/// statx layout is the Linux UAPI fixed 256-byte ABI. Mac: refuse destructive operations.
/// </summary>
public static partial class NativeFileCapability
{
    private const int AT_FDCWD = -100;
    private const int AT_SYMLINK_NOFOLLOW = 0x100;
    private const uint STATX_BASIC_STATS = 0x7ff;
    private const uint RENAME_NOREPLACE = 1;
    private const uint MOVEFILE_WRITE_THROUGH = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public FileTime Created, Accessed, Written;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFile, string existingFile, IntPtr securityAttributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFile, string newFile, uint flags);
    // Opening directories requires FILE_FLAG_BACKUP_SEMANTICS on Windows.
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, EntryPoint="CreateFileW")]
    private static extern SafeFileHandle OpenForAttributes(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("libc", SetLastError = true, EntryPoint="statx")]
    private static extern int Statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathname, int flags, uint mask, IntPtr buffer);
    [DllImport("libc", SetLastError = true, EntryPoint="link")]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string oldpath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newpath);
    [DllImport("libc", SetLastError = true, EntryPoint="renameat2")]
    private static extern int RenameAt2(int olddirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldpath,
        int newdirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string newpath, uint flags);

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>Device identifier for an existing ordinary file or directory. This is
    /// checked before any Move publication to refuse a cross-volume Move+Undo.</summary>
    public static string DeviceId(string path)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("Move requires a native device check.");
        // Directory ancestor validation must receive a directory, not an
        // existing regular file. Keep the file leaf's link/reparse check
        // separate; otherwise every genuine source photo is rejected as an
        // "ancestor is a file" before Move can start.
        var full = Path.GetFullPath(path);
        bool regularFile = File.Exists(full);
        var ancestorDirectory = regularFile ? Path.GetDirectoryName(full)! : full;
        if (SortingWorkspace.HasLinkedExistingAncestor(ancestorDirectory) ||
            (regularFile && SortingWorkspace.HasSymlinkOrReparsePoint(full)))
            throw new IOException("Linked directory or file in volume identity path.");
        if (OperatingSystem.IsWindows())
        {
            using var handle = OpenForAttributes(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
                throw new IOException("Cannot prove Windows volume identity: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return "win:" + info.VolumeSerial.ToString("X8");
        }
        IntPtr buffer = Marshal.AllocHGlobal(256);
        try
        {
            for (int i=0; i<256; i++) Marshal.WriteByte(buffer,i,0);
            if (Statx(AT_FDCWD,path,AT_SYMLINK_NOFOLLOW,STATX_BASIC_STATS,buffer)!=0)
                throw new IOException("Cannot prove Linux volume identity: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            int mode = Marshal.ReadInt16(buffer,0x1c) & 0xF000;
            if (mode is not (0x8000 or 0x4000))
                throw new IOException("Filesystem object is not a regular file or directory.");
            uint major = unchecked((uint)Marshal.ReadInt32(buffer,0x88));
            uint minor = unchecked((uint)Marshal.ReadInt32(buffer,0x8c));
            return $"linux:{major:X8}:{minor:X8}";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static string Identity(string path)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("No independently verified native file-identity adapter on this platform.");
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("A symbolic link is not a verified application-owned file.");
        if (OperatingSystem.IsWindows())
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!GetFileInformationByHandle(input.SafeFileHandle, out var i))
                throw new IOException("Windows file identity failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return $"win:{i.VolumeSerial:X8}:{i.IndexHigh:X8}{i.IndexLow:X8}";
        }
        // Linux UAPI statx is intentionally isolated to one 256-byte buffer.
        // Offsets 0x20 inode, 0x88 dev_major and 0x8c dev_minor, per linux/stat.h.
        IntPtr buffer = Marshal.AllocHGlobal(256);
        try
        {
            for (int i = 0; i < 256; i++) Marshal.WriteByte(buffer, i, 0);
            if (Statx(AT_FDCWD, path, AT_SYMLINK_NOFOLLOW, STATX_BASIC_STATS, buffer) != 0)
                throw new IOException("Linux statx identity failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            var mask = unchecked((uint)Marshal.ReadInt32(buffer,0));
            int mode = Marshal.ReadInt16(buffer,0x1c) & 0xF000;
            if ((mask & 0x100) == 0 || mode != 0x8000)
                throw new IOException("Linux statx did not return a regular-file inode.");
            ulong inode = unchecked((ulong)Marshal.ReadInt64(buffer,0x20));
            uint major = unchecked((uint)Marshal.ReadInt32(buffer,0x88));
            uint minor = unchecked((uint)Marshal.ReadInt32(buffer,0x8c));
            return $"linux:{major:X8}:{minor:X8}:{inode:X16}";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>
    /// Exact local regular-file proof used for Copy successor lineage:
    /// a hardlinked original or output cannot silently alias another owner's
    /// pathname. Linux statx stx_nlink @0x10; Windows BY_HANDLE link count.
    /// </summary>
    public static bool IsSingleLinkRegularFile(string path)
    {
        if (!IsSupported || !File.Exists(path) ||
            SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(path)!))
            return false;
        if (OperatingSystem.IsWindows())
        {
            using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            return GetFileInformationByHandle(input.SafeFileHandle,out var info) &&
                (info.Attributes & (0x10u|0x400u))==0 && info.LinkCount==1;
        }
        IntPtr buffer=Marshal.AllocHGlobal(256);
        try
        {
            for(int i=0;i<256;i++) Marshal.WriteByte(buffer,i,0);
            if(Statx(AT_FDCWD,path,AT_SYMLINK_NOFOLLOW,STATX_BASIC_STATS,buffer)!=0)
                return false;
            uint mask=unchecked((uint)Marshal.ReadInt32(buffer,0));
            int mode=Marshal.ReadInt16(buffer,0x1c) & 0xF000;
            uint count=unchecked((uint)Marshal.ReadInt32(buffer,0x10));
            return (mask & 0x4)!=0 && mode==0x8000 && count==1;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // R30 publication witness: exactly the two reviewed names may share the
    // temporary inode. Any third link is foreign ownership and blocks cleanup.
    internal static uint RegularFileLinkCount(string path)
    {
        if(SortingWorkspace.HasSymlinkOrReparsePoint(path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(path)!))
            throw new IOException("Linked native publication path.");
        if(OperatingSystem.IsWindows())
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(!GetFileInformationByHandle(stream.SafeFileHandle,out var info) ||
                (info.Attributes & (0x10u|0x400u))!=0)throw new IOException("Publication inode unavailable.");
            return info.LinkCount;
        }
        if(OperatingSystem.IsLinux())
        {
            int fd=LinuxOpen(path,O_NOFOLLOW|O_CLOEXEC);
            if(fd<0)throw new IOException("Publication inode unavailable.");
            using var handle=new SafeFileHandle((IntPtr)fd,true);
            LinuxStatId(fd,out int links,out int mode);
            if(mode!=0x8000)throw new IOException("Publication is not a regular file.");
            return checked((uint)links);
        }
        throw new PlatformNotSupportedException();
    }

    // Atomic no-replace publication from an already written temp file in the destination directory.
    // A hard link cannot replace an existing file. Leave the temp until journalled installation is observed.
    public static void PublishLinkNoReplace(string temp, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLink(destination,temp,IntPtr.Zero))
                throw new IOException("Atomic Windows no-replace publication failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        else if (OperatingSystem.IsLinux())
        {
            if (Link(temp,destination) != 0)
                throw new IOException("Atomic Linux no-replace publication failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        else throw new PlatformNotSupportedException("Native no-replace file installation not implemented on this platform.");
    }

    // Atomic within one filesystem, destination must not exist. No File.Move cross-volume fallback.
    public static void RelocateNoReplace(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(source,destination,MOVEFILE_WRITE_THROUGH))
                throw new IOException("Windows no-replace relocation failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        else if (OperatingSystem.IsLinux())
        {
            if (RenameAt2(AT_FDCWD,source,AT_FDCWD,destination,RENAME_NOREPLACE) != 0)
                throw new IOException("Linux renameat2 no-replace relocation failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        else throw new PlatformNotSupportedException("Native no-replace relocation not implemented here.");
    }
    // P6B-3A: source-handle and directory-handle anchored same-directory rename
    // for a cooperative local filesystem. This low-level primitive is NOT a
    // user-facing Rename authorization or durable operation on its own.
    private const uint DELETE_ACCESS = 0x00010000;
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int AT_EMPTY_PATH = 0x1000;
    private const int O_DIRECTORY = 0x10000;
    private const int O_NOFOLLOW = 0x20000;
    private const int O_CLOEXEC = 0x80000;

    // P6B-3A v0.4.6b: Win32 SetFileInformationByHandle(FileRenameInfo)
    // can reject non-NULL RootDirectory + relative FileName (WinError 87).
    // For this *fixture-only native probe*, call the NT file information
    // operation directly: it accepts a pinned directory handle and a leaf
    // filename. NtSetInformationFile is a native API, not a supported
    // Win32-app compatibility guarantee; reject NTSTATUS failures and NEVER
    // fall back to a path-based or overwrite-capable operation.
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr StatusOrPointer;
        public UIntPtr Information;
    }
    [DllImport("ntdll.dll", ExactSpelling=true, EntryPoint="NtSetInformationFile")]
    private static extern int NtSetInformationFile(SafeFileHandle file,
        out IoStatusBlock ioStatus, IntPtr data, uint size, int informationClass);
    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", SetLastError = true, EntryPoint = "openat")]
    private static extern int LinuxOpenAt(int dirfd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    private static string NativeId(ByHandleFileInformation info)
        => $"win:{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";

    private static string LinuxStatId(int fd, out int nlink, out int mode)
    {
        IntPtr buffer = Marshal.AllocHGlobal(256);
        try
        {
            for (int i=0; i<256; i++) Marshal.WriteByte(buffer,i,0);
            if (Statx(fd,"",AT_EMPTY_PATH,STATX_BASIC_STATS,buffer)!=0)
                throw new IOException("statx on pinned handle failed: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            uint mask=unchecked((uint)Marshal.ReadInt32(buffer,0));
            if ((mask & 0x100)==0)
                throw new IOException("Pinned handle did not yield native inode evidence.");
            nlink=Marshal.ReadInt32(buffer,0x10);
            mode=Marshal.ReadInt16(buffer,0x1c) & 0xF000;
            ulong inode=unchecked((ulong)Marshal.ReadInt64(buffer,0x20));
            uint major=unchecked((uint)Marshal.ReadInt32(buffer,0x88));
            uint minor=unchecked((uint)Marshal.ReadInt32(buffer,0x8c));
            return $"linux:{major:X8}:{minor:X8}:{inode:X16}";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Native identity of a directory itself, not merely its device.
    /// Do not persist this as an authorization; the caller must bind its review
    /// to this identity and check it again before filesystem mutation.</summary>
    public static string DirectoryIdentity(string folder)
    {
        var path=Path.GetFullPath(folder);
        if (SortingWorkspace.HasLinkedExistingAncestor(path))
            throw new IOException("Linked/reparse directory path not eligible for native Rename.");
        if (OperatingSystem.IsWindows())
        {
            using var dir=OpenForAttributes(path,0x80,7,IntPtr.Zero,3,
                FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,IntPtr.Zero);
            // Diagnostic-only R24-R2: preserve the exact original refusal conditions.
            // Capture GetLastWin32Error immediately at the failing Win32 boundary.
            // Never retry, fall back to path identity, or grant an operation.
            if (dir.IsInvalid)
                throw new IOException("Cannot pin a regular Windows directory identity: CreateFileW Win32="+
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            if (!GetFileInformationByHandle(dir,out var info))
                throw new IOException("Cannot pin a regular Windows directory identity: GetFileInformationByHandle Win32="+
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            if ((info.Attributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) != FILE_ATTRIBUTE_DIRECTORY)
                throw new IOException("Cannot pin a regular Windows directory identity: unexpected native attributes=0x"+
                    info.Attributes.ToString("X8"));
            return NativeId(info);
        }
        if (OperatingSystem.IsLinux())
        {
            int fd=LinuxOpen(path,O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC);
            if(fd<0) throw new IOException("Cannot open local Linux directory: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            using var dir=new SafeFileHandle((IntPtr)fd,true);
            var id=LinuxStatId(fd,out _,out int mode);
            if(mode!=0x4000) throw new IOException("Pinned object is not a directory.");
            return id;
        }
        throw new PlatformNotSupportedException("Native directory handles are supported on Windows/Linux only.");
    }

    // R30 extends the proven pinned-directory mechanism to exact, same-device
    // generated recovery relocations. Windows acts on the verified source
    // HANDLE; Linux pins both directories and rechecks the source name. Linux
    // still assumes a cooperative local filesystem, as documented below.
    internal static void RelocateNoReplaceOwnedBetweenDirectories(string source,string target,
        string identity,string hash,long length,long ticks,string sourceDirectoryId,string targetDirectoryId)
    {
        string sourceParent=Path.GetDirectoryName(source)!,targetParent=Path.GetDirectoryName(target)!;
        if(SortingWorkspace.PathComparer.Equals(sourceParent,targetParent))
        {
            RelocateNoReplaceOwned(source,target,identity,hash,length,ticks,sourceDirectoryId);return;
        }
        if(DirectoryIdentity(sourceParent)!=sourceDirectoryId || DirectoryIdentity(targetParent)!=targetDirectoryId ||
            DeviceId(sourceParent)!=DeviceId(targetParent))
            throw new IOException("Native relocation directories changed or differ in device.");
        R30Native.Owned(source,identity,hash,length,ticks);R30Native.Vacant(target);
        if(OperatingSystem.IsWindows())
        {
            using var fromDirectory=OpenForAttributes(sourceParent,0x80,7,IntPtr.Zero,3,
                FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,IntPtr.Zero);
            using var toDirectory=OpenForAttributes(targetParent,0x80,7,IntPtr.Zero,3,
                FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,IntPtr.Zero);
            if(fromDirectory.IsInvalid || toDirectory.IsInvalid ||
                !GetFileInformationByHandle(fromDirectory,out var fromInfo) ||
                !GetFileInformationByHandle(toDirectory,out var toInfo) ||
                NativeId(fromInfo)!=sourceDirectoryId || NativeId(toInfo)!=targetDirectoryId ||
                (fromInfo.Attributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))!=FILE_ATTRIBUTE_DIRECTORY ||
                (toInfo.Attributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))!=FILE_ATTRIBUTE_DIRECTORY)
                throw new IOException("Native relocation directory handles cannot be verified.");
            using var file=OpenForAttributes(source,GENERIC_READ|DELETE_ACCESS,7,IntPtr.Zero,3,
                FILE_FLAG_OPEN_REPARSE_POINT,IntPtr.Zero);
            if(file.IsInvalid || !GetFileInformationByHandle(file,out var info) ||
                NativeId(info)!=identity || info.LinkCount!=1 ||
                (info.Attributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))!=0)
                throw new IOException("Native relocation source handle changed.");
            using var input=new FileStream(file,FileAccess.Read);
            if(input.Length!=length || Convert.ToHexString(SHA256.HashData(input))!=hash ||
                new FileInfo(source).LastWriteTimeUtc.Ticks!=ticks ||
                DirectoryIdentity(sourceParent)!=sourceDirectoryId || DirectoryIdentity(targetParent)!=targetDirectoryId)
                throw new IOException("Native relocation source bytes or directories changed.");
            var chars=Path.GetFileName(target).ToCharArray();
            int rootOffset=IntPtr.Size==8?8:4,lengthOffset=IntPtr.Size==8?16:8,nameOffset=IntPtr.Size==8?20:12;
            int bytes=checked(chars.Length*2);IntPtr buffer=Marshal.AllocHGlobal(nameOffset+bytes+2);
            try
            {
                Marshal.Copy(new byte[nameOffset+bytes+2],0,buffer,nameOffset+bytes+2);
                Marshal.WriteInt32(buffer,0,0); // ReplaceIfExists FALSE
                Marshal.WriteIntPtr(buffer,rootOffset,toDirectory.DangerousGetHandle());
                Marshal.WriteInt32(buffer,lengthOffset,bytes);
                Marshal.Copy(chars,0,IntPtr.Add(buffer,nameOffset),chars.Length);
                int status=NtSetInformationFile(input.SafeFileHandle,out _,buffer,(uint)(nameOffset+bytes+2),10);
                if(status!=0)throw new IOException($"Pinned native relocation refused NTSTATUS=0x{unchecked((uint)status):X8}; no fallback.");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            if(!GetFileInformationByHandle(input.SafeFileHandle,out var after) || NativeId(after)!=identity || after.LinkCount!=1)
                throw new IOException("Native relocated handle ownership changed.");
        }
        else if(OperatingSystem.IsLinux())
        {
            int fromfd=LinuxOpen(sourceParent,O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC);
            if(fromfd<0)throw new IOException("Cannot pin relocation source directory.");
            using var fromDir=new SafeFileHandle((IntPtr)fromfd,true);
            int tofd=LinuxOpen(targetParent,O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC);
            if(tofd<0)throw new IOException("Cannot pin relocation target directory.");
            using var toDir=new SafeFileHandle((IntPtr)tofd,true);
            if(LinuxStatId(fromfd,out _,out int fromMode)!=sourceDirectoryId || fromMode!=0x4000 ||
                LinuxStatId(tofd,out _,out int toMode)!=targetDirectoryId || toMode!=0x4000)
                throw new IOException("Pinned relocation directories changed.");
            int filefd=LinuxOpenAt(fromfd,Path.GetFileName(source),O_NOFOLLOW|O_CLOEXEC);
            if(filefd<0)throw new IOException("Cannot pin relocation source.");
            using var file=new SafeFileHandle((IntPtr)filefd,true);
            if(LinuxStatId(filefd,out int links,out int mode)!=identity || links!=1 || mode!=0x8000)
                throw new IOException("Pinned relocation file identity changed.");
            using var input=new FileStream(file,FileAccess.Read);
            if(input.Length!=length || Convert.ToHexString(SHA256.HashData(input))!=hash || Identity(source)!=identity ||
                new FileInfo(source).LastWriteTimeUtc.Ticks!=ticks ||
                DirectoryIdentity(sourceParent)!=sourceDirectoryId || DirectoryIdentity(targetParent)!=targetDirectoryId)
                throw new IOException("Pinned relocation bytes/path changed.");
            if(RenameAt2(fromfd,Path.GetFileName(source),tofd,Path.GetFileName(target),RENAME_NOREPLACE)!=0)
                throw new IOException("Pinned relocation renameat2 failed: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            if(LinuxStatId(filefd,out int afterLinks,out int afterMode)!=identity || afterLinks!=1 || afterMode!=0x8000)
                throw new IOException("Relocated Linux handle changed.");
        }
        else throw new PlatformNotSupportedException();
        R30Native.Owned(target,identity,hash,length,ticks);R30Native.Vacant(source);
        if(DirectoryIdentity(sourceParent)!=sourceDirectoryId || DirectoryIdentity(targetParent)!=targetDirectoryId)
            throw new IOException("Relocation parent directory changed after mutation.");
    }

    // Used in this first step on generated bytes in ordinary *unmarked* directories.
    // It intentionally DOES NOT create an operation journal or enable the public GUI.
    // Windows NtSetInformationFile(FileRenameInformation) uses the verified
    // source file HANDLE and a pinned parent RootDirectory with a relative leaf.
    // Linux renameat2 anchors both names to a pinned parent dirfd, but remains
    // source-NAME-based: a hostile concurrent source swap is NOT fully prevented.
    public static void RelocateNoReplaceOwned(string source,string target,
        string expectedNativeId,string expectedSha256,long expectedLength,
        long expectedWriteTicks,string expectedDirectoryId)
    {
        var from=Path.GetFullPath(source);
        var to=Path.GetFullPath(target);
        var parent=Path.GetDirectoryName(from) ?? throw new IOException("Source has no parent directory.");
        if(!SortingWorkspace.PathComparer.Equals(parent,Path.GetDirectoryName(to)) ||
           SortingWorkspace.PathComparer.Equals(from,to) ||
           !Path.IsPathFullyQualified(from) || !Path.IsPathFullyQualified(to))
            throw new IOException("Native Rename must use distinct names inside one exact directory.");
        string oldName=Path.GetFileName(from), newName=Path.GetFileName(to);
        if(oldName is "." or ".." || newName is "." or ".." ||
           oldName.Length==0 || newName.Length==0 ||
           !string.Equals(Path.GetExtension(from),Path.GetExtension(to),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Ambiguous native Rename file names.");
        if(SortingWorkspace.HasLinkedExistingAncestor(parent) || SortingWorkspace.HasSymlinkOrReparsePoint(from))
            throw new IOException("Linked/reparse source or parent refused.");
        if(!String.Equals(DirectoryIdentity(parent),expectedDirectoryId,StringComparison.Ordinal))
            throw new IOException("Directory identity differs from approved snapshot.");
        if(Directory.EnumerateFileSystemEntries(parent).Any(p=>
            string.Equals(Path.GetFileName(p).Normalize(NormalizationForm.FormC),
                newName.Normalize(NormalizationForm.FormC),StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Existing target or case/Unicode alias: no overwrite.");
        if(File.Exists(to) || Directory.Exists(to)) throw new IOException("Target is occupied.");
        var snapshot=new FileInfo(from);
        if(snapshot.Length!=expectedLength || snapshot.LastWriteTimeUtc.Ticks!=expectedWriteTicks)
            throw new IOException("Source length/time changed since review.");
        if(OperatingSystem.IsWindows())
        {
            using var dir=OpenForAttributes(parent,0x80,7,IntPtr.Zero,3,
                FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,IntPtr.Zero);
            if(dir.IsInvalid || !GetFileInformationByHandle(dir,out var di) ||
                (di.Attributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) != FILE_ATTRIBUTE_DIRECTORY ||
                NativeId(di)!=expectedDirectoryId)
                throw new IOException("Failed to pin reviewed Windows directory.");
            using var sourceHandle=OpenForAttributes(from,GENERIC_READ|DELETE_ACCESS,7,IntPtr.Zero,3,
                FILE_FLAG_OPEN_REPARSE_POINT,IntPtr.Zero);
            if(sourceHandle.IsInvalid || !GetFileInformationByHandle(sourceHandle,out var si) ||
               (si.Attributes & (FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))!=0 ||
               si.LinkCount!=1 || NativeId(si)!=expectedNativeId)
                throw new IOException("Windows source handle changed, linked or unverified.");
            using var input=new FileStream(sourceHandle,FileAccess.Read);
            if(input.Length!=expectedLength ||
               !Convert.ToHexString(SHA256.HashData(input)).Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Pinned Windows source bytes differ from approved snapshot.");
            if(!String.Equals(DirectoryIdentity(parent),expectedDirectoryId,StringComparison.Ordinal))
                throw new IOException("Reviewed parent directory changed before Windows Rename.");
            // Native FILE_RENAME_INFORMATION (class 10), ReplaceIfExists=FALSE.
            // The Win32 FileRenameInfo wrapper (class 3) may reject a non-null
            // RootDirectory (ERROR_INVALID_PARAMETER/87); do not weaken the
            // pinned-root contract by swapping to a path-based Win32 call.
            // The native interface is deliberately isolated to this probe.
            int rootOffset=IntPtr.Size==8?8:4;
            int lengthOffset=IntPtr.Size==8?16:8;
            int nameOffset=IntPtr.Size==8?20:12;
            var chars=newName.ToCharArray();
            int byteCount=checked(chars.Length*2);
            IntPtr info=Marshal.AllocHGlobal(checked(nameOffset+byteCount+2));
            try
            {
                // FILE_RENAME_INFORMATION has a pointer-aligned RootDirectory.
                // Clear the entire native buffer, including x64 padding.
                Marshal.Copy(new byte[nameOffset+byteCount+2],0,info,nameOffset+byteCount+2);
                Marshal.WriteInt32(info,0,0); // ReplaceIfExists is FALSE.
                Marshal.WriteIntPtr(info,rootOffset,dir.DangerousGetHandle());
                Marshal.WriteInt32(info,lengthOffset,byteCount); // UTF-16 bytes, no NUL.
                Marshal.Copy(chars,0,IntPtr.Add(info,nameOffset),chars.Length);
                int status=NtSetInformationFile(input.SafeFileHandle,out _,info,
                    (uint)(nameOffset+byteCount+2),10); // FileRenameInformation
                if(status!=0)
                    throw new IOException($"Windows pinned-root native no-replace Rename returned NTSTATUS=0x{unchecked((uint)status):X8}; no fallback or automatic retry. Inspect paths before proceeding.");
            }
            finally { Marshal.FreeHGlobal(info); }
            if(!GetFileInformationByHandle(input.SafeFileHandle,out var after) || NativeId(after)!=expectedNativeId)
                throw new IOException("Windows source-handle identity changed after Rename. Inspect manually.");
        }
        else if(OperatingSystem.IsLinux())
        {
            int dirfd=LinuxOpen(parent,O_DIRECTORY|O_NOFOLLOW|O_CLOEXEC);
            if(dirfd<0) throw new IOException("Cannot pin Linux Rename parent: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            using var dir=new SafeFileHandle((IntPtr)dirfd,true);
            if(LinuxStatId(dirfd,out _,out int directoryMode)!=expectedDirectoryId || directoryMode!=0x4000)
                throw new IOException("Pinned Linux parent differs from approved directory.");
            int filefd=LinuxOpenAt(dirfd,oldName,O_NOFOLLOW|O_CLOEXEC);
            if(filefd<0) throw new IOException("Cannot pin source entry: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            using var file=new SafeFileHandle((IntPtr)filefd,true);
            if(LinuxStatId(filefd,out int links,out int fileMode)!=expectedNativeId ||
               links!=1 || fileMode!=0x8000)
                throw new IOException("Linux source entry changed, hardlinked or non-regular.");
            using var input=new FileStream(file,FileAccess.Read);
            if(input.Length!=expectedLength ||
               !Convert.ToHexString(SHA256.HashData(input)).Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Pinned Linux source bytes differ from approved snapshot.");
            if(!String.Equals(DirectoryIdentity(parent),expectedDirectoryId,StringComparison.Ordinal) ||
               !String.Equals(Identity(from),expectedNativeId,StringComparison.Ordinal))
                throw new IOException("Linux source path or parent changed just before Rename.");
            // No fallback to rename(), File.Move or cross-device copy+delete.
            if(RenameAt2(dirfd,oldName,dirfd,newName,RENAME_NOREPLACE)!=0)
                throw new IOException("Pinned Linux renameat2 NOREPLACE failed: "+new Win32Exception(Marshal.GetLastWin32Error()).Message);
            if(LinuxStatId(filefd,out int afterLinks,out int afterMode)!=expectedNativeId ||
               afterLinks!=1 || afterMode!=0x8000)
                throw new IOException("Linux source handle changed after Rename. Inspect manually.");
        }
        else throw new PlatformNotSupportedException("Verified Rename currently supports Windows/Linux only.");
        // Any ambiguity AFTER mutation must be escalated to a durable caller's
        // NeedsAttention state. Never auto-retry a failed/unknown rename.
        if(File.Exists(from) || !File.Exists(to) ||
           !String.Equals(DirectoryIdentity(parent),expectedDirectoryId,StringComparison.Ordinal) ||
           !String.Equals(Identity(to),expectedNativeId,StringComparison.Ordinal))
            throw new IOException("Native Rename final ownership evidence ambiguous: inspect paths manually.");
    }

}
