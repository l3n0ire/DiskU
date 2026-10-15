using DiskU.Core;
using Microsoft.UI.Xaml;

namespace DiskU.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        // DISKU_THEME forces Light or Dark for checking both themes; it must be set before any window exists.
        if (Enum.TryParse<ApplicationTheme>(Diagnostics.Theme, ignoreCase: true, out var theme)) RequestedTheme = theme;
        InitializeComponent();
        UnhandledException += (_, e) => Diagnostics.Log($"Unhandled: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Like the Mac app: the first argument that is an existing folder is scanned at launch.
        string? folder = LaunchArguments.FolderFrom(Environment.GetCommandLineArgs().Skip(1));
        _window = new MainWindow(folder);
        _window.Activate();
    }
}
