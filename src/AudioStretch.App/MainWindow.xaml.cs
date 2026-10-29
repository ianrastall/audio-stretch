using System.Globalization;
using AudioStretch.Interop;
using AudioStretch.Protocol;
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
    private bool _ready, _running, _probing, _syncing, _autoOutput = true, _allowClose, _closing;

    public MainWindow()
    {
        InitializeComponent();
        Player.SetMediaPlayer(new Windows.Media.Playback.MediaPlayer());
        App.Themes.Attach(this); App.Accent.Attach(Root);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioStretch.ico"));
        Tempo.Text = Math.Clamp(App.Preferences.TempoChange, -75, 300).ToString("0.######", CultureInfo.CurrentCulture);
        Format.SelectedIndex = App.Preferences.Format == "flac" ? 1 : 0;
        Precision.SelectedIndex = App.Preferences.Encoding switch { "24" => 1, "16" => 2, _ => 0 };
        UpdatePrecision();
        var shortcut = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control };
        shortcut.Invoked += (_, e) => { OpenSettings(); e.Handled = true; };
        Root.KeyboardAccelerators.Add(shortcut);
        _timer.Tick += (_, _) => Poll();
        Root.Loaded += (_, _) =>
        {
            Placement.Restore(this, Root);
            try
            {
                _core = new CoreClient(Path.Combine(AppContext.BaseDirectory, "tools"));
                foreach (var tool in new[] { "ffmpeg.exe", "ffprobe.exe", "rubberband-r3.exe", "sndfile.dll" })
                    if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", tool))) throw new IOException($"Missing {tool}. Reinstall AudioStretch to restore its audio tools.");
            }
            catch (Exception error) { ShowError("Audio tools could not start", error); }
            _ready = true; UpdateTempo(); UpdateEnabled();
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
                if (_running) _core?.Cancel();
                await Task.Run(() => _core?.Dispose());
            }
            catch (Exception error) { App.LogError(error); }
            finally { DispatcherQueue.TryEnqueue(() => { _allowClose = true; Close(); }); }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_settings is { IsClosed: false }) _settings.Close();
            Player.MediaPlayer.Dispose();
            try { SavePreferences(); Placement.Save(this, Root); } catch (Exception error) { App.LogError(error); }
            App.Themes.Detach(this); App.Accent.Detach(Root);
        };
    }
    private string FormatName => Format.SelectedIndex == 1 ? "flac" : "wav";
    private string EncodingName => (Precision.SelectedItem as ComboBoxItem)?.Tag as string ?? "float";
    private bool TryTempo(out double value)
    {
        bool valid = double.TryParse(Tempo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(Tempo.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return valid && double.IsFinite(value) && value is >= -75 and <= 300;
    }
    private void UpdateTempo()
    {
        if (!_ready) return;
        bool valid = TryTempo(out double change);
        if (valid)
        {
            double speed = 1 + change / 100;
            TempoSummary.Text = $"{speed:0.######}× speed" + (_info is null ? "" : $" · Output {TimeSpan.FromSeconds(_info.Duration / speed):g}");
            TempoError.Visibility = Visibility.Collapsed;
            _syncing = true; TempoSlider.Value = change; _syncing = false;
        }
        else TempoSummary.Text = "Enter a valid tempo change";
        UpdateEnabled();
    }
    private void TempoChanged(object sender, TextChangedEventArgs e) { if (!_syncing) UpdateTempo(); }
    private void TempoLostFocus(object sender, RoutedEventArgs e) => TempoError.Visibility = TryTempo(out _) ? Visibility.Collapsed : Visibility.Visible;
    private void SliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        Tempo.Text = e.NewValue.ToString("0.######", CultureInfo.CurrentCulture);
    }
    private void UpdateEnabled()
    {
        if (!_ready) return;
        bool idle = !_running && !_probing && !_closing;
        OpenButton.IsEnabled = idle;
        Tempo.IsEnabled = TempoSlider.IsEnabled = Format.IsEnabled = Precision.IsEnabled = OutputPath.IsEnabled = OutputBrowse.IsEnabled = idle;
        ExportButton.IsEnabled = idle && _core is not null && _info is not null && TryTempo(out _) && !string.IsNullOrWhiteSpace(OutputPath.Text);
        CancelButton.IsEnabled = _running && !_closing;
        PlaySource.IsEnabled = _info is not null && !_closing;
        PlayOutput.IsEnabled = _result is not null && !_closing;
        Progress.Visibility = _running || _probing ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdatePrecision()
    {
        if (Precision is null) return;
        ((ComboBoxItem)Precision.Items[0]).IsEnabled = FormatName == "wav";
        if (FormatName == "flac" && Precision.SelectedIndex == 0) Precision.SelectedIndex = 1;
    }
    private void ExportChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        UpdatePrecision();
        if (_autoOutput) DeriveOutput();
        else if (!string.IsNullOrEmpty(OutputPath.Text))
        {
            try { _syncing = true; OutputPath.Text = Path.ChangeExtension(OutputPath.Text, FormatName); }
            catch (Exception error) { ShowError("Output path is invalid", error); }
            finally { _syncing = false; }
        }
        UpdateEnabled();
    }
    private void OutputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        _autoOutput = false; UpdateEnabled();
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
        if (!_ready || _running || _probing || _closing) return;
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
        if (_running || _probing || _closing || _core is null) return;
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
        if (_running || _probing || _closing) return;
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
        if (_running || _probing || _closing) return;
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
        if (!_ready || _running || _probing || _closing || _info is null || _core is null) return;
        if (!TryTempo(out double tempo)) { TempoLostFocus(Tempo, new RoutedEventArgs()); return; }
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
            var snapshot = _core.Start(new Request { Input = SourcePath.Text, Output = output, TempoChange = tempo, Encoding = EncodingName, Overwrite = overwrite, CentreFocus = App.Preferences.CentreFocus });
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
            if (s.State == "Completed") { _result = s.Output; ProgressText.Text = "Completed · " + s.Output; }
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
    private async void PlayOutputClicked(object sender, RoutedEventArgs e) => await Play(_result, "Completed output");
    private void StopClicked(object sender, RoutedEventArgs e) { Player.MediaPlayer.Pause(); Player.MediaPlayer.Source = null; Player.Visibility = PlaybackLabel.Visibility = Visibility.Collapsed; }
    private void CopyLogClicked(object sender, RoutedEventArgs e) { var data = new DataPackage(); data.SetText(Log.Text); Clipboard.SetContent(data); }
    private void OpenSettings() { if (_settings is null || _settings.IsClosed) _settings = new SettingsWindow(); _settings.Activate(); }
    private void SettingsClicked(object sender, RoutedEventArgs e) => OpenSettings();
    private void ExitClicked(object sender, RoutedEventArgs e) => Close();
    private async void AboutClicked(object sender, RoutedEventArgs e)
    {
        try { await ShowDialog(new ContentDialog { Title = "AudioStretch 0.2.0", Content = "Tempo adjustment with pitch preserved.\nRubber Band R3/Finer · FFmpeg/FFprobe\nWindows 11 x64 · Jupiter\n\nLocal evaluation build. Source: github.com/ianrastall/audio-stretch\nThird-party notices are included in the installation folder.", CloseButtonText = "Close" }); }
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
        if (TryTempo(out var tempo)) App.Preferences.TempoChange = tempo;
        App.Preferences.Format = FormatName; App.Preferences.Encoding = EncodingName; App.Preferences.Save();
    }
    private void ShowError(string title, Exception error)
    {
        MessageBar.Title = title; MessageBar.Message = error.Message; MessageBar.Severity = InfoBarSeverity.Error; MessageBar.IsOpen = true;
        Status.Text = "Failed"; App.LogError(error);
    }
}
