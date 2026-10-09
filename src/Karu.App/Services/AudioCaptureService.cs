using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Karu.App.Services;

/// <summary>
/// Capture jeu (loopback) + micro (WASAPI). Rebranche à chaud si le périphérique
/// par défaut change ; silence si mute / aucun device — jamais de crash.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private static readonly WaveFormat OutputFormat = new(48000, 16, 2);

    private readonly object _gate = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceNotificationClient _notifications;
    private readonly System.Threading.Timer _debounce;
    private WasapiLoopbackCapture? _loopback;
    private WasapiCapture? _mic;
    private BufferedWaveProvider? _gameBuffer;
    private BufferedWaveProvider? _micBuffer;
    private IWaveProvider? _mixed;
    private readonly SilenceProvider _silence = new(OutputFormat);
    private string? _loopbackId;
    private string? _micId;
    private bool _disposed;

    public AudioCaptureService()
    {
        _notifications = new DeviceNotificationClient(ScheduleRebuild);
        _debounce = new System.Threading.Timer(_ => RebuildSafe(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(_notifications);
        }
        catch
        {
            // notifications optionnelles
        }
    }

    public void Start() => Rebuild(force: true);

    public int Read(byte[] buffer, int offset, int count)
    {
        IWaveProvider source;
        lock (_gate)
        {
            source = _mixed ?? _silence;
        }

        try
        {
            var read = source.Read(buffer, offset, count);
            if (read < count)
            {
                Array.Clear(buffer, offset + read, count - read);
            }

            return count;
        }
        catch
        {
            Array.Clear(buffer, offset, count);
            ScheduleRebuild();
            return count;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { /* ignore */ }
        _debounce.Dispose();
        lock (_gate)
        {
            TearDownUnlocked();
        }

        _enumerator.Dispose();
    }

    private void ScheduleRebuild()
    {
        if (_disposed)
        {
            return;
        }

        _debounce.Change(400, Timeout.Infinite);
    }

    private void RebuildSafe() => Rebuild(force: false);

    private void Rebuild(bool force)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                var loopId = TryDefaultId(DataFlow.Render, Role.Multimedia);
                var micId = TryDefaultId(DataFlow.Capture, Role.Communications)
                            ?? TryDefaultId(DataFlow.Capture, Role.Multimedia);

                if (!force &&
                    _mixed is not null &&
                    string.Equals(_loopbackId, loopId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_micId, micId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                TearDownUnlocked();
                _loopbackId = loopId;
                _micId = micId;

                var inputs = new List<ISampleProvider>();

                try
                {
                    _loopback = new WasapiLoopbackCapture();
                    _gameBuffer = CreateBuffer(_loopback.WaveFormat);
                    _loopback.DataAvailable += OnLoopbackData;
                    _loopback.RecordingStopped += (_, _) => ScheduleRebuild();
                    _loopback.StartRecording();
                    inputs.Add(ToStereo48k(_gameBuffer));
                }
                catch
                {
                    DisposeCapture(ref _loopback);
                    _gameBuffer = null;
                }

                try
                {
                    // Pas de micro / mute système : Wasapi peut échouer → on continue sans voix.
                    _mic = new WasapiCapture();
                    _micBuffer = CreateBuffer(_mic.WaveFormat);
                    _mic.DataAvailable += OnMicData;
                    _mic.RecordingStopped += (_, _) => ScheduleRebuild();
                    _mic.StartRecording();
                    inputs.Add(new VolumeSampleProvider(ToStereo48k(_micBuffer)) { Volume = 0.7f });
                }
                catch
                {
                    DisposeCapture(ref _mic);
                    _micBuffer = null;
                }

                if (inputs.Count == 0)
                {
                    _mixed = _silence;
                    return;
                }

                ISampleProvider mix = inputs.Count == 1
                    ? inputs[0]
                    : new MixingSampleProvider(inputs) { ReadFully = true };

                _mixed = new SampleToWaveProvider16(mix);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("audio rebuild: " + ex.Message);
            lock (_gate)
            {
                TearDownUnlocked();
                _mixed = _silence;
            }
        }
    }

    private void OnLoopbackData(object? sender, WaveInEventArgs e)
    {
        try { _gameBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded); }
        catch { ScheduleRebuild(); }
    }

    private void OnMicData(object? sender, WaveInEventArgs e)
    {
        try { _micBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded); }
        catch { ScheduleRebuild(); }
    }

    private void TearDownUnlocked()
    {
        DisposeCapture(ref _loopback);
        DisposeCapture(ref _mic);
        _gameBuffer = null;
        _micBuffer = null;
        _mixed = null;
    }

    private string? TryDefaultId(DataFlow flow, Role role)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    private static BufferedWaveProvider CreateBuffer(WaveFormat format) =>
        new(format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };

    private static ISampleProvider ToStereo48k(IWaveProvider source)
    {
        ISampleProvider sample = source.ToSampleProvider();
        if (sample.WaveFormat.Channels != 2)
        {
            sample = new StereoConvertSampleProvider(sample);
        }

        if (sample.WaveFormat.SampleRate != 48000)
        {
            sample = new WdlResamplingSampleProvider(sample, 48000);
        }

        return sample;
    }

    private static void DisposeCapture<T>(ref T? capture) where T : class, IDisposable
    {
        try
        {
            if (capture is IWaveIn waveIn)
            {
                waveIn.StopRecording();
            }
        }
        catch
        {
            // ignore
        }

        try { capture?.Dispose(); } catch { /* ignore */ }
        capture = null;
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action _changed;

        public DeviceNotificationClient(Action changed) => _changed = changed;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _changed();
        public void OnDeviceAdded(string pwstrDeviceId) => _changed();
        public void OnDeviceRemoved(string deviceId) => _changed();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (role is Role.Multimedia or Role.Communications)
            {
                _changed();
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // ignore (trop bruyant)
        }
    }
}

internal sealed class StereoConvertSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _temp = Array.Empty<float>();

    public StereoConvertSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        var needed = frames * _channels;
        if (_temp.Length < needed)
        {
            _temp = new float[needed];
        }

        var read = _source.Read(_temp, 0, needed);
        var framesRead = _channels == 0 ? 0 : read / _channels;
        for (var i = 0; i < framesRead; i++)
        {
            if (_channels == 1)
            {
                var sample = _temp[i];
                buffer[offset + i * 2] = sample;
                buffer[offset + i * 2 + 1] = sample;
            }
            else
            {
                buffer[offset + i * 2] = _temp[i * _channels];
                buffer[offset + i * 2 + 1] = _temp[i * _channels + 1];
            }
        }

        return framesRead * 2;
    }
}
