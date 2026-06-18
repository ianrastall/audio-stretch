using System.IO;

namespace AudioStretch;

internal static class ToolFinder
{
    public static string? Find(string name)
    {
        var exe = name + ".exe";

        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools", exe);
        if (File.Exists(toolsDir)) return toolsDir;

        var sameDir = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(sameDir)) return sameDir;

        var legacy = Path.Combine(@"D:\cli\tools", exe);
        if (File.Exists(legacy)) return legacy;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }
}
