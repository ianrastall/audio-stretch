using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jupiter", "AudioStretch");
Directory.CreateDirectory(data);
string resultPath = Path.Combine(data, "update-result.json");
try
{
    if (args.Length != 4 || !int.TryParse(args[0], out int pid) || args[2].Length != 64 || Path.GetFileName(args[3]) != "AudioStretch.exe") throw new InvalidDataException("Invalid update request.");
    string path = Path.GetFullPath(args[1]);
    string allowed = Path.GetFullPath(Path.Combine(data, "updates", "downloads")) + Path.DirectorySeparatorChar;
    if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("AudioStretch-", StringComparison.Ordinal) || !path.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer is outside the AudioStretch update cache.");
    try { using var parent = Process.GetProcessById(pid); if (!parent.WaitForExit(120000)) throw new TimeoutException("AudioStretch did not close; update was not installed."); }
    catch (ArgumentException) { }
    using (var file = File.OpenRead(path)) if (!Convert.ToHexString(SHA256.HashData(file)).Equals(args[2], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer checksum changed; update was not installed.");
    var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
    foreach (string arg in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=" + Path.Combine(data, "update-install.log") }) start.ArgumentList.Add(arg);
    using var installer = Process.Start(start) ?? throw new IOException("Installer could not start.");
    installer.WaitForExit(); if (installer.ExitCode != 0) throw new IOException("Installer exited with " + installer.ExitCode);
    File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = true, Message = "AudioStretch update installed.", Date = DateTimeOffset.UtcNow }));
}
catch (Exception error)
{
    File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Message = error.Message, Date = DateTimeOffset.UtcNow }));
}
// Relaunch the installed app after both success and failure so the result is visible.
if (args.Length == 4 && Path.GetFileName(args[3]) == "AudioStretch.exe" && File.Exists(args[3]))
    Process.Start(new ProcessStartInfo(args[3]) { UseShellExecute = true });
