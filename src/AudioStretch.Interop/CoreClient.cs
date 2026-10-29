using System.Runtime.InteropServices;
using System.Text;
using AudioStretch.Protocol;
using Google.Protobuf;
using Microsoft.Win32.SafeHandles;

namespace AudioStretch.Interop;

internal static partial class Native
{
    static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, _, _) =>
        name == "audio_stretch_core" ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "audio_stretch_core.dll")) : IntPtr.Zero);
    [LibraryImport("audio_stretch_core")] internal static partial uint audio_abi_version();
    [LibraryImport("audio_stretch_core")] internal static partial int audio_create(byte[] path, nuint len, out CoreHandle core);
    [LibraryImport("audio_stretch_core")] internal static partial int audio_call(CoreHandle core, byte[] input, nuint len, out ResultHandle result);
    [LibraryImport("audio_stretch_core")] internal static partial int audio_result_bytes(ResultHandle result, out IntPtr data, out nuint length);
    [LibraryImport("audio_stretch_core")] internal static partial void audio_destroy(IntPtr core);
    [LibraryImport("audio_stretch_core")] internal static partial void audio_result_free(IntPtr result);
}
internal sealed class CoreHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public CoreHandle() : base(true) { }
    protected override bool ReleaseHandle() { Native.audio_destroy(handle); return true; }
}
internal sealed class ResultHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ResultHandle() : base(true) { }
    protected override bool ReleaseHandle() { Native.audio_result_free(handle); return true; }
}
public sealed class CoreClient : IDisposable
{
    private readonly CoreHandle _handle;
    private readonly object _gate = new();
    private ulong _id;
    private bool _disposed;
    public CoreClient(string toolsDirectory)
    {
        if (Native.audio_abi_version() != 1) throw new InvalidOperationException("Audio core version mismatch. Reinstall AudioStretch.");
        var bytes = Encoding.UTF8.GetBytes(toolsDirectory);
        if (Native.audio_create(bytes, (nuint)bytes.Length, out _handle) != 0)
        { _handle?.Dispose(); throw new InvalidOperationException("The audio core could not start. Reinstall AudioStretch."); }
    }
    public Reply Call(Request request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            request.RequestId = ++_id;
            var bytes = request.ToByteArray();
            int status = Native.audio_call(_handle, bytes, (nuint)bytes.Length, out var result);
            using (result)
            {
                if (status != 0) throw new InvalidOperationException($"Audio core request failed ({status}). Restart AudioStretch.");
                if (Native.audio_result_bytes(result, out var data, out var length) != 0 || length > 2_097_152)
                    throw new InvalidOperationException("Invalid response from the audio core.");
                var copy = new byte[(int)length];
                if (copy.Length > 0) Marshal.Copy(data, copy, 0, copy.Length);
                var reply = Reply.Parser.ParseFrom(copy);
                if (reply.RequestId != request.RequestId) throw new InvalidOperationException("Audio core response identity mismatch.");
                if (reply.Error.Length > 0) throw new InvalidOperationException(reply.Error);
                return reply;
            }
        }
    }
    public Task<AudioInfo> ProbeAsync(string path) => Task.Run(() => Call(new Request { Operation = "probe", Input = path }).Info);
    public Snapshot Start(Request request) { request.Operation = "start"; return Call(request).Snapshot; }
    public Snapshot Poll() => Call(new Request { Operation = "poll" }).Snapshot;
    public void Cancel() => Call(new Request { Operation = "cancel" });
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _handle.Dispose(); } }
}
