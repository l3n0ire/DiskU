using System.Runtime.InteropServices;
using DiskU.Core;

namespace DiskU.App;

/// <summary>
/// The Scan… folder picker (Mac: NSOpenPanel): folders only, single selection, starts in the profile folder,
/// titled "Choose a folder to scan" with a "Scan" button. Uses IFileOpenDialog directly because the WinRT
/// FolderPicker can't set the title or button and fails when the app runs elevated.
/// </summary>
internal static class FolderPicker
{
    /// <summary>The chosen folder, or null when the user cancels. Modal; call on the UI thread.</summary>
    public static string? Choose(IntPtr owner)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
        try
        {
            const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800, FOS_NOCHANGEDIR = 0x8;
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
            dialog.SetTitle(Strings.PickerTitle);
            dialog.SetOkButtonLabel(Strings.PickerButton);
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(DiskStore.HomeFolder, IntPtr.Zero, ref iid, out var home) == 0 && home is not null)
            {
                dialog.SetFolder(home);
                Marshal.ReleaseComObject(home);
            }
            if (dialog.Show(owner) != 0) return null; // cancelled (HRESULT_FROM_WIN32(ERROR_CANCELLED)) or failed
            dialog.GetResult(out var item);
            try
            {
                const uint SIGDN_FILESYSPATH = 0x80058000;
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr name);
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogCoClass;

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);
        // IFileDialog
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName(out IntPtr pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        // IFileOpenDialog
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);
}
