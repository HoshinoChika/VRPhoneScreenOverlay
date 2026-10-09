using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VRPhoneScreenOverlay.Media;

internal sealed class WasapiAudioSink(
    BufferedWaveProvider buffer,
    WasapiPlayer player) : IAudioSink
{
    private readonly BufferedWaveProvider _buffer = buffer;
    private readonly WasapiPlayer _player = player;
    private bool _disposed;

    public string OutputDeviceId => _player.DeviceId ?? "windows-default";

    public TimeSpan BufferedDuration => _buffer.BufferedDuration;

    public static async ValueTask<WasapiAudioSink> CreateAsync()
    {
        BufferedWaveProvider buffer = new(
            new WaveFormat(48_000, 16, 2),
            TimeSpan.FromMilliseconds(200))
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        WasapiPlayer player = await new WasapiPlayerBuilder()
            .WithDefaultDeviceStreamRouting()
            .WithLatency(60)
            .WithEventSync()
            .BuildAsync().ConfigureAwait(false);
        try
        {
            player.Init(buffer);
            player.Play();
            return new WasapiAudioSink(buffer, player);
        }
        catch
        {
            await player.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<short> interleavedPcm,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _buffer.AddSamples(MemoryMarshal.AsBytes(interleavedPcm.Span));
        return ValueTask.CompletedTask;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _buffer.ClearBuffer();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _player.DisposeAsync().ConfigureAwait(false);
    }
}
