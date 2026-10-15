namespace DiskU.Core;

/// <summary>File kinds, in the Mac's declaration order (legend ties sort by this order).</summary>
public enum Kind
{
    Folder,
    /// <summary>Mac packages (.app etc.). Windows has no packages, so the scanner never produces this.</summary>
    Bundle,
    Image,
    Video,
    Audio,
    Archive,
    Code,
    Document,
    Other,
}

/// <summary>An sRGB color as 0-255 components. No UI framework types, so the UI converts.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(uint hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    /// <summary>Gray with all components equal.</summary>
    public static Rgb Gray(byte value) => new(value, value, value);

    /// <summary>"#RRGGBB".</summary>
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Labels, colors and the extension table. Add a file type here, never with checks in views.</summary>
public static class Kinds
{
    /// <summary>Every kind, in declaration order.</summary>
    public static IReadOnlyList<Kind> All { get; } = Enum.GetValues<Kind>();

    public static string Label(this Kind kind) => Style(kind).Label;

    /// <summary>The kind's base color (the cushion's middle stop).</summary>
    public static Rgb Color(this Kind kind) => Style(kind).Color;

    /// <summary>The kind's color as the Mac defines it, as 0-1 sRGB components.</summary>
    public static (double R, double G, double B) UnitColor(this Kind kind) => Style(kind).Unit;

    private static (string Label, Rgb Color, (double, double, double) Unit) Style(Kind kind) => kind switch
    {
        // Hex values are round(component * 255) of the Mac's unit colors.
        Kind.Folder => ("Folders", Rgb.FromHex(0x6B7380), (0.42, 0.45, 0.50)),
        Kind.Bundle => ("Apps & Bundles", Rgb.FromHex(0x9E66DB), (0.62, 0.40, 0.86)),
        Kind.Image => ("Images", Rgb.FromHex(0x33B373), (0.20, 0.70, 0.45)),
        Kind.Video => ("Video", Rgb.FromHex(0xE64D4D), (0.90, 0.30, 0.30)),
        Kind.Audio => ("Audio", Rgb.FromHex(0xF29933), (0.95, 0.60, 0.20)),
        Kind.Archive => ("Archives & Disk Images", Rgb.FromHex(0xD9BF40), (0.85, 0.75, 0.25)),
        Kind.Code => ("Code & Data", Rgb.FromHex(0x40A6D9), (0.25, 0.65, 0.85)),
        Kind.Document => ("Documents", Rgb.FromHex(0x4D73E6), (0.30, 0.45, 0.90)),
        _ => ("System & Other", Rgb.FromHex(0x8C949E), (0.55, 0.58, 0.62)),
    };

    /// <summary>Kind for a file name from its extension (text after the last dot, lowercased).</summary>
    public static Kind ForFile(string name)
    {
        string ext = Extension(name);
        return ext.Length > 0 && ExtensionTable.TryGetValue(ext, out var kind) ? kind : Kind.Other;
    }

    /// <summary>Lowercased extension without the dot, or "" when there is none (also for ".bashrc" and "file.").</summary>
    public static string Extension(string name)
    {
        int slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) name = name[(slash + 1)..];
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return "";
        return name[(dot + 1)..].ToLowerInvariant();
    }

    // Verbatim from the Mac app. Windows-only types (exe, dll, ...) stay "System & Other" for parity.
    private static readonly Dictionary<string, Kind> ExtensionTable = BuildTable();

    private static Dictionary<string, Kind> BuildTable()
    {
        var groups = new (Kind Kind, string[] Extensions)[]
        {
            (Kind.Image, ["jpg", "jpeg", "png", "gif", "heic", "heif", "tif", "tiff", "bmp", "webp", "raw", "cr2", "nef", "arw", "dng", "psd", "svg", "ico", "icns"]),
            (Kind.Video, ["mov", "mp4", "m4v", "avi", "mkv", "wmv", "flv", "webm", "mpg", "mpeg", "3gp"]),
            (Kind.Audio, ["mp3", "m4a", "aac", "wav", "aiff", "aif", "flac", "ogg", "caf", "alac"]),
            (Kind.Archive, ["zip", "gz", "tgz", "bz2", "xz", "7z", "rar", "tar", "dmg", "iso", "pkg", "sparseimage", "sparsebundle", "zst", "xip"]),
            (Kind.Code, ["swift", "m", "h", "c", "cc", "cpp", "hpp", "rs", "go", "py", "js", "mjs", "ts", "tsx", "jsx", "java", "kt", "rb", "php", "sh", "json", "yaml", "yml", "toml", "xml", "plist", "sql", "sqlite", "db", "html", "css", "o", "a", "dylib", "so", "wasm", "jar", "class", "pyc"]),
            (Kind.Document, ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pages", "numbers", "key", "txt", "md", "rtf", "csv", "epub", "odt"]),
        };
        var table = new Dictionary<string, Kind>(StringComparer.Ordinal);
        foreach (var (kind, extensions) in groups)
            foreach (var ext in extensions) table[ext] = kind;
        return table;
    }
}
