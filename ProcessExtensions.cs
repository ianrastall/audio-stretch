using System.Diagnostics;

namespace AudioStretch;

internal static class ProcessExtensions
{
    /// <summary>
    /// Waits for the process to exit. If the token is cancelled, the entire
    /// process tree is killed before the cancellation propagates, so external
    /// tools (ffmpeg / rubberband) don't keep running in the background and
    /// holding locks on the temp files.
    /// </summary>
    public static async Task WaitForExitOrKillAsync(this Process p, CancellationToken ct)
    {
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    // Wait for the tree to actually die so its file locks are
                    // released before the caller cleans up temp files.
                    await p.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch { /* already exited or no longer accessible */ }
            throw;
        }
    }
}
