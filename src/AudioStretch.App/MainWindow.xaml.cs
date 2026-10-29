using System.Globalization;
using AudioStretch.Interop;
using AudioStretch.Protocol;
using AudioStretch.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace AudioStretch;
public sealed partial class MainWindow : Window
{
    private CoreClient? _core;
    private AudioInfo? _info;
    private string? _result;
    private SettingsWindow? _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Task? _probe;
    private bool _installing;
    internal bool CanChangeTools => !_running && !_probing && !_installing && !_closing && (_mapsWindow is null || _mapsWindow.IsClosed);
    internal static string CurrentVersion => "0.3.0";
    internal static string ToolRoot => Path.Combine(Preferences.DirectoryPath, "tools");
    private bool _ready, _running, _probing, _syncing, _autoOutput = true, _allowClose, _closing;

    public MainWindow()
    {
        InitializeComponent();
        Player.SetMediaPlayer(new Windows.Media.Playback.MediaPlayer());
        App.Themes.Attach(this); App.Accent.Attach(Root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioStretch.ico"));
        InitializeProcessing();
        Format.SelectedIndex = App.Preferences.Format == "flac" ? 1 : 0;
        Precision.SelectedIndex = App.Preferences.Encoding switch { "24" => 1, "16" => 2, _ => 0 };
        UpdatePrecision();
        var shortcut = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control };
        shortcut.Invoked += (_, e) => { OpenSettings(); e.Handled = true; };
        Root.KeyboardAccelerators.Add(shortcut);
        _timer.Tick += (_, _) => Poll();
        Root.Loaded += async (_, _) =>
        {
            Placement.Restore(this, Root);
            try
            {
                _core = new CoreClient(ToolRoot);
                RefreshToolStatus();
                string result = Path.Combine(Preferences.DirectoryPath, "update-result.json");
                if (File.Exists(result))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(result));
                    MessageBar.Title = document.RootElement.GetProperty("Success").GetBoolean() ? "Update completed" : "Update did not complete";
                    MessageBar.Message = document.RootElement.GetProperty("Message").GetString(); MessageBar.IsOpen = true;
                }
            }
            catch (Exception error) { ShowError("Audio tools could not start", error); }
            _ready = true; UpdateTempo(); UpdateEnabled();
            if (App.Preferences.CheckUpdatesAutomatically && (App.Preferences.LastUpdateCheck is null || DateTimeOffset.UtcNow - App.Preferences.LastUpdateCheck > TimeSpan.FromDays(1)))
            {
                try
                {
                    using var service = new UpdateService(Preferences.DirectoryPath);
                    var check = await service.CheckAsync(CurrentVersion);
                    App.Preferences.LastUpdateCheck = check.CheckedAt; App.Preferences.Save();
                    string? rbPath = service.ResolveTool("rubberband-r3.exe", Path.Combine(AppContext.BaseDirectory, "tools"));
                    string rbVersion = rbPath is null ? "0.0" : await UpdateService.ReadVersionAsync(rbPath, "--version", CancellationToken.None);
                    if (check.Application is not null || UpdateService.IsNewer(check.RubberBandUpstream, rbVersion))
                    { MessageBar.Title = "An update is available"; MessageBar.Message = "Open File > Settings > Updates and audio tools to review and install it."; MessageBar.Severity = InfoBarSeverity.Informational; MessageBar.IsOpen = true; }
                }
                catch (Exception error) { Status.Text = "Update check failed · check Settings for a manual retry"; App.LogError(error); }
            }
        };
        Player.MediaPlayer.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() => ShowError("Playback unavailable", new InvalidOperationException(e.ErrorMessage + " Rendering is still available.")));
        AppWindow.Closing += async (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true; UpdateEnabled();
            try
            {
                if (_probe is not null) await _probe;
                if (_installing) { _settings?.CancelWork(); while (_installing) await Task.Delay(50); }
                if (_running) _core?.Cancel();
                await Task.Run(() => _core?.Dispose());
            }
            catch (Exception error) { App.LogError(error); }
            finally { DispatcherQueue.TryEnqueue(() => { _allowClose = true; Close(); }); }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_mapsWindow is { IsClosed: false }) _mapsWindow.Close();
            if (_helpWindow is { IsClosed: false }) _helpWindow.Close();
            if (_settings is { IsClosed: false }) _settings.Close();
            Player.MediaPlayer.Dispose();
            try { SavePreferences(); Placement.Save(this, Root); } catch (Exception error) { App.LogError(error); }
            App.Themes.Detach(this); App.Accent.Detach(Root);
        };
    }
    private string FormatName => Format.SelectedIndex == 1 ? "flac" : "wav";
    private string EncodingName => (Precision.SelectedItem as ComboBoxItem)?.Tag as string ?? "float";
    private void UpdatePrecision()
    {
        if (Precision is null) return;
        ((ComboBoxItem)Precision.Items[0]).IsEnabled = FormatName == "wav";
        if (FormatName == "flac" && Precision.SelectedIndex == 0) Precision.SelectedIndex = 1;
    }
    private void ExportChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        UpdatePrecision();
        if (_autoOutput) DeriveOutput();
        else if (!string.IsNullOrEmpty(OutputPath.Text))
        {
            try { _syncing = true; OutputPath.Text = Path.ChangeExtension(OutputPath.Text, FormatName); }
            catch (Exception error) { ShowError("Output path is invalid", error); }
            finally { _syncing = false; }
        }
        RebuildPlan();
    }
    private void OutputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        _autoOutput = false; RebuildPlan();
    }
    private void DeriveOutput()
    {
        if (string.IsNullOrEmpty(SourcePath.Text)) return;
        _syncing = true;
        OutputPath.Text = Path.Combine(Path.GetDirectoryName(SourcePath.Text)!, Path.GetFileNameWithoutExtension(SourcePath.Text) + "_stretched." + FormatName);
        _syncing = false;
    }
    private async void OpenClicked(object sender, RoutedEventArgs e) => await PickSource();
    private async void OpenShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; await PickSource(); }
    private async Task PickSource()
    {
        if (!_ready || !CanChangeTools) return;
        try
        {
            var picker = new FileOpenPicker();
            foreach (var ext in new[] { ".wav", ".flac", ".aiff", ".aif", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".wv", ".w64", "*" }) picker.FileTypeFilter.Add(ext);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is not null) await Load(file.Path);
        }
        catch (Exception error) { ShowError("Audio could not be opened", error); }
    }
    private async Task Load(string path)
    {
        if (!CanChangeTools || _core is null) return;
        if (!await ConfirmClearMaps()) return;
        _maps = null; _receipt = _savedJob = null; _plan = null; ReplayCommand.Text = ResultDetails.Text = "";
        _probing = true; _info = null; _result = null;
        Player.MediaPlayer.Pause(); Player.MediaPlayer.Source = null;
        Player.Visibility = PlaybackLabel.Visibility = Visibility.Collapsed;
        SourcePath.Text = path; SourceInfo.Text = "Probing audio…"; Status.Text = "Running · Probing audio";
        ProgressText.Text = "Reading audio properties…"; MessageBar.IsOpen = false; UpdateEnabled();
        _probe = LoadCore(path);
        await _probe;
    }
    private async Task LoadCore(string path)
    {
        try
        {
            _info = await _core!.ProbeAsync(path);
            SourceInfo.Text = $"{_info.Codec} · {_info.SampleRate:N0} Hz · {_info.Channels} channels · "
                + (_info.BitDepth == 0 ? "precision unspecified" : $"{_info.BitDepth}-bit") + $" · {TimeSpan.FromSeconds(_info.Duration):g}";
            Title = Path.GetFileName(path) + " - AudioStretch";
            App.Preferences.LastFolder = Path.GetDirectoryName(path);
            _autoOutput = true; DeriveOutput();
            Status.Text = "Ready"; ProgressText.Text = "Ready to export.";
            if (_info.Lossy)
            {
                MessageBar.Severity = InfoBarSeverity.Informational; MessageBar.Title = "Lossy source";
                MessageBar.Message = "WAV or FLAC export avoids another lossy encode, but cannot restore information absent from the source."; MessageBar.IsOpen = true;
            }
        }
        catch (Exception error) { SourceInfo.Text = "Audio properties unavailable"; ShowError("Audio could not be read", error); }
        finally { _probing = false; UpdateTempo(); UpdateEnabled(); }
    }
    private void RootDragOver(object sender, DragEventArgs e) { e.AcceptedOperation = !_running && !_probing && e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None; }
    private async void RootDrop(object sender, DragEventArgs e)
    {
        if (!CanChangeTools) return;
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count != 1 || items[0] is not StorageFile file) throw new InvalidOperationException("Drop one audio file at a time.");
            await Load(file.Path);
        }
        catch (Exception error) { ShowError("Audio could not be opened", error); }
    }
    private async void OutputBrowseClicked(object sender, RoutedEventArgs e)
    {
        if (!CanChangeTools) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = string.IsNullOrWhiteSpace(OutputPath.Text) ? "stretched" : Path.GetFileNameWithoutExtension(OutputPath.Text) };
            picker.FileTypeChoices.Add(FormatName.ToUpperInvariant() + " audio", new[] { "." + FormatName });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is not null) OutputPath.Text = file.Path;
        }
        catch (Exception error) { ShowError("Output location could not be selected", error); }
    }
    private async void ExportClicked(object sender, RoutedEventArgs e) => await Export();
    private async void ExportShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; await Export(); }
    private async Task Export()
    {
        if (!_ready || !CanChangeTools || _info is null || _core is null) return;
        Request request;
        try { request = BuildRequest(); _plan = _core.Plan(request.Clone()); }
        catch (Exception error) { ShowError("Render settings are invalid", error); return; }
        _running = true; UpdateEnabled(); MessageBar.IsOpen = false;
        try
        {
            string output = Path.GetFullPath(OutputPath.Text.Trim());
            if (!string.Equals(Path.GetExtension(output), "." + FormatName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The output filename must end in .{FormatName} for the selected format.");
            bool overwrite = File.Exists(output);
            if (overwrite)
            {
                var answer = await ShowDialog(new ContentDialog { Title = "Replace the existing audio file?", Content = output + "\nThe existing file will be replaced only after rendering and validation complete.", PrimaryButtonText = "Replace", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close });
                if (answer != ContentDialogResult.Primary) { _running = false; UpdateEnabled(); return; }
            }
            SavePreferences();
            // Release playback handles before a completed result could be replaced.
            Player.MediaPlayer.Pause(); Player.MediaPlayer.Source = null;
            Player.Visibility = PlaybackLabel.Visibility = Visibility.Collapsed;
            request.Output = output; request.Overwrite = overwrite;
            var snapshot = _core.Start(request);
            ApplySnapshot(snapshot); _timer.Start();
        }
        catch (Exception error) { _running = false; UpdateEnabled(); ShowError("Export could not start", error); }
    }
    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        if (!_running || _core is null || _closing) return;
        try { _core.Cancel(); CancelButton.IsEnabled = false; Status.Text = "Cancelling"; ProgressText.Text = "Cancelling · Waiting for audio tools to stop"; }
        catch (Exception error) { ShowError("Cancellation failed", error); }
    }
    private void Poll()
    {
        if (_core is null || _closing) return;
        try { ApplySnapshot(_core.Poll()); }
        catch (Exception error) { _timer.Stop(); _running = false; UpdateEnabled(); ShowError("Audio status could not be read", error); }
    }
    private void ApplySnapshot(Snapshot s)
    {
        Log.Text = s.Log;
        Status.Text = s.State;
        ProgressText.Text = $"{s.State} · {s.Stage} · {TimeSpan.FromSeconds(s.Elapsed):g} elapsed";
        if (s.State is "Completed" or "Failed" or "Cancelled")
        {
            _timer.Stop(); _running = false;
            if (s.State == "Completed") { _result = s.Output; _receipt = s.ReceiptJson; ProgressText.Text = "Completed · " + s.Output; ResultDetails.Text = $"Actual duration {s.ActualDuration:0.######}s · Difference from requested duration {s.DurationError:+0.######;-0.######;0}s"; PlanWarnings.Text = string.Join("\n", s.Warnings); }
            if (s.State == "Failed") ShowError("Export failed", new InvalidOperationException(s.Error));
            UpdateEnabled();
        }
    }
    private async Task Play(string? path, string label)
    {
        if (string.IsNullOrEmpty(path) || _closing) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            Player.MediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
            Player.Visibility = PlaybackLabel.Visibility = Visibility.Visible;
            PlaybackLabel.Text = label + ": " + Path.GetFileName(path);
            Player.MediaPlayer.Play();
        }
        catch (Exception error) { ShowError("Playback unavailable", error); }
    }
    private async void PlaySourceClicked(object sender, RoutedEventArgs e) { if (_info is not null) await Play(SourcePath.Text, "Source"); }
    private async void PlayOutputClicked(object sender, RoutedEventArgs e) { if (!_running && !_probing) await Play(_result, "Completed output"); }
    private void StopClicked(object sender, RoutedEventArgs e) { Player.MediaPlayer.Pause(); Player.MediaPlayer.Source = null; Player.Visibility = PlaybackLabel.Visibility = Visibility.Collapsed; }
    private void CopyLogClicked(object sender, RoutedEventArgs e) { var data = new DataPackage(); data.SetText(Log.Text); Clipboard.SetContent(data); }
    private void OpenSettings() { if (_settings is null || _settings.IsClosed) _settings = new SettingsWindow(this); _settings.Activate(); }
    private void SetupToolsClicked(object sender, RoutedEventArgs e) => OpenSettings();
    internal void RefreshToolStatus()
    {
        using var service = new UpdateService(Preferences.DirectoryPath);
        bool ready = new[] { "ffmpeg.exe", "ffprobe.exe", "rubberband-r3.exe", "sndfile.dll" }.All(n => service.ResolveTool(n, Path.Combine(AppContext.BaseDirectory, "tools")) is not null);
        SetupTools.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        if (!ready) { MessageBar.Severity = InfoBarSeverity.Informational; MessageBar.Title = "Audio tools need to be installed"; MessageBar.Message = "Open Settings and install FFmpeg and Rubber Band from their official download packages. Internet access is needed for setup; audio processing stays local."; MessageBar.IsOpen = true; }
        else if (MessageBar.Title == "Audio tools need to be installed") MessageBar.IsOpen = false;
    }
    internal async Task InstallTool(ToolPackage package, IProgress<string> progress, CancellationToken ct)
    {
        if (!CanChangeTools) throw new InvalidOperationException("Finish or cancel the current operation before changing audio tools.");
        _installing = true; UpdateEnabled();
        try { using var service = new UpdateService(Preferences.DirectoryPath); await service.InstallToolAsync(package, progress, ct); RefreshToolStatus(); }
        finally { _installing = false; UpdateEnabled(); }
    }
    internal void ApplyApplicationUpdate(string installer, string hash)
    {
        if (!CanChangeTools) throw new InvalidOperationException("Finish the current operation before installing an update.");
        UpdateService.LaunchApplicationInstaller(installer, hash, Path.Combine(AppContext.BaseDirectory, "AudioStretch.UpdateHost.exe"));
        Close();
    }
    private void SettingsClicked(object sender, RoutedEventArgs e) => OpenSettings();
    private void ExitClicked(object sender, RoutedEventArgs e) => Close();
    private async void AboutClicked(object sender, RoutedEventArgs e)
    {
        try { await ShowDialog(new ContentDialog { Title = "AudioStretch " + CurrentVersion, Content = "Full Rubber Band R3 frontend.\nTime, pitch, formants and dynamic maps\nRubber Band R3/Finer · FFmpeg/FFprobe\nWindows 11 x64 · Jupiter\n\nSource: github.com/ianrastall/audio-stretch\nUpdate checks and audio tool installation are available in Settings.", CloseButtonText = "Close" }); }
        catch (Exception error) { ShowError("About window could not open", error); }
    }
    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        dialog.XamlRoot = Root.XamlRoot; App.Themes.Attach(dialog);
        try { return await dialog.ShowAsync(); }
        finally { App.Themes.Detach(dialog); }
    }
    private void SavePreferences()
    {
        SaveProcessingPreferences();
        App.Preferences.Format = FormatName; App.Preferences.Encoding = EncodingName; App.Preferences.Save();
    }
    private void ShowError(string title, Exception error)
    {
        MessageBar.Title = title; MessageBar.Message = error.Message; MessageBar.Severity = InfoBarSeverity.Error; MessageBar.IsOpen = true;
        Status.Text = "Failed"; App.LogError(error);
    }
}
