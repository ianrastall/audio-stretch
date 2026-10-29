using Microsoft.UI.Xaml;
using AudioStretch.Updates;

namespace AudioStretch;
public sealed partial class SettingsWindow : Window
{
    private bool _ready;
    private readonly MainWindow _main;
    private ToolCatalog _catalog = null!;
    private AppUpdate? _application;
    private CancellationTokenSource? _work;
    private bool _busy;
    internal bool IsClosed { get; private set; }
    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        App.Themes.Attach(this); App.Accent.Attach(Root);
        ThemeControl.Service = App.Themes; AccentControl.Service = App.Accent;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioStretch.ico"));
        CentreFocus.IsChecked = App.Preferences.CentreFocus;
        AutomaticChecks.IsChecked = App.Preferences.CheckUpdatesAutomatically;
        ApplicationVersion.Text = "AudioStretch " + MainWindow.CurrentVersion;
        using (var service = new UpdateService(Preferences.DirectoryPath)) _catalog = service.BundledCatalog(Path.Combine(AppContext.BaseDirectory, "tool-catalog.json"));
        _ready = true;
        Root.Loaded += async (_, _) => { Placement.Restore(this, Root, settings: true); await RunWork(RefreshVersions); };
        Closed += (_, _) => { IsClosed = true; CancelWork(); App.Themes.Detach(this); App.Accent.Detach(Root); };
    }
    private void CentreFocusChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        App.Preferences.CentreFocus = CentreFocus.IsChecked == true;
        _main.RefreshProcessingPreferences();
        try { App.Preferences.Save(); }
        catch (Exception error) { ErrorBar.Title = "Settings could not be saved"; ErrorBar.Message = error.Message; ErrorBar.IsOpen = true; }
    }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    internal void CancelWork() => _work?.Cancel();
    private void CancelUpdateClicked(object sender, RoutedEventArgs e) => CancelWork();
    private void AutomaticChecksChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        App.Preferences.CheckUpdatesAutomatically = AutomaticChecks.IsChecked == true;
        try { App.Preferences.Save(); } catch (Exception error) { SetError(error); }
    }
    private void SetError(Exception error)
    {
        if (IsClosed) return;
        ErrorBar.Title = "Update or tool setup did not complete"; ErrorBar.Message = error.Message; ErrorBar.IsOpen = true;
    }
    private async Task RunWork(Func<CancellationToken, Task> work)
    {
        if (_busy || IsClosed) return;
        _busy = true; _work = new(); SetBusy(); ErrorBar.IsOpen = false;
        try { await work(_work.Token); }
        catch (OperationCanceledException) { if (!IsClosed) UpdateStatus.Text = "Cancelled. Existing software was preserved."; }
        catch (Exception error) { SetError(error); }
        finally { _busy = false; _work.Dispose(); _work = null; if (!IsClosed) SetBusy(); }
    }
    private void SetBusy()
    {
        CheckButton.IsEnabled = RubberBandInstall.IsEnabled = FfmpegInstall.IsEnabled = !_busy;
        AppInstall.IsEnabled = !_busy && _application is not null;
        UpdateProgress.Visibility = CancelUpdate.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task RefreshVersions(CancellationToken ct)
    {
        using var service = new UpdateService(Preferences.DirectoryPath);
        foreach (var (id, file, label, button) in new[] {
            ("rubberband", "rubberband-r3.exe", RubberBandVersion, RubberBandInstall),
            ("ffmpeg", "ffmpeg.exe", FfmpegVersion, FfmpegInstall) })
        {
            var package = _catalog.Packages.Single(p => p.Id == id);
            string? path = service.ResolveTool(file, Path.Combine(AppContext.BaseDirectory, "tools"));
            string? version = path is null ? null : await UpdateService.ReadVersionAsync(path, id == "rubberband" ? "--version" : "-version", ct);
            if (IsClosed) return;
            string name = id == "rubberband" ? "Rubber Band R3" : "FFmpeg / FFprobe";
            label.Text = name + ": " + (version ?? "not installed") + " · Catalog: " + package.Version;
            button.Content = (version is null ? "Install " : UpdateService.IsNewer(package.Version, version) ? "Update " : "Reinstall ") + name + " " + package.Version;
        }
    }
    private async void CheckClicked(object sender, RoutedEventArgs e) => await RunWork(async ct =>
    {
        using var service = new UpdateService(Preferences.DirectoryPath);
        UpdateStatus.Text = "Checking application releases, tool catalog and the official Rubber Band download page…";
        var result = await service.CheckAsync(MainWindow.CurrentVersion, ct);
        if (IsClosed) return;
        _catalog = result.Catalog; _application = result.Application;
        App.Preferences.LastUpdateCheck = result.CheckedAt; App.Preferences.Save();
        await RefreshVersions(ct);
        if (IsClosed) return;
        UpdateStatus.Text = "Checked " + result.CheckedAt.ToLocalTime().ToString("g") + ". "
            + (_application is null ? "No newer AudioStretch release. " : "AudioStretch " + _application.Version + " is available. ")
            + "Latest upstream Rubber Band Windows release: " + result.RubberBandUpstream + ". Tested catalog: " + _catalog.Packages.Single(p => p.Id == "rubberband").Version + ".";
    });
    private async Task Install(string id)
    {
        await RunWork(async ct =>
        {
            var progress = new Progress<string>(s => { if (!IsClosed) UpdateStatus.Text = s; });
            await _main.InstallTool(_catalog.Packages.Single(p => p.Id == id), progress, ct);
            await RefreshVersions(ct);
            if (!IsClosed) UpdateStatus.Text = "Completed. The verified audio tool is ready for new renders.";
        });
    }
    private async void RubberBandInstallClicked(object sender, RoutedEventArgs e) => await Install("rubberband");
    private async void FfmpegInstallClicked(object sender, RoutedEventArgs e) => await Install("ffmpeg");
    private async void AppInstallClicked(object sender, RoutedEventArgs e) => await RunWork(async ct =>
    {
        var update = _application ?? throw new InvalidOperationException("Check for updates first.");
        if (!_main.CanChangeTools) throw new InvalidOperationException("Finish the current operation before updating AudioStretch.");
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog { XamlRoot = Root.XamlRoot, Title = "Install AudioStretch " + update.Version + "?", Content = "The verified installer will update AudioStretch, close this window and reopen the app. Settings and audio files are preserved.", PrimaryButtonText = "Install", CloseButtonText = "Cancel", DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close };
        App.Themes.Attach(dialog);
        try { if (await dialog.ShowAsync() != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return; }
        finally { App.Themes.Detach(dialog); }
        using var service = new UpdateService(Preferences.DirectoryPath);
        string path = await service.DownloadApplicationAsync(update, new Progress<string>(s => { if (!IsClosed) UpdateStatus.Text = s; }), ct);
        ct.ThrowIfCancellationRequested();
        _main.ApplyApplicationUpdate(path, update.Sha256);
    });
}
