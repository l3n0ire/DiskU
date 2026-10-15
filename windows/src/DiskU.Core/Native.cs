using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskU.Core;

/// <summary>Win32 calls used by the scanner, trasher and shell helpers.</summary>
internal static partial class Native
{
    public const uint FILE_LIST_DIRECTORY = 0x0001;
    public const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, FILE_SHARE_DELETE = 0x4;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    /// <summary>Reparse tags with this bit are name surrogates: symlinks, junctions, mount points.</summary>
    public const uint REPARSE_TAG_NAME_SURROGATE_BIT = 0x20000000;

    public const int FileIdBothDirectoryInfo = 10;
    public const int FileIdBothDirectoryRestartInfo = 11;
    public const int FileFullDirectoryInfo = 14;
    public const int FileFullDirectoryRestartInfo = 15;
    public const int FileIdExtdDirectoryInfo = 19;
    public const int FileIdExtdDirectoryRestartInfo = 20;

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_NO_MORE_FILES = 18;
    public const int ERROR_INVALID_PARAMETER = 87;
    public const int ERROR_NOT_SUPPORTED = 50;
    public const int ERROR_INVALID_LEVEL = 124;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, byte* buffer, uint size);

    // --- Recycle Bin ---

    public const uint FO_DELETE = 3;
    public const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    public static partial int SHFileOperation(ref SHFILEOPSTRUCTW op);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHQueryRecycleBin(string rootPath, ref SHQUERYRBINFO info);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* volumeName, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetVolumePathName(string fileName, char* volumePath, uint length);

    /// <summary>Path with the \\?\ prefix so Win32 accepts paths longer than MAX_PATH.</summary>
    public static string Extended(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }
}
