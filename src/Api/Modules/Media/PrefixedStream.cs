namespace Api.Modules.Media;

/// <summary>
/// Replays a peeked prefix, then continues with the rest of the underlying stream.
///
/// §7.2 item 5 needs the file's leading bytes before the blob PUT — the bucket rules and the
/// resolution floor cannot be applied to bytes that have already gone to storage. Reading them
/// consumes them, so they are handed back in front of the remainder here. Bounded by
/// <see cref="MediaValidation.HeaderPrefixBytes"/>: the file itself is never buffered.
/// </summary>
internal sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream rest) : Stream
{
    private int _prefixPosition;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var fromPrefix = CopyPrefix(buffer);
        return fromPrefix > 0 ? fromPrefix : rest.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var fromPrefix = CopyPrefix(buffer.Span);
        return fromPrefix > 0
            ? ValueTask.FromResult(fromPrefix)
            : rest.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int CopyPrefix(Span<byte> buffer)
    {
        var remaining = prefix.Length - _prefixPosition;
        if (remaining <= 0 || buffer.IsEmpty)
        {
            return 0;
        }

        var take = Math.Min(remaining, buffer.Length);
        prefix.Span.Slice(_prefixPosition, take).CopyTo(buffer);
        _prefixPosition += take;
        return take;
    }
}
