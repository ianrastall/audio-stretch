using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace AudioStretch;

/// <summary>
/// Downloads the external command-line tools the app depends on (ffmpeg /
/// ffprobe and the Rubber Band R3 utility) from their stable upstream archives
/// and unpacks just the needed executables (plus any companion DLLs) into the
/// "tools" folder next to the app, where <see cref="ToolFinder"/> looks first.
/// </summary>
internal static class DependencyInstaller
{
    public static string ToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");

    private sealed record Archive(string Label, string Url, string[] Exes);

    // gyan.dev's "release-essentials" URL always resolves to the latest stable
    // build and bundles static ffmpeg.exe + ffprobe.exe (linked from ffmpeg.org).
    private static readonly Archive Ffmpeg = new(
        "FFmpeg (ffmpeg + ffprobe)",
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
        ["ffmpeg.exe", "ffprobe.exe"]);

    // breakfastquay publishes one archive per release at a fixed URL; it ships
    // rubberband-r3.exe with a required sndfile.dll beside it.
    private static readonly Archive Rubberband = new(
        "Rubber Band (rubberband-r3)",
        "https://breakfastquay.com/files/releases/rubberband-4.0.0-gpl-executable-windows.zip",
        ["rubberband-r3.exe"]);

    /// <summary>Returns the archives whose tools aren't already resolvable.</summary>
    private static List<Archive> Missing()
    {
        var needed = new List<Archive>();
        if (ToolFinder.Find("ffmpeg") is null || ToolFinder.Find("ffprobe") is null)
            needed.Add(Ffmpeg);
        if (ToolFinder.Find("rubberband-r3") is null)
            needed.Add(Rubberband);
        return needed;
    }

    /// <summary>
    /// Downloads and installs any missing tools. <paramref name="log"/> receives
    /// milestone messages; <paramref name="progress"/> receives a live,
    /// overwrite-in-place status line (e.g. download percentage).
    /// </summary>
    public static async Task EnsureAsync(
        Action<string> log, Action<string> progress, CancellationToken ct)
    {
        var needed = Missing();
        if (needed.Count == 0)
        {
            log("All tools already present — nothing to download.");
            return;
        }

        Directory.CreateDirectory(ToolsDir);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AudioStretch/1.0 (dependency installer)");

        foreach (var archive in needed)
        {
            ct.ThrowIfCancellationRequested();
            var zipPath = Path.Combine(Path.GetTempPath(), $"as_dl_{Guid.NewGuid():N}.zip");
            try
            {
                log($"Downloading {archive.Label}…");
                await DownloadAsync(http, archive.Url, zipPath, progress, ct);

                log($"Extracting {archive.Label}…");
                ExtractTools(zipPath, archive.Exes, log);
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            }
        }

        log($"Done. Installed to {ToolsDir}");
    }

    private static async Task DownloadAsync(
        HttpClient http, string url, string dest, Action<string> progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);

        var buffer = new byte[81920];
        long received = 0;
        var lastReport = -1;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            if (total > 0)
            {
                var pct = (int)(received * 100 / total);
                if (pct != lastReport)
                {
                    progress($"  {pct}%  ({received / 1_000_000} / {total / 1_000_000} MB)");
                    lastReport = pct;
                }
            }
            else
            {
                progress($"  {received / 1_000_000} MB");
            }
        }
    }

    /// <summary>
    /// Extracts the wanted executables, plus any DLLs sitting beside them in the
    /// archive (e.g. sndfile.dll for rubberband), flattened into ToolsDir.
    /// </summary>
    private static void ExtractTools(string zipPath, string[] wantedExes, Action<string> log)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var wanted = new HashSet<string>(wantedExes, StringComparer.OrdinalIgnoreCase);

        // Archive-internal folders that hold a wanted executable.
        var exeDirs = zip.Entries
            .Where(e => wanted.Contains(Path.GetFileName(e.FullName)))
            .Select(e => DirOf(e.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (exeDirs.Count == 0)
        {
            log("  WARNING: expected executables were not found in the archive.");
            return;
        }

        var installed = 0;
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory marker

            var fileName = Path.GetFileName(entry.FullName);
            var isWantedExe = wanted.Contains(fileName);
            var isCompanionDll =
                fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                exeDirs.Contains(DirOf(entry.FullName));

            if (!isWantedExe && !isCompanionDll) continue;

            entry.ExtractToFile(Path.Combine(ToolsDir, fileName), overwrite: true);
            log($"  installed {fileName}");
            installed++;
        }

        if (installed == 0)
            log("  WARNING: nothing was extracted from the archive.");
    }

    private static string DirOf(string entryFullName)
    {
        var slash = entryFullName.LastIndexOf('/');
        return slash < 0 ? string.Empty : entryFullName[..slash];
    }
}
