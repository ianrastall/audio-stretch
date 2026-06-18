using NAudio.Wave;

namespace AudioStretch;

internal sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _out;
    private MediaFoundationReader? _reader;

    public TimeSpan TotalDuration => _reader?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan CurrentTime => _reader?.CurrentTime ?? TimeSpan.Zero;
    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;
    public bool IsLoaded => _reader is not null;

    public void Load(string path)
    {
        Stop();
        _out?.Dispose();
        _reader?.Dispose();

        _reader = new MediaFoundationReader(path);
        _out = new WaveOutEvent();
        _out.Init(_reader);
    }

    public void PlayPause()
    {
        if (_out is null) return;
        if (_out.PlaybackState == PlaybackState.Playing)
            _out.Pause();
        else
            _out.Play();
    }

    public void Stop()
    {
        _out?.Stop();
        if (_reader is not null)
            try { _reader.CurrentTime = TimeSpan.Zero; } catch { }
    }

    public void Seek(TimeSpan position)
    {
        if (_reader is null) return;
        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero
                    : position > _reader.TotalTime ? _reader.TotalTime
                    : position;
        try { _reader.CurrentTime = clamped; } catch { }
    }

    public void Dispose()
    {
        _out?.Stop();
        _out?.Dispose();
        _reader?.Dispose();
    }
}
