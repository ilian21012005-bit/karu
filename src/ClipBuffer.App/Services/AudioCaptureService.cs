using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ClipBuffer.App.Services;

public sealed class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _loopback;
    private WasapiCapture? _mic;
    private BufferedWaveProvider? _gameBuffer;
    private BufferedWaveProvider? _micBuffer;
    private IWaveProvider? _mixed;
    private bool _disposed;

    public void Start()
    {
        var inputs = new List<ISampleProvider>();

        try
        {
            _loopback = new WasapiLoopbackCapture();
            _gameBuffer = CreateBuffer(_loopback.WaveFormat);
            _loopback.DataAvailable += (_, e) => _gameBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _loopback.StartRecording();
            inputs.Add(ToStereo48k(_gameBuffer));
        }
        catch
        {
            DisposeCapture(ref _loopback);
        }

        try
        {
            _mic = new WasapiCapture();
            _micBuffer = CreateBuffer(_mic.WaveFormat);
            _mic.DataAvailable += (_, e) => _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _mic.StartRecording();
            inputs.Add(ToStereo48k(_micBuffer));
        }
        catch
        {
            DisposeCapture(ref _mic);
        }

        if (inputs.Count == 0)
        {
            _mixed = new SilenceProvider(new WaveFormat(48000, 16, 2));
            return;
        }

        ISampleProvider mix = inputs.Count == 1
            ? inputs[0]
            : new MixingSampleProvider(new[]
            {
                inputs[0],
                new VolumeSampleProvider(inputs[1]) { Volume = 0.7f }
            })
            {
                ReadFully = true
            };

        _mixed = new SampleToWaveProvider16(mix);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (_mixed is null)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        var read = _mixed.Read(buffer, offset, count);
        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCapture(ref _loopback);
        DisposeCapture(ref _mic);
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
            else if (capture is WasapiCapture wasapi)
            {
                wasapi.StopRecording();
            }
        }
        catch
        {
            // ignore
        }

        capture?.Dispose();
        capture = null;
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
