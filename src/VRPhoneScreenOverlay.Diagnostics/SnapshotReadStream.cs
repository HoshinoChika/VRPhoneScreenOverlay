namespace VRPhoneScreenOverlay.Diagnostics;

// A fixed byte boundary prevents a concurrently growing log from extending collection.
internal sealed class SnapshotReadStream(Stream source, long length) : Stream
{
    private long _position;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, (int)Math.Min(count, length - _position));
        _position += read; return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, length - _position)], cancellationToken).ConfigureAwait(false);
        _position += read; return read;
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
