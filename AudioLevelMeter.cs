using NAudio.Wave;

namespace RGBCommander;

/// <summary>System-audio level meter for the Music effect: WASAPI loopback capture of
/// whatever is playing, reduced to one smoothed 0..1 level (fast attack, slow release).
/// Started lazily when a Music effect is active and stopped when it isn't, so the app
/// doesn't hold an audio session while idle.</summary>
public sealed class AudioLevelMeter : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private volatile float _level;
    private volatile float _bass;
    private float _lpState;            // one-pole low-pass state (~140 Hz, the kick/bass band)
    private int _sampleRate = 48000;
    private int _channels = 2;

    /// <summary>Current smoothed wideband level, 0..1. Zero while not capturing.</summary>
    public float Level => _level;

    /// <summary>Current smoothed bass-band level (low-passed ~140 Hz), 0..~0.5.</summary>
    public float Bass => _bass;

    public void Start()
    {
        if (_capture != null) return;
        try
        {
            var capture = new WasapiLoopbackCapture();
            _sampleRate = capture.WaveFormat.SampleRate;
            _channels = Math.Max(1, capture.WaveFormat.Channels);
            capture.DataAvailable += OnData;
            capture.StartRecording();
            _capture = capture;
            DebugLog.Log("audio meter: started");
        }
        catch (Exception ex)
        {
            DebugLog.Log("audio meter: start failed - " + ex.Message);
            _capture = null;
        }
    }

    public void Stop()
    {
        var capture = _capture;
        _capture = null;
        _level = 0;
        if (capture == null) return;
        try
        {
            capture.DataAvailable -= OnData;
            capture.StopRecording();
            capture.Dispose();
            DebugLog.Log("audio meter: stopped");
        }
        catch { }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        // Loopback delivers interleaved 32-bit float samples. Track the wideband peak
        // and a low-passed (bass) peak of the left channel — the bass is what makes a
        // visualizer feel on-beat.
        float peak = 0, bassPeak = 0;
        float a = (float)(1 - Math.Exp(-2 * Math.PI * 140.0 / _sampleRate));
        int step = 4 * _channels;
        float lp = _lpState;
        for (int i = 0; i + 3 < e.BytesRecorded; i += step)
        {
            float v = BitConverter.ToSingle(e.Buffer, i);
            lp += a * (v - lp);
            float bv = Math.Abs(lp);
            if (bv > bassPeak) bassPeak = bv;
            float av = Math.Abs(v);
            if (av > peak) peak = av;
        }
        _lpState = lp;
        float current = _level;
        _level = peak > current ? peak : current * 0.82f + peak * 0.18f;
        float curBass = _bass;
        _bass = bassPeak > curBass ? bassPeak : curBass * 0.78f + bassPeak * 0.22f;
    }

    public void Dispose() => Stop();
}
