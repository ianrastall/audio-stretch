using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AudioStretch;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioPlayer _player = new();
    private readonly DispatcherTimer _posTimer;
    private readonly Dispatcher _uiDispatcher = Dispatcher.CurrentDispatcher;
    private bool _seekingFromTimer;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TempoDisplay))]
    private double tempoPercent = 100.0;

    public string TempoDisplay => $"{TempoPercent:0} %  ({TempoPercent / 100:0.000}x)";

    public ObservableCollection<string> LogLines { get; } = [];

    public bool IsScrubbing { get; set; }

    public MainViewModel()
    {
        _posTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _posTimer.Tick += (_, _) => UpdatePosition();
        _posTimer.Start();
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

        if (string.IsNullOrEmpty(OutputPath))
            OutputPath = DeriveOutputPath(dlg.FileName);

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
    private void ResetTempo() => TempoPercent = 100;

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

        IsRunning = true;
        LogLines.Clear();

        Append($"Input:  {InputPath}");
        if (IsLossyInput)
            Append("WARNING: Lossy input — re-encoding degrades quality further.");
        Append($"Output: {OutputPath}");
        Append($"Tempo:  {TempoPercent:0} % ({TempoPercent / 100:0.000}x)");
        Append("Pitch:  unchanged");
        Append(string.Empty);

        var tempWav = Path.Combine(Path.GetTempPath(), $"as_{Path.GetRandomFileName()}.wav");

        try
        {
            Append("Probing input…");
            var probe = await AudioConverter.ProbeAsync(InputPath, ffprobe!, Append, ct);
            if (probe is null) { Append("ERROR: Could not probe input."); return; }

            Append($"  {probe.SampleRate} Hz · {probe.BitDepth}-bit · {probe.Channels} ch · {probe.CodecName}");
            if (probe.SampleRate > 192000)
                Append($"  Downsampling {probe.SampleRate} → 192000 Hz");
            Append(string.Empty);

            Append("Decoding to WAV…");
            var ok = await AudioConverter.ConvertToWavAsync(InputPath, tempWav, probe, ffmpeg!, Append, ct);
            if (!ok) { Append("ERROR: FFmpeg decode failed."); return; }
            Append("Decode done.");
            Append(string.Empty);

            var args = BuildRubberbandArgs(tempWav, OutputPath, probe);
            Append($"> rubberband-r3 {args}");
            Append(string.Empty);

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
            rb.OutputDataReceived += (_, e) => { if (e.Data is not null) Append(e.Data); };
            rb.ErrorDataReceived  += (_, e) => { if (e.Data is not null) Append(e.Data); };
            rb.Start();
            rb.BeginOutputReadLine();
            rb.BeginErrorReadLine();
            await rb.WaitForExitOrKillAsync(ct);

            Append(string.Empty);
            Append(rb.ExitCode == 0 ? "Done." : $"rubberband exited with code {rb.ExitCode}.");
        }
        catch (OperationCanceledException)
        {
            Append("Cancelled.");
        }
        catch (Exception ex)
        {
            Append($"ERROR: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
        }
    }

    private string BuildRubberbandArgs(string input, string output, AudioConverter.ProbeResult probe)
    {
        var tempo = (TempoPercent / 100).ToString("0.000", CultureInfo.InvariantCulture);
        var args = $"--fine --ignore-clipping --tempo {tempo}";
        if (probe.Channels == 2)
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
        _player.Dispose();
    }
}
