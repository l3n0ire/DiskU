using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace DiskU.Core;

/// <summary>Platform actions that need no UI framework (Mac: <c>Actions</c>). Clipboard stays in the app.</summary>
public static class Shell
{
    /// <summary>"Show in File Explorer": opens Explorer with the item selected.</summary>
    public static void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();

    /// <summary>Opens the item with its default app; folders open in Explorer.</summary>
    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>Opens a web page (the update banner's release link).</summary>
    public static void OpenUrl(Uri url) =>
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();

    /// <summary>True when the process runs with an elevated administrator token.</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Starts this app again elevated (UAC prompt), passing <paramref name="folderToScan"/> as the launch
    /// argument. Returns false if the user declined; the caller exits the current instance on true.
    /// </summary>
    public static bool RestartAsAdministrator(string? folderToScan)
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return false;
        var info = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        if (folderToScan is not null) info.ArgumentList.Add(folderToScan);
        try
        {
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC declined
        {
            return false;
        }
    }
}

/// <summary>Command-line handling (Mac: launch-folder argument).</summary>
public static class LaunchArguments
{
    /// <summary>
    /// The folder to scan at launch: the first argument not starting with "-" that is an existing folder,
    /// with a leading "~" expanded to the profile and %VARIABLES% expanded. Null if there is none.
    /// </summary>
    public static string? FolderFrom(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-')) continue;
            string path = Environment.ExpandEnvironmentVariables(arg.Trim('"'));
            if (path == "~" || path.StartsWith("~\\", StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
                path = DiskStore.HomeFolder + path[1..];
            if (Directory.Exists(path)) return Path.GetFullPath(path);
            return null; // like the Mac: only the first candidate counts
        }
        return null;
    }
}
