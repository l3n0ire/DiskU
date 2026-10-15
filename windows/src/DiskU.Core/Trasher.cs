using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskU.Core;

/// <summary>Moves an item to the Recycle Bin. Throws with a user-readable message when it can't.</summary>
public interface ITrasher
{
    void Trash(string path);
}

/// <summary>Wraps a delegate; tests use it to record or refuse instead of touching files.</summary>
public sealed class DelegateTrasher(Action<string> trash) : ITrasher
{
    public void Trash(string path) => trash(path);
}

/// <summary>
/// Moves items to the Recycle Bin with SHFileOperationW (FOF_ALLOWUNDO). Never deletes permanently:
/// it refuses drives without a Recycle Bin (removable, network, or set to delete immediately), and
/// FOF_WANTNUKEWARNING makes Windows ask before it would ever destroy an item instead of recycling it.
/// </summary>
public sealed class RecycleBinTrasher : ITrasher
{
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    public void Trash(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!File.Exists(full) && !Directory.Exists(full)) throw new FileNotFoundException(Strings.FolderNotFound(full), full);
        if (!HasRecycleBin(full)) throw new IOException(Strings.NoRecycleBin(full));

        IntPtr from = Marshal.StringToHGlobalUni(full + "\0"); // double-null-terminated list
        try
        {
            var op = new Native.SHFILEOPSTRUCTW
            {
                wFunc = Native.FO_DELETE,
                pFrom = from,
                fFlags = (ushort)(Native.FOF_ALLOWUNDO | Native.FOF_NOCONFIRMATION | Native.FOF_SILENT | Native.FOF_NOERRORUI | FOF_WANTNUKEWARNING),
            };
            int result = Native.SHFileOperation(ref op);
            if (op.fAnyOperationsAborted != 0) throw new IOException(Strings.TrashAborted);
            if (result != 0) throw new IOException($"{Strings.TrashFailed} (0x{result:X})");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException(Strings.TrashFailed);
    }

    /// <summary>True when items on this path's volume go to a Recycle Bin rather than being deleted.</summary>
    public static unsafe bool HasRecycleBin(string path)
    {
        char* volume = stackalloc char[1024];
        if (!Native.GetVolumePathName(path, volume, 1024)) return false;
        string root = new(volume);
        try
        {
            if (new DriveInfo(root).DriveType != DriveType.Fixed) return false;
        }
        catch (ArgumentException)
        {
            return false; // a UNC share or a folder mount point that DriveInfo doesn't understand
        }
        var info = new Native.SHQUERYRBINFO { cbSize = (uint)sizeof(Native.SHQUERYRBINFO) };
        if (Native.SHQueryRecycleBin(root, ref info) != 0) return false;
        if (PolicyDisablesRecycling()) return false;

        char* guid = stackalloc char[64];
        if (Native.GetVolumeNameForVolumeMountPoint(root, guid, 64))
        {
            // \\?\Volume{GUID}\ → {GUID}
            string name = new(guid);
            int open = name.IndexOf('{'), close = name.IndexOf('}');
            if (open >= 0 && close > open)
            {
                string key = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{name[open..(close + 1)]}";
                using var volumeKey = Registry.CurrentUser.OpenSubKey(key);
                if (volumeKey?.GetValue("NukeOnDelete") is int nuke && nuke != 0) return false;
            }
        }
        return true;
    }

    private static bool PolicyDisablesRecycling()
    {
        const string policies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(policies);
            if (key?.GetValue("NoRecycleFiles") is int value && value != 0) return true;
        }
        return false;
    }
}
