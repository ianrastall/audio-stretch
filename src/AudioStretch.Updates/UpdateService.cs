using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudioStretch.Updates;

public sealed record ToolPackage(string Id, string Version, string Url, string Sha256, string[] Executables, string[] RequiredFiles);
public sealed record ToolCatalog(int SchemaVersion, ToolPackage[] Packages);
public sealed record AppUpdate(string Version, string Url, string Sha256, string Page);
public sealed record UpdateCheck(AppUpdate? Application, ToolCatalog Catalog, string RubberBandUpstream, DateTimeOffset CheckedAt);

public sealed partial class UpdateService : IDisposable
{
    public const string Repository = "ianrastall/audio-stretch";
    public const string CatalogUrl = "https://raw.githubusercontent.com/ianrastall/audio-stretch/master/updates/tool-catalog.json";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly HttpClient _http;
    private readonly string _data;
    public string ToolRoot => Path.Combine(_data, "tools");
    public UpdateService(string dataDirectory, HttpClient? http = null)
    {
        _data = Path.GetFullPath(dataDirectory);
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AudioStretch/0.3.0");
    }

    public static Version ParseVersion(string value)
    {
        if (!VersionText().IsMatch(value)) throw new InvalidDataException("The update has an unsupported version number.");
        var parsed = Version.Parse(value.TrimStart('v'));
        return new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
    }
    public static bool IsNewer(string remote, string local) => ParseVersion(remote) > ParseVersion(local);
    public static void ValidatePackage(ToolPackage package)
    {
        _ = ParseVersion(package.Version);
        if (package.Id is not ("rubberband" or "ffmpeg") || !HashText().IsMatch(package.Sha256)) throw new InvalidDataException("Invalid tool catalog entry.");
        var uri = new Uri(package.Url);
        string expected = package.Id == "rubberband"
            ? $"https://breakfastquay.com/files/releases/rubberband-{package.Version}-gpl-executable-windows.zip"
            : $"https://github.com/GyanD/codexffmpeg/releases/download/{package.Version}/ffmpeg-{package.Version}-essentials_build.zip";
        if (uri.AbsoluteUri != expected) throw new InvalidDataException("The tool package is not at its expected official upstream URL.");
        string[] executables = package.Id == "rubberband" ? ["rubberband-r3.exe"] : ["ffmpeg.exe", "ffprobe.exe"];
        string[] required = package.Id == "rubberband" ? ["rubberband-r3.exe", "sndfile.dll"] : executables;
        if (!package.Executables.SequenceEqual(executables) || !package.RequiredFiles.SequenceEqual(required)) throw new InvalidDataException("Unexpected executable list in the tool catalog.");
    }
    public static ToolCatalog ParseCatalog(string json)
    {
        var catalog = JsonSerializer.Deserialize<ToolCatalog>(json, JsonOptions) ?? throw new InvalidDataException("Empty tool catalog.");
        if (catalog.SchemaVersion != 1 || catalog.Packages.Length != 2 || catalog.Packages.Select(p => p.Id).Distinct().Count() != 2) throw new InvalidDataException("Unsupported tool catalog.");
        foreach (var package in catalog.Packages) ValidatePackage(package);
        return catalog;
    }
    public ToolCatalog BundledCatalog(string path) => ParseCatalog(File.ReadAllText(path));
    public async Task<UpdateCheck> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        // A failed request is an error, never a false report that everything is current.
        var app = await CheckApplicationAsync(currentVersion, ct);
        var catalog = ParseCatalog(await GetTextAsync(CatalogUrl, 65536, ct));
        string html = await GetTextAsync("https://breakfastquay.com/rubberband/", 2_097_152, ct);
        var versions = UpstreamZip().Matches(html).Select(m => m.Groups[1].Value).Distinct().OrderBy(ParseVersion).ToArray();
        if (versions.Length == 0) throw new InvalidDataException("Could not read the current Windows Rubber Band release from its official download page.");
        return new UpdateCheck(app, catalog, versions[^1], DateTimeOffset.UtcNow);
    }
    public async Task<AppUpdate?> CheckApplicationAsync(string currentVersion, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(ct);
        if (text.Length > 1_048_576) throw new InvalidDataException("Release response is too large.");
        using var doc = JsonDocument.Parse(text); var release = doc.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string version = release.GetProperty("tag_name").GetString()!.TrimStart('v');
        if (!IsNewer(version, currentVersion)) return null;
        string filename = $"AudioStretch-{version}-Setup.exe";
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var asset = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == filename);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The release has no Windows installer.");
        string url = asset.GetProperty("browser_download_url").GetString()!;
        ValidateAppUrl(url, version, filename);
        string? digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
        string hash;
        if (digest?.StartsWith("sha256:", StringComparison.Ordinal) == true) hash = digest[7..];
        else
        {
            var sums = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
            if (sums.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The release has no installer checksum.");
            string sumsUrl = sums.GetProperty("browser_download_url").GetString()!;
            ValidateAppUrl(sumsUrl, version, "SHA256SUMS.txt");
            string checksums = await GetTextAsync(sumsUrl, 65536, ct);
            hash = ParseChecksum(checksums, filename);
        }
        if (!HashText().IsMatch(hash)) throw new InvalidDataException("The release checksum is invalid.");
        return new AppUpdate(version, url, hash, $"https://github.com/{Repository}/releases/tag/v{version}");
    }
    private static void ValidateAppUrl(string url, string version, string filename)
    {
        if (url != $"https://github.com/{Repository}/releases/download/v{version}/{filename}") throw new InvalidDataException("Unexpected application update URL.");
    }
    public static string ParseChecksum(string checksums, string filename)
    {
        foreach (string line in checksums.Split('\n'))
        {
            var parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == filename && HashText().IsMatch(parts[0])) return parts[0];
        }
        throw new InvalidDataException("The checksum file does not identify this installer.");
    }
    private async Task<string> GetTextAsync(string url, int limit, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update metadata is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream(); var buffer = new byte[8192];
        int n; while ((n = await stream.ReadAsync(buffer, ct)) > 0) { if (memory.Length + n > limit) throw new InvalidDataException("Update metadata is too large."); await memory.WriteAsync(buffer.AsMemory(0, n), ct); }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }
    public async Task<string> DownloadVerifiedAsync(string url, string expectedHash, string filename, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!HashText().IsMatch(expectedHash) || Path.GetFileName(filename) != filename || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Invalid download identity.");
        var directory = Path.Combine(_data, "updates", "downloads"); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, filename);
        if (File.Exists(target) && await HasHash(target, expectedHash, ct)) return target;
        string partial = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            const long max = 512L * 1024 * 1024;
            if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Update download exceeds the size limit.");
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[131072]; long total = 0, lastReport = -1; int n;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += n; if (total > max) throw new InvalidDataException("Update download exceeds the size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    long megabytes = total / 1_048_576;
                    if (megabytes != lastReport) { progress?.Report($"Downloading {filename}: {megabytes} MiB" + (response.Content.Headers.ContentLength is long length ? $" / {length / 1_048_576} MiB" : "")); lastReport = megabytes; }
                }
            }
            if (!await HasHash(partial, expectedHash, ct)) throw new InvalidDataException("The downloaded file failed SHA-256 verification. Existing software was preserved.");
            File.Move(partial, target, true); return target;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public static async Task<bool> HasHash(string path, string hash, CancellationToken ct = default)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    public async Task InstallToolAsync(ToolPackage package, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ValidatePackage(package);
        string archive = await DownloadVerifiedAsync(package.Url, package.Sha256, $"{package.Id}-{package.Version}.zip", progress, ct);
        string family = Path.Combine(ToolRoot, package.Id); Directory.CreateDirectory(Path.Combine(family, "versions"));
        string key = package.Version + "-" + Guid.NewGuid().ToString("N");
        string stage = Path.Combine(family, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            progress?.Report("Extracting and checking " + package.Id);
            await Task.Run(() => ExtractPackage(archive, stage, package), ct);
            foreach (string exe in package.Executables)
            {
                ct.ThrowIfCancellationRequested();
                var version = await ReadVersionAsync(Path.Combine(stage, exe), exe == "rubberband-r3.exe" ? "--version" : "-version", ct);
                if (ParseVersion(version) != ParseVersion(package.Version)) throw new InvalidDataException($"{exe} reports {version}; expected {package.Version}. The previous installation is unchanged.");
            }
            var receipt = new { package.Id, package.Version, package.Url, package.Sha256, InstalledAt = DateTimeOffset.UtcNow };
            await File.WriteAllTextAsync(Path.Combine(stage, "receipt.json"), JsonSerializer.Serialize(receipt, JsonOptions), ct);
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, Path.Combine(family, "versions", key));
            string active = Path.Combine(family, "active.txt");
            if (File.Exists(active)) File.Copy(active, Path.Combine(family, "previous.txt"), true);
            await File.WriteAllTextAsync(active + ".tmp", key, ct);
            File.Move(active + ".tmp", active, true);
            progress?.Report($"Installed {package.Id} {package.Version}");
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public static void ExtractPackage(string archive, string stage, ToolPackage package)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 1_073_741_824) throw new InvalidDataException("Tool archive exceeds the extraction limit.");
        foreach (var entry in zip.Entries) ValidateEntry(entry.FullName);
        var first = zip.Entries.SingleOrDefault(e => Path.GetFileName(e.FullName) == package.Executables[0]) ?? throw new InvalidDataException("The archive is missing its executable.");
        string bin = first.FullName[..(first.FullName.LastIndexOf('/') + 1)];
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue;
            string name = Path.GetFileName(entry.FullName);
            bool binary = package.RequiredFiles.Contains(name) || (entry.FullName.StartsWith(bin, StringComparison.Ordinal) && !entry.FullName[bin.Length..].Contains('/') && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            bool notice = Regex.IsMatch(name, "(?i)license|copying|readme|notice|changelog");
            string? destination = binary ? Path.Combine(stage, name) : notice ? Path.Combine(stage, "notices", entry.FullName.Replace('/', Path.DirectorySeparatorChar)) : null;
            if (destination is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, false);
        }
        foreach (string required in package.RequiredFiles) if (!File.Exists(Path.Combine(stage, required))) throw new InvalidDataException("Archive is missing " + required);
    }
    public static void ValidateEntry(string name)
    {
        if (name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p => p is "." or "..")) throw new InvalidDataException("Unsafe path in tool archive.");
    }
    public static async Task<string> ReadVersionAsync(string exe, string argument, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add(argument); process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } catch { } throw; }
        string text = (await stdout) + "\n" + (await stderr);
        if (process.ExitCode != 0) throw new InvalidDataException("The downloaded executable did not start successfully.");
        var match = BinaryVersion().Match(text);
        return match.Success ? match.Value : throw new InvalidDataException("Could not determine the executable version.");
    }
    public string? ResolveTool(string name, string bundledDirectory)
    {
        string family = name.StartsWith("rubberband", StringComparison.Ordinal) || name == "sndfile.dll" ? "rubberband" : "ffmpeg";
        string active = Path.Combine(ToolRoot, family, "active.txt");
        if (File.Exists(active))
        {
            string key = File.ReadAllText(active).Trim();
            if (key.Length < 120 && key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && key is not ("." or ".."))
            { string candidate = Path.Combine(ToolRoot, family, "versions", key, name); if (File.Exists(candidate)) return candidate; }
        }
        string bundled = Path.Combine(bundledDirectory, name);
        return File.Exists(bundled) ? bundled : null;
    }
    public async Task<string> DownloadApplicationAsync(AppUpdate update, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _ = ParseVersion(update.Version); string filename = $"AudioStretch-{update.Version}-Setup.exe";
        ValidateAppUrl(update.Url, update.Version, filename);
        return await DownloadVerifiedAsync(update.Url, update.Sha256, filename, progress, ct);
    }
    public static void LaunchApplicationInstaller(string path, string hash, string hostPath)
    {
        if (!HashText().IsMatch(hash)) throw new InvalidDataException("Invalid installer hash.");
        // Run a self-contained copy outside {app}, so the installer can replace every installed file.
        string helperDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jupiter", "AudioStretch", "updates", "helpers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDirectory);
        string helper = Path.Combine(helperDirectory, "AudioStretch.UpdateHost.exe");
        File.Copy(hostPath, helper);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { Environment.ProcessId.ToString(), path, hash, Path.Combine(Path.GetDirectoryName(hostPath)!, "AudioStretch.exe") }) start.ArgumentList.Add(arg);
        _ = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
    }
    public void Dispose() => _http.Dispose();
    [GeneratedRegex(@"^v?\d+\.\d+(?:\.\d+){0,2}$")] private static partial Regex VersionText();
    [GeneratedRegex(@"^[a-fA-F0-9]{64}$")] private static partial Regex HashText();
    [GeneratedRegex(@"rubberband-([0-9]+\.[0-9]+(?:\.[0-9]+)?)-gpl-executable-windows\.zip")] private static partial Regex UpstreamZip();
    [GeneratedRegex(@"\b\d+\.\d+(?:\.\d+)?\b")] private static partial Regex BinaryVersion();
}
