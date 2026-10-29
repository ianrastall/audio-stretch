using AudioStretch.Protocol;
using Google.Protobuf;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace AudioStretch;
public sealed partial class MainWindow
{
    private RenderPlan? _plan;
    private Maps? _maps;
    private MapsWindow? _mapsWindow;
    private HelpWindow? _helpWindow;
    private string? _receipt, _savedJob;
    private int _timeMode, _pitchMode;
    private bool _requestedRealtime, _rebuilding, _showValidation;
    internal CoreClientFacade CoreAccess => new(this);
    internal AudioInfo CurrentSource => _info?.Clone() ?? throw new InvalidOperationException("Open audio first.");
    internal Maps CurrentMaps => _maps?.Clone() ?? BoundMaps(CurrentSource);
    internal static Maps BoundMaps(AudioInfo info) => new() { SampleRate = info.SampleRate, SourceFrames = info.FrameCount, SourceSha256 = info.Sha256, PitchKind = MapKind.Pitch };

    // Windows presentation code forwards all map parsing/validation and transformation rules to Rust.
    internal readonly struct CoreClientFacade(MainWindow window)
    {
        internal Reply Call(Request request) => window._core?.Call(request) ?? throw new InvalidOperationException("Audio core unavailable.");
    }
    private void InitializeProcessing()
    {
        Transform value = new() { TimeValue = App.Preferences.TempoChange, SourceBpm = 120, TargetBpm = 120 };
        if (!string.IsNullOrWhiteSpace(App.Preferences.LastTransformJson))
            try { value = Transform.Parser.ParseJson(App.Preferences.LastTransformJson); } catch (Exception error) { App.LogError(error); }
        WriteTransform(value);
        CentreFocus.IsChecked = App.Preferences.CentreFocus;
        Root.LostFocus += (_, _) => { if (_ready && !_syncing && !_rebuilding) { _showValidation = true; RebuildPlan(); } };
    }
    private static string Numeric(double value) => value.ToString("G17", CultureInfo.CurrentCulture);
    private static double Number(string text, string label)
    {
        if (!(double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) || !double.IsFinite(value))
            throw new InvalidOperationException(label + " must be a finite number.");
        return value;
    }
    private double TimeNumber(int mode)
    {
        if (mode == (int)TimeMode.Duration && Tempo.Text.Contains(':'))
        {
            var parts = Tempo.Text.Split(':').Select(p => Number(p, "Duration component")).ToArray();
            if (parts.Length is < 2 or > 3 || parts.Any(p => p < 0) || parts[^1] >= 60 || (parts.Length == 3 && parts[1] >= 60)) throw new InvalidOperationException("Use seconds, m:ss or h:mm:ss for the target duration.");
            return parts.Length == 2 ? parts[0] * 60 + parts[1] : parts[0] * 3600 + parts[1] * 60 + parts[2];
        }
        return Number(Tempo.Text, "Time value");
    }
    private Request BuildRequest(Maps? mapsOverride = null, int? timeMode = null, int? pitchMode = null)
    {
        int time = timeMode ?? TimeModeChoice.SelectedIndex, pitch = pitchMode ?? PitchModeChoice.SelectedIndex;
        var maps = mapsOverride ?? _maps;
        bool pitchMap = maps?.PitchPoints.Count > 0, timeMap = maps?.TimePoints.Count > 0;
        var transform = new Transform {
            TimeMode = (TimeMode)time, TimeValue = time == (int)TimeMode.Bpm ? 0 : TimeNumber(time),
            SourceBpm = time == (int)TimeMode.Bpm ? Number(BpmFrom.Text, "Source BPM") : 120,
            TargetBpm = time == (int)TimeMode.Bpm ? Number(BpmTo.Text, "Target BPM") : 120,
            PitchMode = (PitchMode)pitch, PitchValue = pitch == 0 ? 0 : Number(PitchValue.Text, "Pitch value"),
            PreserveFormants = Formant.IsChecked == true && (pitch != 0 || pitchMap),
            Realtime = _requestedRealtime, ShortWindow = WindowChoice.SelectedIndex == 1,
            PitchHq = HqPitch.IsChecked == true && _requestedRealtime && !pitchMap && !timeMap,
            Clamp = ClippingChoice.SelectedIndex == 1, Debug = (uint)Math.Max(0, DebugLevel.SelectedIndex),
            DiagnosticTicks = DiagnosticRender.IsChecked == true, Quiet = QuietMode.IsChecked == true
        };
        return new Request { Input = SourcePath.Text, Output = string.IsNullOrWhiteSpace(OutputPath.Text) ? "" : Path.GetFullPath(OutputPath.Text.Trim()),
            Encoding = EncodingName, CentreFocus = App.Preferences.CentreFocus, SourceInfo = _info?.Clone(), Transform = transform, Maps = maps?.Clone() };
    }
    internal Request RequestForMaps(Maps maps)
    {
        var request = BuildRequest(maps);
        if (maps.TimePoints.Count > 0) { request.Transform.Realtime = false; request.Transform.PitchHq = false; }
        return request;
    }
    internal void ApplyMaps(Maps maps)
    {
        var request = RequestForMaps(maps);
        _ = _core!.Plan(request);
        _maps = maps.Clone();
        if (maps.TimePoints.Count > 0) _requestedRealtime = false;
        RebuildPlan();
    }
    private void WriteTransform(Transform value)
    {
        _syncing = true;
        _timeMode = Math.Clamp((int)value.TimeMode, 0, 4); TimeModeChoice.SelectedIndex = _timeMode;
        _pitchMode = Math.Clamp((int)value.PitchMode, 0, 3); PitchModeChoice.SelectedIndex = _pitchMode;
        Tempo.Text = Numeric(value.TimeValue); BpmFrom.Text = Numeric(value.SourceBpm > 0 ? value.SourceBpm : 120); BpmTo.Text = Numeric(value.TargetBpm > 0 ? value.TargetBpm : 120);
        PitchValue.Text = Numeric(value.PitchValue); Formant.IsChecked = value.PreserveFormants;
        _requestedRealtime = value.Realtime; EngineRealtime.IsChecked = value.Realtime;
        WindowChoice.SelectedIndex = value.ShortWindow ? 1 : 0; HqPitch.IsChecked = value.PitchHq;
        ClippingChoice.SelectedIndex = value.Clamp ? 1 : 0; DiagnosticRender.IsChecked = value.DiagnosticTicks;
        DebugTicksItem.IsEnabled = value.DiagnosticTicks; DebugLevel.SelectedIndex = Math.Clamp((int)value.Debug, 0, 3); QuietMode.IsChecked = value.Quiet;
        _syncing = false;
    }
    private void TimeModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        int next = TimeModeChoice.SelectedIndex;
        try
        {
            if (_info is not null && _core is not null)
            {
                var request = BuildRequest(timeMode: _timeMode); request.Operation = "convert_time"; request.TargetTimeMode = (TimeMode)next;
                WriteTransform(_core.Call(request).Transform);
            }
            else { _syncing = true; Tempo.Text = next == 0 ? "0" : "1"; _timeMode = next; _syncing = false; }
        }
        catch (Exception) { _timeMode = next; }
        RebuildPlan();
    }
    private void PitchModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        int next = PitchModeChoice.SelectedIndex;
        try
        {
            if (_info is not null && _core is not null)
            {
                var request = BuildRequest(pitchMode: _pitchMode); request.Operation = "convert_pitch"; request.TargetPitchMode = (PitchMode)next;
                WriteTransform(_core.Call(request).Transform);
            }
            else { _syncing = true; PitchValue.Text = next == 3 ? "1" : "0"; _pitchMode = next; _syncing = false; }
        }
        catch (Exception) { _pitchMode = next; }
        RebuildPlan();
    }
    private void UpdateTempo() => RebuildPlan();
    private void TempoChanged(object sender, TextChangedEventArgs e) { if (!_syncing) RebuildPlan(); }
    private void TempoLostFocus(object sender, RoutedEventArgs e) { _showValidation = true; RebuildPlan(); }
    private void ProcessingTextChanged(object sender, TextChangedEventArgs e) { if (!_syncing) RebuildPlan(); }
    private void ProcessingChanged(object sender, RoutedEventArgs e) { if (!_syncing) RebuildPlan(); }
    private void ProcessingSelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_syncing) RebuildPlan(); }
    private void RealtimeChanged(object sender, RoutedEventArgs e) { if (!_ready || _syncing) return; _requestedRealtime = EngineRealtime.IsChecked == true; RebuildPlan(); }
    private void CentreFocusChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncing) return;
        App.Preferences.CentreFocus = CentreFocus.IsChecked == true; RebuildPlan();
    }
    internal void RefreshProcessingPreferences() { _syncing = true; CentreFocus.IsChecked = App.Preferences.CentreFocus; _syncing = false; RebuildPlan(); }
    private void DiagnosticChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncing) return;
        _syncing = true; DebugTicksItem.IsEnabled = DiagnosticRender.IsChecked == true;
        if (!DebugTicksItem.IsEnabled && DebugLevel.SelectedIndex == 3) DebugLevel.SelectedIndex = 0;
        _syncing = false; RebuildPlan();
    }
    private void SliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _syncing) return; Tempo.Text = e.NewValue.ToString("0.######", CultureInfo.CurrentCulture);
    }
    private void RebuildPlan()
    {
        if (!_ready || _syncing || _rebuilding) return;
        _rebuilding = true;
        try
        {
            _syncing = true;
            bool bpm = TimeModeChoice.SelectedIndex == 2;
            BpmFields.Visibility = bpm ? Visibility.Visible : Visibility.Collapsed;
            TimeValuePanel.Visibility = bpm ? Visibility.Collapsed : Visibility.Visible;
            Tempo.Header = TimeModeChoice.SelectedIndex switch { 0 => "Tempo (%)", 1 => "Speed multiplier", 3 => "Duration multiplier", 4 => "Seconds, m:ss or h:mm:ss", _ => "Time value" };
            TempoSlider.Visibility = TimeModeChoice.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (TimeModeChoice.SelectedIndex == 0 && (double.TryParse(Tempo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double change) || double.TryParse(Tempo.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out change)) && double.IsFinite(change)) TempoSlider.Value = Math.Clamp(change, -75, 300);
            PitchValue.Header = PitchModeChoice.SelectedIndex switch { 1 => "Semitones", 2 => "Cents", 3 => "Frequency multiplier", _ => "Pitch unchanged" };
            EngineRealtime.IsChecked = _requestedRealtime || _maps?.PitchPoints.Count > 0;
            _syncing = false;
            _plan = null;
            MapSummary.Text = _maps?.TimePoints.Count > 0 ? $"Time map · {_maps.TimePoints.Count} anchors · Offline" : _maps?.PitchPoints.Count > 0 ? $"{(_maps.PitchKind == MapKind.Pitch ? "Pitch" : "Frequency")} map · {_maps.PitchPoints.Count} points · Real-time engine" : "No dynamic maps";
            if (_info is null || _core is null) { TempoSummary.Text = "R3/Finer · Open audio to calculate the render plan."; TempoError.Visibility = Visibility.Collapsed; CommandPreview.Text = ""; return; }
            try
            {
                _plan = _core.Plan(BuildRequest()); TempoSummary.Text = _plan.Summary;
                PlanWarnings.Text = string.Join("\n", _plan.Warnings); TempoError.Visibility = Visibility.Collapsed;
                var arguments = _plan.Options.ToList();
                if (_plan.TimeMapText.Length > 0) arguments.AddRange(["--timemap", "time-map.txt"]);
                if (_plan.PitchMapText.Length > 0) arguments.AddRange([_plan.PitchKind == MapKind.FrequencyMap ? "--freqmap" : "--pitchmap", "pitch-map.txt"]);
                arguments.AddRange(["decoded.wav", "stretched.wav"]);
                CommandPreview.Text = "& 'rubberband-r3.exe' " + string.Join(" ", arguments.Select(Quote));
            }
            catch (Exception error)
            {
                TempoSummary.Text = "Render settings need attention"; TempoError.Text = "Error: " + error.Message;
                TempoError.Visibility = _showValidation ? Visibility.Visible : Visibility.Collapsed; CommandPreview.Text = "";
            }
        }
        finally { _syncing = false; _rebuilding = false; UpdateEnabled(); }
    }
    private void UpdateEnabled()
    {
        if (!_ready) return;
        bool idle = CanChangeTools;
        foreach (Control control in new Control[] { OpenButton, Tempo, TempoSlider, TimeModeChoice, BpmFrom, BpmTo, PitchModeChoice, PitchValue, WindowChoice, ClippingChoice, DebugLevel, DiagnosticRender, QuietMode, Format, Precision, OutputPath, OutputBrowse, CentreFocus }) control.IsEnabled = idle;
        PitchValue.IsEnabled = idle && PitchModeChoice.SelectedIndex != 0;
        Formant.IsEnabled = idle && (PitchModeChoice.SelectedIndex != 0 || _maps?.PitchPoints.Count > 0);
        EngineRealtime.IsEnabled = idle && !(_maps?.TimePoints.Count > 0 || _maps?.PitchPoints.Count > 0);
        HqPitch.IsEnabled = idle && _requestedRealtime && !(_maps?.PitchPoints.Count > 0 || _maps?.TimePoints.Count > 0);
        ExportButton.IsEnabled = JobExport.IsEnabled = idle && _core is not null && _info is not null && _plan is not null && !string.IsNullOrWhiteSpace(OutputPath.Text);
        MapsButton.IsEnabled = idle && _info is not null;
        ClearMaps.IsEnabled = idle && (_maps?.TimePoints.Count > 0 || _maps?.PitchPoints.Count > 0);
        ScriptExport.IsEnabled = idle && _savedJob is not null;
        ReceiptExport.IsEnabled = idle && !string.IsNullOrEmpty(_receipt);
        CancelButton.IsEnabled = _running && !_closing;
        PlaySource.IsEnabled = _info is not null && !_closing;
        PlayOutput.IsEnabled = _result is not null && !_running && !_closing;
        Progress.Visibility = _running || _probing ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SaveProcessingPreferences()
    {
        try { var value = BuildRequest().Transform; App.Preferences.LastTransformJson = JsonFormatter.Default.Format(value); if (value.TimeMode == TimeMode.TempoChange) App.Preferences.TempoChange = value.TimeValue; }
        catch (Exception) { /* Leave the prior valid setting when a field is incomplete. */ }
    }
    private async Task<bool> ConfirmClearMaps()
    {
        if (!(_maps?.TimePoints.Count > 0 || _maps?.PitchPoints.Count > 0)) return true;
        return await ShowDialog(new ContentDialog { Title = "Replace the source and clear its maps?", Content = "Map files are preserved. Active maps are bound to the current source and will be cleared from this session.", PrimaryButtonText = "Replace source", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary;
    }
    private async void ClearMapsClicked(object sender, RoutedEventArgs e)
    {
        if (!CanChangeTools || _maps is null || (_maps.TimePoints.Count == 0 && _maps.PitchPoints.Count == 0)) return;
        if (await ShowDialog(new ContentDialog { Title = "Clear the active maps?", Content = "Saved map files are preserved.", PrimaryButtonText = "Clear maps", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary) { _maps = null; RebuildPlan(); }
    }
    private void EditMapsClicked(object sender, RoutedEventArgs e)
    {
        if (_mapsWindow is { IsClosed: false }) { _mapsWindow.Activate(); return; }
        if (!CanChangeTools || _info is null || _core is null) return;
        _mapsWindow = new MapsWindow(this); _mapsWindow.Closed += (_, _) => { RebuildPlan(); }; _mapsWindow.Activate(); UpdateEnabled();
    }
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    private void CopyCommandClicked(object sender, RoutedEventArgs e) => CopyText(CommandPreview.Text);
    private void CopyReplayClicked(object sender, RoutedEventArgs e) => CopyText(ReplayCommand.Text);
    private static void CopyText(string value) { if (value.Length == 0) return; var content = new DataPackage(); content.SetText(value); Clipboard.SetContent(content); }
    internal async Task<string?> SaveText(string title, string suggestedName, string extension, string content, Window? owner = null)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName }; picker.FileTypeChoices.Add(title, new[] { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner ?? this));
        var file = await picker.PickSaveFileAsync(); if (file is null) return null;
        _core!.Call(new Request { Operation = "validate_artifact", Input = SourcePath.Text, Output = OutputPath.Text, ArtifactPath = file.Path });
        string temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, content); File.Move(temporary, file.Path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return file.Path;
    }
    private async void ExportJobClicked(object sender, RoutedEventArgs e)
    {
        if (!CanChangeTools || _info is null || _core is null) return;
        try
        {
            string json = _core.ExportJob(BuildRequest());
            var path = await SaveText("AudioStretch render job", Path.GetFileNameWithoutExtension(SourcePath.Text) + ".audiostretch", ".json", json);
            if (path is null) return;
            _savedJob = path; ReplayCommand.Text = "& " + Quote(Path.Combine(AppContext.BaseDirectory, "audio-stretch-cli.exe")) + " --tools " + Quote(ToolRoot) + " --job " + Quote(path);
            Status.Text = "Render job saved"; UpdateEnabled();
        }
        catch (Exception error) { ShowError("Render job could not be exported", error); }
    }
    private async void SaveScriptClicked(object sender, RoutedEventArgs e)
    {
        if (!CanChangeTools || _savedJob is null) return;
        try { await SaveText("PowerShell replay script", Path.GetFileNameWithoutExtension(_savedJob) + "-replay", ".ps1", "$ErrorActionPreference = 'Stop'\n" + ReplayCommand.Text + "\nif ($LASTEXITCODE) { throw 'AudioStretch render failed.' }\n"); }
        catch (Exception error) { ShowError("Replay script could not be saved", error); }
    }
    private async void SaveReceiptClicked(object sender, RoutedEventArgs e)
    {
        if (!CanChangeTools || string.IsNullOrEmpty(_receipt)) return;
        try { await SaveText("AudioStretch render receipt", Path.GetFileNameWithoutExtension(_result ?? SourcePath.Text) + ".render-receipt", ".json", _receipt); }
        catch (Exception error) { ShowError("Render receipt could not be saved", error); }
    }
    private async void ImportJobClicked(object sender, RoutedEventArgs e)
    {
        if (!CanChangeTools || _core is null) return;
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is null || !await ConfirmClearMaps()) return;
            if (new FileInfo(file.Path).Length > 8_388_608) throw new InvalidDataException("Render job exceeds the 8 MiB limit.");
            string json = await File.ReadAllTextAsync(file.Path); _probing = true; UpdateEnabled();
            var task = Task.Run(() => _core.Call(new Request { Operation = "job_import", JobJson = json })); _probe = task;
            var reply = await task; var job = reply.Job;
            _syncing = true;
            Player.MediaPlayer.Pause(); Player.MediaPlayer.Source = null; Player.Visibility = PlaybackLabel.Visibility = Visibility.Collapsed;
            _info = job.SourceInfo; SourcePath.Text = job.Input; OutputPath.Text = job.Output;
            SourceInfo.Text = $"{_info.Codec} · {_info.SampleRate:N0} Hz · {_info.Channels} channels · {_info.FrameCount:N0} frames · {TimeSpan.FromSeconds(_info.Duration):g}";
            _maps = job.Maps?.Clone(); _receipt = _result = _savedJob = null; ReplayCommand.Text = ResultDetails.Text = "";
            App.Preferences.CentreFocus = job.CentreFocus; CentreFocus.IsChecked = job.CentreFocus;
            Format.SelectedIndex = Path.GetExtension(job.Output).Equals(".flac", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Precision.SelectedIndex = job.Encoding switch { "24" => 1, "16" => 2, _ => 0 };
            _autoOutput = false; _syncing = false; WriteTransform(job.Transform ?? new Transform { TimeValue = job.TempoChange });
            UpdatePrecision(); Title = Path.GetFileName(job.Input) + " - AudioStretch"; ProgressText.Text = "Render job loaded · overwrite confirmation remains required."; Status.Text = "Ready"; MessageBar.IsOpen = false;
        }
        catch (Exception error) { ShowError("Render job could not be opened", error); }
        finally { _probing = false; _syncing = false; RebuildPlan(); }
    }
    private void HelpClicked(object sender, RoutedEventArgs e) => OpenHelp();
    private void HelpShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; OpenHelp(); }
    private void OpenHelp()
    {
        if (_core is null) return;
        if (_helpWindow is null || _helpWindow.IsClosed) _helpWindow = new HelpWindow(this);
        _helpWindow.Activate();
    }
}
