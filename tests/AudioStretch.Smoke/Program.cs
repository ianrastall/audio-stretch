using AudioStretch.Interop;
using AudioStretch.Protocol;
using System.Diagnostics;
using System.Globalization;

var publish = Path.GetFullPath(args[0]);
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
var temp = Path.Combine(Path.GetTempPath(), "audio-stretch-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var source = Path.Combine(temp, "source 音.wav");
    using (var wav = new BinaryWriter(File.Create(source)))
    {
        const int rate = 48000, seconds = 60, data = rate * seconds * 4;
        wav.Write("RIFF"u8); wav.Write(data + 36); wav.Write("WAVEfmt "u8); wav.Write(16);
        wav.Write((short)1); wav.Write((short)2); wav.Write(rate); wav.Write(rate * 4); wav.Write((short)4); wav.Write((short)16); wav.Write("data"u8); wav.Write(data);
        for (int i = 0; i < rate * seconds; i++) { short sample = (short)(10000 * Math.Sin(2 * Math.PI * 440 * i / rate)); wav.Write(sample); wav.Write(sample); }
    }
    using var core = new CoreClient(Path.Combine(publish, "tools"));
    var info = await core.ProbeAsync(source);
    Check(info.SampleRate == 48000 && info.Channels == 2 && Math.Abs(info.Duration - 60) < .01, "Managed/native audio probe");
    var output = Path.Combine(temp, "result.flac");
    var sentinel = "previous audio file";
    File.WriteAllText(output, sentinel);
    var pitchMap = new Maps { SampleRate = info.SampleRate, SourceFrames = info.FrameCount, SourceSha256 = info.Sha256, PitchKind = MapKind.Pitch };
    pitchMap.PitchPoints.Add(new PitchAnchor { SourceFrame = 0, Value = 3 });
    pitchMap.PitchPoints.Add(new PitchAnchor { SourceFrame = 480000, Value = -3 });
    var savedJob = core.ExportJob(new Request { Input = source, Output = output, SourceInfo = info, Transform = new Transform { TimeValue = 5 }, Maps = pitchMap, Encoding = "24", Overwrite = true });
    var imported = core.Call(new Request { Operation = "job_import", JobJson = savedJob }).Job;
    Check(!imported.Overwrite && imported.Maps.Equals(pitchMap) && imported.SourceInfo.Sha256 == info.Sha256, "Job roundtrip keeps source identity/maps and disables overwrite");
    var equivalent = core.Call(new Request { Operation = "convert_time", SourceInfo = info, Transform = new Transform { TimeValue = 25 }, TargetTimeMode = TimeMode.Duration }).Transform;
    Check(Math.Abs(equivalent.TimeValue - 48) < .000001, "Time entry conversion preserves duration");
    foreach (var stage in new[] { "Decoding", "Stretching", "Encoding" })
    {
        core.Start(new Request { Input = source, Output = output, Transform = new Transform { TimeValue = 25 }, Maps = pitchMap, Encoding = "24", Overwrite = true });
        var watch = Stopwatch.StartNew(); bool cancelled = false; Snapshot current;
        do
        {
            current = core.Poll();
            if (!cancelled && current.Stage.StartsWith(stage, StringComparison.Ordinal)) { core.Cancel(); cancelled = true; }
            if (watch.Elapsed.TotalSeconds > 90) throw new Exception("Cancellation timed out");
            await Task.Delay(5);
        } while (current.State is "Running" or "Cancelling");
        Check(cancelled && current.State == "Cancelled", "Cancellation during " + stage);
        Check(File.ReadAllText(output) == sentinel, "Existing output preserved after " + stage);
        Check(!Directory.EnumerateDirectories(temp, ".audiostretch-*").Any(), "Working audio cleaned after " + stage);
    }
    core.Start(new Request { Input = source, Output = output, TempoChange = 5.125, Encoding = "24", Overwrite = true });
    var completed = await Finish(core);
    Check(completed.State == "Completed", "Render replaces an explicitly approved destination: " + completed.Error);
    var rendered = await core.ProbeAsync(output);
    Check(Math.Abs(rendered.Duration - 60 / 1.05125) < .03, "Invariant tempo under German culture");
    Check(rendered.BitDepth == 24 && rendered.Channels == 2, "FLAC output precision/channels");

    core.Start(new Request { Input = source, Output = source, TempoChange = 5, Encoding = "float", Overwrite = true });
    Check((await Finish(core)).State == "Failed", "Source overwrite rejected");
    core.Start(new Request { Input = source, Output = output, TempoChange = 5, Encoding = "24" });
    Check((await Finish(core)).State == "Failed", "Unapproved overwrite rejected");

    File.WriteAllText(output, sentinel);
    core.Start(new Request { Input = source, Output = output, TempoChange = 25, Encoding = "24", Overwrite = true });
    while (core.Poll().Stage != "Stretching with Rubber Band R3/Finer") await Task.Delay(5);
    File.WriteAllText(output, "changed by another writer");
    Check((await Finish(core)).State == "Failed", "Changed destination rejected");
    Check(File.ReadAllText(output) == "changed by another writer", "Changed destination preserved");

    File.WriteAllText(output, sentinel);
    core.Start(new Request { Input = source, Output = output, Transform = new Transform { TimeValue = 25 }, Maps = pitchMap, Encoding = "24", Overwrite = true });
    while (core.Poll().Stage != "Stretching with Rubber Band R3/Finer") await Task.Delay(5);
    using (var changed = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { changed.Position = 1000; changed.WriteByte(123); }
    var changedSource = await Finish(core);
    Check(changedSource.State == "Failed" && changedSource.Error.Contains("source changed"), "Source mutation detected with maps");
    Check(File.ReadAllText(output) == sentinel && changedSource.ReceiptJson.Length == 0, "Source mutation preserves output and publishes no receipt");

    var unavailable = new CoreClient(Path.Combine(temp, "missing-tools"));
    using (unavailable)
    {
        unavailable.Start(new Request { Input = source, Output = Path.Combine(temp, "missing.wav"), TempoChange = 5, Encoding = "float" });
        Check((await Finish(unavailable)).State == "Failed", "Missing tools reported");
    }
    using (var shutdown = new CoreClient(Path.Combine(publish, "tools")))
    {
        shutdown.Start(new Request { Input = source, Output = Path.Combine(temp, "shutdown.wav"), TempoChange = -50, Encoding = "float" });
        await Task.Delay(100);
    }
    Check(!File.Exists(Path.Combine(temp, "shutdown.wav")) && !Directory.EnumerateDirectories(temp, ".audiostretch-*").Any(), "Shutdown cancels worker and cleans temporary audio");
    Check(!Process.GetProcessesByName("rubberband-r3").Any(), "No orphan Rubber Band process");
    Console.WriteLine("All managed/native integration checks passed.");
}
finally { Directory.Delete(temp, true); }

static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
static async Task<Snapshot> Finish(CoreClient core)
{
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed.TotalSeconds < 90)
    {
        var result = core.Poll();
        if (result.State is "Completed" or "Failed" or "Cancelled") return result;
        await Task.Delay(10);
    }
    throw new Exception("Worker timed out");
}
