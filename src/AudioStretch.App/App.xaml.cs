using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace AudioStretch;
public partial class App : Application
{
    private Window? _window;
    private System.Threading.Mutex? _instance;
    internal static Preferences Preferences { get; private set; } = null!;
    internal static Jupiter.UI.ThemeService Themes { get; private set; } = null!;
    internal static Jupiter.UI.AccentService Accent { get; private set; } = null!;
    public App() { InitializeComponent(); UnhandledException += (_, e) => LogError(e.Exception); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new System.Threading.Mutex(true, "Jupiter.AudioStretch", out bool first);
        if (!first) { Exit(); return; }
        SetCurrentProcessExplicitAppUserModelID("Jupiter.AudioStretch");
        Preferences = Preferences.Load(); Themes = new(Preferences); Accent = new(Preferences);
        _window = new MainWindow(); _window.Activate();
    }
    internal static void LogError(Exception e)
    {
        try { Directory.CreateDirectory(Preferences.DirectoryPath); File.AppendAllText(Path.Combine(Preferences.DirectoryPath, "errors.log"), $"{DateTimeOffset.UtcNow:O} {e}\n"); } catch { }
    }
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int SetCurrentProcessExplicitAppUserModelID(string id);
}
