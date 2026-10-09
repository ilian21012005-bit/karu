using LibVLCSharp.Shared;

namespace Karu.App.Services;

/// <summary>
/// Lecteur VLC pour la revue de clips (son jeu fiable, contrairement à MediaElement).
/// Coût perf uniquement pendant la lecture dans l'UI — 0 impact buffer / en jeu.
/// </summary>
public sealed class ClipPlaybackController : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private bool _disposed;
    private string? _path;

    public ClipPlaybackController()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--avcodec-hw=d3d11va",
            "--aout=wasapi");
        _player = new MediaPlayer(_libVlc);
        _player.EndReached += (_, _) => EndReached?.Invoke();
        _player.EncounteredError += (_, _) =>
            EncounteredError?.Invoke("Erreur de lecture VLC.");
    }

    public MediaPlayer MediaPlayer => _player;

    public string? Path => _path;

    public bool HasMedia => !string.IsNullOrWhiteSpace(_path);

    public bool IsPlaying => _player.IsPlaying;

    public TimeSpan Duration =>
        _player.Length > 0 ? TimeSpan.FromMilliseconds(_player.Length) : TimeSpan.Zero;

    public bool HasDuration => _player.Length > 0;

    public TimeSpan Position
    {
        get => TimeSpan.FromMilliseconds(Math.Max(0, _player.Time));
        set
        {
            if (!HasDuration)
            {
                return;
            }

            var ms = (long)Math.Clamp(value.TotalMilliseconds, 0, _player.Length);
            _player.Time = ms;
        }
    }

    /// <summary>0–1</summary>
    public double Volume
    {
        get => Math.Clamp(_player.Volume / 100.0, 0, 1);
        set => _player.Volume = (int)Math.Clamp(Math.Round(value * 100), 0, 100);
    }

    public event Action? EndReached;
    public event Action<string>? EncounteredError;

    public void Open(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _path = path;
        using var media = new Media(_libVlc, path, FromType.FromPath);
        _player.Media = media;
    }

    public void Play()
    {
        if (!HasMedia)
        {
            return;
        }

        _player.Play();
    }

    public void Pause()
    {
        if (_player.CanPause)
        {
            _player.SetPause(true);
        }
    }

    public void Stop()
    {
        try
        {
            _player.Stop();
        }
        catch
        {
            // ignore
        }
    }

    public void Clear()
    {
        Stop();
        _player.Media?.Dispose();
        _player.Media = null;
        _path = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _player.Dispose();
        _libVlc.Dispose();
    }
}
