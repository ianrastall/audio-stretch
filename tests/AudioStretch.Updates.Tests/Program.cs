using AudioStretch.Updates;
using AudioStretch.Interop;
using AudioStretch.Protocol;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

string root = Path.GetFullPath(args[0]);
var catalog = UpdateService.ParseCatalog(File.ReadAllText(Path.Combine(root, "updates", "tool-catalog.json")));
if (args.Contains("--live-release"))
{
    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jupiter", "AudioStretch");
    using var live = new UpdateService(data);
    var check = await live.CheckAsync("0.2.1");
    Check(check.Application?.Version == "0.3.0", "Published release discovered as upgrade from 0.2.1");
    string installer = await live.DownloadApplicationAsync(check.Application!);
    Check(await UpdateService.HasHash(installer, check.Application!.Sha256), "Published installer download hash verified");
    if (args.Contains("--install-tools"))
        foreach (var package in check.Catalog.Packages) await live.InstallToolAsync(package, new Progress<string>(Console.WriteLine));
    File.WriteAllText(Path.Combine(root, "artifacts", "live-update-verification.json"), JsonSerializer.Serialize(new { installer, hash = check.Application.Sha256, check.RubberBandUpstream, tools = live.ToolRoot }, UpdateService.JsonOptions));
    return;
}
Check(UpdateService.IsNewer("v0.2.10", "0.2.9") && !UpdateService.IsNewer("4.0", "4.0.0"), "Semantic version ordering");
Throws(() => UpdateService.ParseVersion("../../4.0"), "Unsafe version rejected");
Throws(() => UpdateService.ParseVersion("0.3.0-beta"), "Prerelease version rejected");
Throws(() => UpdateService.ValidatePackage(catalog.Packages[0] with { Url = "https://example.com/tool.zip" }), "Unexpected tool origin rejected");
Check(UpdateService.ParseChecksum(new string('a', 64) + "  app.exe\n", "app.exe") == new string('a', 64), "Installer checksum identity");
Throws(() => UpdateService.ParseChecksum(new string('a', 64) + "  other.exe", "app.exe"), "Unrelated checksum rejected");
foreach (string path in new[] { "../evil.exe", "C:/evil.exe", "/evil.exe", "dir\\evil.exe" }) Throws(() => UpdateService.ValidateEntry(path), "Archive traversal rejected: " + path);

string temp = Path.Combine(Path.GetTempPath(), "audiostretch-update-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
try
{
    byte[] payload = Encoding.UTF8.GetBytes("verified payload"); string hash = Convert.ToHexString(SHA256.HashData(payload));
    using (var client = new HttpClient(new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })))
    using (var service = new UpdateService(temp, client))
    {
        string path = await service.DownloadVerifiedAsync("https://example.test/payload", hash, "payload.zip");
        Check(File.ReadAllText(path) == "verified payload", "Verified download published");
        bool rejected = false;
        try { await service.DownloadVerifiedAsync("https://example.test/payload", new string('0', 64), "payload.zip"); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(path) == "verified payload", "Bad checksum preserves existing cache");
    }
    string catalogText = File.ReadAllText(Path.Combine(root, "updates", "tool-catalog.json"));
    var release = JsonSerializer.Serialize(new {
        draft = false, prerelease = false, tag_name = "v0.2.2",
        assets = new[] { new { name = "AudioStretch-0.2.2-Setup.exe", browser_download_url = "https://github.com/ianrastall/audio-stretch/releases/download/v0.2.2/AudioStretch-0.2.2-Setup.exe", digest = "sha256:" + hash.ToLowerInvariant() } }
    });
    using (var client = new HttpClient(new Stub(req => new(HttpStatusCode.OK) { Content = new StringContent(req.RequestUri!.Host switch { "api.github.com" => release, "raw.githubusercontent.com" => catalogText, _ => "rubberband-4.0.0-gpl-executable-windows.zip" }) })))
    using (var service = new UpdateService(temp, client))
    {
        var result = await service.CheckAsync("0.2.1");
        Check(result.Application?.Version == "0.2.2" && result.RubberBandUpstream == "4.0.0", "Independent application/tool update discovery");
        Check((await service.CheckAsync("0.2.2")).Application is null, "Current version is not offered as an upgrade");
    }
    using (var client = new HttpClient(new Stub(_ => throw new HttpRequestException("offline"))))
    using (var service = new UpdateService(temp, client))
    {
        bool failed = false; try { await service.CheckAsync("0.2.1"); } catch (HttpRequestException) { failed = true; }
        Check(failed, "Offline check is an error rather than a false up-to-date result");
    }
    // Install the exact official packages into an isolated user data directory, preserving notices.
    var downloads = Path.Combine(temp, "updates", "downloads"); Directory.CreateDirectory(downloads);
    foreach (var package in catalog.Packages)
        File.Copy(Path.Combine(root, "work", "downloads", package.Id == "rubberband" ? "rubberband.zip" : "ffmpeg.zip"), Path.Combine(downloads, package.Id + "-" + package.Version + ".zip"), true);
    using (var service = new UpdateService(temp))
    {
        foreach (var package in catalog.Packages)
        {
            await service.InstallToolAsync(package);
            string? path = service.ResolveTool(package.Executables[0], Path.Combine(temp, "no-bundle"));
            Check(path is not null, "Installed tool selected: " + package.Id);
            Check(Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(path!)!, "notices"), "*", SearchOption.AllDirectories).Any(), "Upstream notices retained: " + package.Id);
        }
        string active = Path.Combine(service.ToolRoot, "rubberband", "active.txt"); string before = File.ReadAllText(active);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await service.InstallToolAsync(catalog.Packages[0], ct: cancelled.Token); } catch (OperationCanceledException) { }
        Check(File.ReadAllText(active) == before, "Cancelled installation preserves active tool");
        using var core = new CoreClient(service.ToolRoot);
        string input = Path.Combine(root, "test.wav"), output = Path.Combine(temp, "rendered.wav");
        var info = await core.ProbeAsync(input);
        core.Start(new Request { Input = input, Output = output, TempoChange = 5.125, Encoding = "float" });
        Snapshot snapshot; var start = DateTime.UtcNow;
        do { snapshot = core.Poll(); if (DateTime.UtcNow - start > TimeSpan.FromSeconds(60)) throw new Exception("Render timeout"); await Task.Delay(20); } while (snapshot.State is "Running" or "Cancelling");
        Check(snapshot.State == "Completed", "Native render uses independently installed tools: " + snapshot.Error);
        Check(Math.Abs((await core.ProbeAsync(output)).Duration - info.Duration / 1.05125) < .03, "Managed-tool render duration");
        // Preserve this isolated installation for the real audio measurement harness.
        File.WriteAllText(Path.Combine(root, "artifacts", "managed-test-tools.txt"), service.ToolRoot);
    }
    Console.WriteLine("All update checks passed.");
}
finally
{
    // Keep isolated packages for the immediately following audio verification; cleanup is an explicit later test-build step.
}
static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
static void Throws(Action action, string name) { bool threw = false; try { action(); } catch (InvalidDataException) { threw = true; } Check(threw, name); }
sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
}
