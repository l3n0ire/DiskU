namespace DiskU.App;

/// <summary>
/// Developer switches read from the environment. None of them changes what a user sees unless set.
/// <list type="bullet">
/// <item><c>DISKU_LOG=&lt;file&gt;</c>: appends timing lines (treemap layout and draw times, scan duration).</item>
/// <item><c>DISKU_THEME=Light|Dark</c>: forces a theme, to check both without changing the system setting.</item>
/// <item><c>DISKU_CURRENT_VERSION=x.y.z</c>: pretends to be that version for the update check, to show the banner.</item>
/// </list>
/// </summary>
internal static class Diagnostics
{
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("DISKU_LOG");
    private static readonly object Gate = new();

    public static bool Enabled => LogPath is not null;

    public static string? Theme => Environment.GetEnvironmentVariable("DISKU_THEME");

    public static string? CurrentVersionOverride => Environment.GetEnvironmentVariable("DISKU_CURRENT_VERSION");

    public static void Log(string line)
    {
        if (LogPath is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
