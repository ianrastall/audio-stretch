using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace AudioStretch;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioPlayer _player = new();
    private readonly DispatcherTimer _posTimer;
    private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;
    private bool _seekingFromTimer;

    private Process? _activeProcess;
    private string? _decodeTemp;
    private string? _outputTemp;
    private bool _outputCustomized;
    private bool _suppressOutputTracking;

    [ObservableProperty] private string inputPath = string.Empty;
    [ObservableProperty] private string outputPath = string.Empty;
    [ObservableProperty] private bool isLossyInput;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string playPauseLabel = "Play";
    [ObservableProperty] private double currentPositionSeconds;
    [ObservableProperty] private double totalDurationSeconds;
    [ObservableProperty] private string currentPositionText = "0:00";
    [ObservableProperty] private string totalDurationText = "0:00";

    [ObservableProperty] private string dependencyStatusText = string.Empty;
    [ObservableProperty] private bool toolsMissing;
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private string installStatus = string.Empty;

    // Tempo is expressed as a percentage change from the original speed:
    // 0 = unchanged, negative = slower, positive = faster.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TempoDisplay))]
    [NotifyPropertyChangedFor(nameof(TempoMultiplier))]
    private double tempoChange;

    public double TempoMultiplier => 1.0 + TempoChange / 100.0;
    public string TempoDisplay => $"{TempoChange:+0;-0;0} %  ({TempoMultiplier:0.000}x)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PitchDisplay))]
    private double pitchShift;
    
    public string PitchDisplay => PitchShift == 0 ? "0 semitones" : $"{PitchShift:+0.0;-0.0;0} semitones";

    [ObservableProperty] private bool enableFormant;
    [ObservableProperty] private bool enableCentreFocus = true;

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty] private double overallProgress;
    [ObservableProperty] private string mainStatus = string.Empty;
    [ObservableProperty] private string subStatus = string.Empty;
    [ObservableProperty] private bool isProgressIndeterminate;
    [ObservableProperty] private bool showRawLog;

    public bool IsScrubbing { get; set; }

    public MainViewModel()
    {
        _posTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _posTimer.Tick += (_, _) => UpdatePosition();
        _posTimer.Start();

        CheckTools();
    }

    private void CheckTools()
    {
        var ffmpeg  = ToolFinder.Find("ffmpeg");
        var ffprobe = ToolFinder.Find("ffprobe");
        var rb      = ToolFinder.Find("rubberband-r3");

        ToolsMissing = ffmpeg is null || ffprobe is null || rb is null;

        static string Mark(string? p) => p is null ? "not found" : "found";
        DependencyStatusText =
            $"ffmpeg: {Mark(ffmpeg)}     ffprobe: {Mark(ffprobe)}     rubberband-r3: {Mark(rb)}";
    }

    [RelayCommand]
    private void RefreshTools() => CheckTools();

    [RelayCommand]
    private async Task InstallToolsAsync()
    {
        IsInstalling = true;
        InstallStatus = "Starting…";
        try
        {
            await DependencyInstaller.EnsureAsync(
                log: msg => _uiDispatcher.Invoke(() => { LogLines.Add(msg); InstallStatus = msg; }),
                progress: msg => _uiDispatcher.Invoke(() => InstallStatus = msg),
                ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            InstallStatus = $"Install failed: {ex.Message}";
            Append($"ERROR: Tool install failed: {ex.Message}");
        }
        finally
        {
            IsInstalling = false;
            CheckTools();
        }
    }

    private void UpdatePosition()
    {
        if (!IsScrubbing)
        {
            _seekingFromTimer = true;
            CurrentPositionSeconds = _player.CurrentTime.TotalSeconds;
            _seekingFromTimer = false;
            CurrentPositionText = FormatTime(_player.CurrentTime);
        }

        var playing = _player.IsPlaying;
        if (IsPlaying != playing)
        {
            IsPlaying = playing;
            PlayPauseLabel = playing ? "Pause" : "Play";
        }
    }

    partial void OnCurrentPositionSecondsChanged(double value)
    {
        if (!_seekingFromTimer)
            _player.Seek(TimeSpan.FromSeconds(value));
    }

    public void SeekTo(double seconds) => _player.Seek(TimeSpan.FromSeconds(seconds));

    partial void OnOutputPathChanged(string value)
    {
        // Any change the app didn't make itself counts as the user choosing
        // their own output path.
        if (!_suppressOutputTracking)
            _outputCustomized = true;
    }

    [RelayCommand]
    private void PickInput()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio files|*.wav;*.aif;*.aiff;*.mp3;*.flac;*.ogg;*.opus;*.m4a;*.aac;*.wma;*.w64|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        InputPath = dlg.FileName;
        IsLossyInput = AudioConverter.IsLossy(dlg.FileName);

        // Re-point the output at the new input unless the user picked or typed
        // their own output path; otherwise a second input keeps targeting the
        // previous file.
        if (!_outputCustomized)
        {
            _suppressOutputTracking = true;
            OutputPath = DeriveOutputPath(dlg.FileName);
            _suppressOutputTracking = false;
        }

        try
        {
            _player.Load(dlg.FileName);
            TotalDurationSeconds = _player.TotalDuration.TotalSeconds;
            TotalDurationText = FormatTime(_player.TotalDuration);
            CurrentPositionSeconds = 0;
            CurrentPositionText = "0:00";
            HasPreview = true;
        }
        catch (Exception ex)
        {
            HasPreview = false;
            LogLines.Add($"Preview unavailable: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PickOutput()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "WAV files|*.wav",
            DefaultExt = ".wav",
            FileName = Path.GetFileName(OutputPath)
        };
        if (!string.IsNullOrEmpty(OutputPath))
            dlg.InitialDirectory = Path.GetDirectoryName(OutputPath);
        if (dlg.ShowDialog() != true) return;
        OutputPath = dlg.FileName;
    }

    [RelayCommand]
    private void PlayPause()
    {
        _player.PlayPause();
        IsPlaying = _player.IsPlaying;
        PlayPauseLabel = _player.IsPlaying ? "Pause" : "Play";
    }

    [RelayCommand]
    private void ResetTempo() => TempoChange = 0;

    [RelayCommand]
    private void ResetPitch() => PitchShift = 0;

    [RelayCommand]
    private void CopyLog()
    {
        Clipboard.SetText(BuildLogText(includeHeader: false));
        Append("Log copied to clipboard.");
    }

    [RelayCommand]
    private void SaveLog()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Text log|*.txt|All files|*.*",
            DefaultExt = ".txt",
            FileName = $"AudioStretch-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };

        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, BuildLogText(includeHeader: true), Encoding.UTF8);
        Append($"Log saved: {dlg.FileName}");
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(InputPath) || string.IsNullOrWhiteSpace(OutputPath)) return;

        if (SamePath(InputPath, OutputPath))
        {
            Append("ERROR: Output path cannot be the same as the input path.");
            Append("The input is still open for preview, so it can't be overwritten in place.");
            return;
        }

        var ffmpeg    = ToolFinder.Find("ffmpeg");
        var ffprobe   = ToolFinder.Find("ffprobe");
        var rubberband = ToolFinder.Find("rubberband-r3");

        var missing = new List<string>();
        if (ffmpeg    is null) missing.Add("ffmpeg");
        if (ffprobe   is null) missing.Add("ffprobe");
        if (rubberband is null) missing.Add("rubberband");

        if (missing.Count > 0)
        {
            Append($"ERROR: Missing tools: {string.Join(", ", missing)}");
            Append("Place them in a 'tools' folder next to this exe, or add to PATH.");
            return;
        }

        if (File.Exists(OutputPath))
        {
            var answer = MessageBox.Show(
                $"Output file already exists:\n\n{OutputPath}\n\nOverwrite it?",
                "AudioStretch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                Append("Cancelled: output file already exists.");
                return;
            }
        }

        IsRunning = true;
        OverallProgress = 0;
        MainStatus = "Initializing…";
        SubStatus = string.Empty;
        IsProgressIndeterminate = true;
        LogLines.Clear();

        Append($"Input:  {InputPath}");
        if (IsLossyInput)
            Append("WARNING: Lossy input — re-encoding degrades quality further.");
        Append($"Output: {OutputPath}");
        Append($"Tempo:  {TempoChange:+0;-0;0} % ({TempoMultiplier:0.000}x)");
        var pitchStr = PitchShift == 0 ? "unchanged" : $"{PitchShift:+0.0;-0.0;0} semitones";
        if (EnableFormant && PitchShift != 0) pitchStr += " (Formant preserved)";
        Append($"Pitch:  {pitchStr}");
        Append($"Centre Focus: {(EnableCentreFocus ? "Enabled" : "Disabled")}");
        Append(string.Empty);
        
        var sw = Stopwatch.StartNew();
        
        void UpdateStats(double pct)
        {
            _uiDispatcher.InvokeAsync(() => 
            {
                OverallProgress = pct * 100;
                if (pct > 0)
                {
                    var elapsed = sw.Elapsed;
                    var totalEst = TimeSpan.FromSeconds(elapsed.TotalSeconds / pct);
                    var remaining = totalEst - elapsed;
                    if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
                    SubStatus = $"{OverallProgress:0}% • Elapsed: {FormatTime(elapsed)} • ETA: {FormatTime(remaining)}";
                }
                else
                {
                    SubStatus = $"0% • Elapsed: {FormatTime(sw.Elapsed)}";
                }
            });
        }

        // Decode goes to a temp WAV; rubberband writes to a temp file beside the
        // final output so a failed or cancelled run never leaves a partial file
        // at OutputPath. The final file appears only via an atomic move on success.
        var tempWav = Path.Combine(Path.GetTempPath(), $"as_{Path.GetRandomFileName()}.wav");
        var outDir = Path.GetDirectoryName(Path.GetFullPath(OutputPath)) ?? Path.GetTempPath();
        var outputTmp = Path.Combine(outDir, $"~as_{Guid.NewGuid():N}.tmp.wav");
        _decodeTemp = tempWav;
        _outputTemp = outputTmp;

        try
        {
            MainStatus = "Probing input…";
            IsProgressIndeterminate = true;
            Append("Probing input…");
            var probe = await AudioConverter.ProbeAsync(InputPath, ffprobe!, Append, ct, TrackProcess);
            if (probe is null) { Append("ERROR: Could not probe input."); return; }

            Append($"  {probe.SampleRate} Hz · {probe.BitDepth}-bit · {probe.Channels} ch · {probe.CodecName}");
            if (probe.SampleRate > 192000)
                Append($"  Downsampling {probe.SampleRate} → 192000 Hz");
            Append(string.Empty);

            MainStatus = "Decoding to WAV…";
            IsProgressIndeterminate = false;
            Append("Decoding to WAV…");
            var ok = await AudioConverter.ConvertToWavAsync(InputPath, tempWav, probe, TotalDurationSeconds, ffmpeg!, Append, 
                pct => UpdateStats(pct * 0.20), ct, TrackProcess);
            if (!ok) { Append("ERROR: FFmpeg decode failed."); return; }
            Append("Decode done.");
            Append(string.Empty);

            MainStatus = "Applying time-stretch…";
            var args = BuildRubberbandArgs(tempWav, outputTmp, probe);
            Append($"> rubberband-r3 {args}");
            Append(string.Empty);

            var rbRegex = new Regex(@"(\d+)%", RegexOptions.Compiled);
            int currentPass = 1;

            using var rb = new Process
            {
                StartInfo = new ProcessStartInfo(rubberband!, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            rb.OutputDataReceived += (_, e) => 
            { 
                if (e.Data is null) return;
                Append(e.Data); 
                if (e.Data.Contains("Pass 1:")) { currentPass = 1; _uiDispatcher.InvokeAsync(() => MainStatus = "Applying time-stretch (Pass 1)…"); }
                else if (e.Data.Contains("Pass 2:")) { currentPass = 2; _uiDispatcher.InvokeAsync(() => MainStatus = "Applying time-stretch (Pass 2)…"); }
                
                var matches = rbRegex.Matches(e.Data);
                if (matches.Count > 0)
                {
                    if (int.TryParse(matches[^1].Groups[1].Value, out var p))
                    {
                        double stretchPct = (currentPass == 1) ? (p / 200.0) : (0.5 + (p / 200.0));
                        UpdateStats(0.20 + (stretchPct * 0.80));
                    }
                }
            };
            rb.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Append(e.Data); };
            rb.Start();
            TrackProcess(rb);
            rb.BeginOutputReadLine();
            rb.BeginErrorReadLine();
            await rb.WaitForExitOrKillAsync(ct);

            Append(string.Empty);
            if (rb.ExitCode == 0)
            {
                File.Move(outputTmp, OutputPath, overwrite: true);
                MainStatus = "Done.";
                OverallProgress = 100;
                SubStatus = $"Elapsed: {FormatTime(sw.Elapsed)}";
                Append($"Done. Wrote {OutputPath}");
            }
            else
            {
                MainStatus = "Failed.";
                Append($"rubberband exited with code {rb.ExitCode}. Output not written.");
            }
        }
        catch (OperationCanceledException)
        {
            MainStatus = "Cancelled.";
            Append("Cancelled.");
        }
        catch (Exception ex)
        {
            MainStatus = "Error occurred.";
            Append($"ERROR: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            IsRunning = false;
            IsProgressIndeterminate = false;
            _activeProcess = null;
            CleanupTempFiles();
        }
    }

    private void TrackProcess(Process p) => _activeProcess = p;

    private void CleanupTempFiles()
    {
        foreach (var f in new[] { _decodeTemp, _outputTemp })
        {
            try { if (f is not null && File.Exists(f)) File.Delete(f); } catch { }
        }
        _decodeTemp = null;
        _outputTemp = null;
    }

    private string BuildRubberbandArgs(string input, string output, AudioConverter.ProbeResult probe)
    {
        var tempo = TempoMultiplier.ToString("0.000", CultureInfo.InvariantCulture);
        var args = $"--fine --ignore-clipping --tempo {tempo}";
        
        if (PitchShift != 0)
        {
            var pitch = PitchShift.ToString("0.00", CultureInfo.InvariantCulture);
            args += $" --pitch {pitch}";
            if (EnableFormant)
                args += " --formant";
        }
        
        if (EnableCentreFocus && probe.Channels == 2)
            args += " --centre-focus";
            
        args += $" \"{input}\" \"{output}\"";
        return args;
    }

    private void Append(string line) =>
        _uiDispatcher.Invoke(() => LogLines.Add(line));

    private string BuildLogText(bool includeHeader)
    {
        var sb = new StringBuilder();

        if (includeHeader)
        {
            sb.AppendLine("AudioStretch log");
            sb.AppendLine($"Saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        if (LogLines.Count == 0)
        {
            sb.AppendLine("(No log entries.)");
        }
        else
        {
            foreach (var line in LogLines)
                sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string DeriveOutputPath(string inputPath)
    {
        var dir  = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, name + "_stretched.wav");
    }

    public void Dispose()
    {
        _posTimer.Stop();

        // Closing the window mid-run must take ffmpeg/rubberband down with it,
        // otherwise the child tree is orphaned and keeps running after exit.
        try
        {
            var p = _activeProcess;
            if (p is not null && !p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2000);
            }
        }
        catch { /* already exited or no longer accessible */ }

        CleanupTempFiles();
        _player.Dispose();
    }
}
