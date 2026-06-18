using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AudioStretch;

internal static class AudioConverter
{
    private static readonly HashSet<string> LossyExtensions =
        [".mp3", ".ogg", ".opus", ".aac", ".m4a", ".wma", ".ac3", ".mp2"];

    public static bool IsLossy(string path) =>
        LossyExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public record ProbeResult(int SampleRate, int BitDepth, int Channels, string CodecName);

    public static async Task<ProbeResult?> ProbeAsync(
        string path, string ffprobe, Action<string> log, CancellationToken ct)
    {
        var args = $"-v quiet -print_format json -show_streams \"{path}\"";
        var sb = new StringBuilder();

        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(ffprobe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitOrKillAsync(ct);

        if (p.ExitCode != 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(sb.ToString());
            foreach (var stream in doc.RootElement.GetProperty("streams").EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out var t) || t.GetString() != "audio")
                    continue;

                int GetInt(JsonElement e) =>
                    e.ValueKind == JsonValueKind.Number ? e.GetInt32() :
                    e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var v) ? v : 0;

                var sr = stream.TryGetProperty("sample_rate", out var srp) ? GetInt(srp) : 44100;
                if (sr == 0) sr = 44100;

                var bd = stream.TryGetProperty("bits_per_sample", out var bdp) ? GetInt(bdp) : 0;
                if (bd == 0 && stream.TryGetProperty("bits_per_raw_sample", out var brsp))
                    bd = GetInt(brsp);
                if (bd == 0) bd = 16;

                var ch = stream.TryGetProperty("channels", out var chp) ? GetInt(chp) : 2;
                if (ch == 0) ch = 2;

                var codec = stream.TryGetProperty("codec_name", out var cnp) ? cnp.GetString() ?? "" : "";

                return new ProbeResult(sr, bd, ch, codec);
            }
        }
        catch { }

        return null;
    }

    public static async Task<bool> ConvertToWavAsync(
        string input, string output, ProbeResult probe,
        string ffmpeg, Action<string> log, CancellationToken ct)
    {
        var targetSr = Math.Min(probe.SampleRate, 192000);
        var pcmCodec = probe.BitDepth switch
        {
            <= 16 => "pcm_s16le",
            <= 24 => "pcm_s24le",
            _     => "pcm_f32le"
        };

        var args = $"-i \"{input}\" -ar {targetSr} -c:a {pcmCodec} -y \"{output}\"";
        log($"ffmpeg {args}");

        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitOrKillAsync(ct);

        return p.ExitCode == 0;
    }
}
