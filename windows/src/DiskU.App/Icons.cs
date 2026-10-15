using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using DiskU.Core;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DiskU.App;

/// <summary>
/// Shell icons (Mac: <c>Icons</c>). Outline rows share one icon per extension (or one folder icon); the scan
/// root and its direct children get their real icon, as on the Mac. Breadcrumbs and the inspector load the
/// real icon (or thumbnail) of the path off the UI thread. Everything is cached and UI-thread only.
/// </summary>
internal static class Icons
{
    private const string FolderKey = "/folder";
    private static readonly Dictionary<string, ImageSource?> Shared = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ImageSource?> ByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<ImageSource?>> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon for an outline row. Cheap: a dictionary lookup after the first row of each type.</summary>
    public static ImageSource? Outline(FileNode node, FileNode? root)
    {
        if (root is not null && (ReferenceEquals(node, root) || ReferenceEquals(node.Parent, root)))
        {
            string path = node.Path;
            if (ByPath.TryGetValue(path, out var cached)) return cached;
            var icon = FromFileInfo(path, useAttributes: false, node.IsFolder);
            ByPath[path] = icon;
            return icon;
        }
        string key = node.IsFolder ? FolderKey : Kinds.Extension(node.Name);
        if (Shared.TryGetValue(key, out var shared)) return shared;
        var created = FromFileInfo(node.IsFolder ? "folder" : "file." + key, useAttributes: true, node.IsFolder);
        Shared[key] = created;
        return created;
    }

    /// <summary>The real icon or thumbnail of a path at <paramref name="pixels"/> square, loaded in the background.</summary>
    public static Task<ImageSource?> ForPathAsync(string path, int pixels)
    {
        string key = $"{pixels}|{path}";
        if (Pending.TryGetValue(key, out var task)) return task;
        task = LoadAsync(path, pixels);
        Pending[key] = task;
        if (Pending.Count > 256) Pending.Clear(); // a long session can visit many items; keep memory bounded
        return task;
    }

    private static async Task<ImageSource?> LoadAsync(string path, int pixels)
    {
        var bitmap = await Task.Run(() => Native.ShellImage(path, pixels));
        return bitmap is null ? null : ToImage(bitmap.Value.Pixels, bitmap.Value.Width, bitmap.Value.Height);
    }

    private static ImageSource? FromFileInfo(string path, bool useAttributes, bool folder)
    {
        var bitmap = Native.FileInfoIcon(path, useAttributes, folder);
        return bitmap is null ? null : ToImage(bitmap.Value.Pixels, bitmap.Value.Width, bitmap.Value.Height);
    }

    private static WriteableBitmap ToImage(byte[] premultipliedBgra, int width, int height)
    {
        var image = new WriteableBitmap(width, height);
        using (var stream = image.PixelBuffer.AsStream()) stream.Write(premultipliedBgra, 0, premultipliedBgra.Length);
        image.Invalidate();
        return image;
    }

    private static class Native
    {
        public readonly record struct Bitmap(byte[] Pixels, int Width, int Height);

        public static Bitmap? FileInfoIcon(string path, bool useAttributes, bool folder)
        {
            const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
            const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;
            var info = new SHFILEINFOW();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON | (useAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
            uint attributes = folder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (SHGetFileInfoW(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;
            try { return FromIcon(info.hIcon); }
            finally { DestroyIcon(info.hIcon); }
        }

        public static Bitmap? ShellImage(string path, int pixels)
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null) return null;
            try
            {
                const int SIIGBF_BIGGERSIZEOK = 0x1;
                if (factory.GetImage(new SIZE { cx = pixels, cy = pixels }, SIIGBF_BIGGERSIZEOK, out var hbitmap) != 0 || hbitmap == IntPtr.Zero) return null;
                try { return FromBitmap(hbitmap, IntPtr.Zero, alreadyPremultiplied: true); }
                finally { DeleteObject(hbitmap); }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }

        private static Bitmap? FromIcon(IntPtr icon)
        {
            if (!GetIconInfo(icon, out var info)) return null;
            try { return FromBitmap(info.hbmColor, info.hbmMask, alreadyPremultiplied: false); }
            finally
            {
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            }
        }

        /// <summary>Reads a 32-bit top-down BGRA copy of a bitmap and premultiplies it for WriteableBitmap.</summary>
        private static Bitmap? FromBitmap(IntPtr color, IntPtr mask, bool alreadyPremultiplied)
        {
            if (color == IntPtr.Zero) return null;
            var bm = new BITMAP();
            if (GetObjectW(color, Marshal.SizeOf<BITMAP>(), ref bm) == 0) return null;
            int width = bm.bmWidth, height = Math.Abs(bm.bmHeight);
            if (width <= 0 || height <= 0) return null;
            var pixels = new byte[width * height * 4];
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                var header = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
                if (GetDIBits(dc, color, 0, (uint)height, pixels, ref header, 0) == 0) return null;
                bool hasAlpha = false;
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
                if (!hasAlpha)
                {
                    // Old-style icon without an alpha channel: take opacity from the AND mask.
                    var maskBits = new byte[width * height * 4];
                    var maskHeader = header;
                    bool haveMask = mask != IntPtr.Zero && GetDIBits(dc, mask, 0, (uint)height, maskBits, ref maskHeader, 0) != 0;
                    for (int i = 0; i < pixels.Length; i += 4) pixels[i + 3] = haveMask && maskBits[i] != 0 ? (byte)0 : (byte)255;
                    alreadyPremultiplied = false;
                }
                if (!alreadyPremultiplied)
                {
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        int a = pixels[i + 3];
                        if (a == 255) continue;
                        pixels[i] = (byte)(pixels[i] * a / 255);
                        pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                        pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                    }
                }
                return new Bitmap(pixels, width, height);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFOW
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
            // Room for the three color masks GetDIBits may write for BI_BITFIELDS.
            public uint mask0, mask1, mask2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
        [DllImport("gdi32.dll")] private static extern int GetObjectW(IntPtr h, int c, ref BITMAP pv);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);
    }
}
