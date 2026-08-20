namespace Api.Modules.Media;

/// <summary>
/// Thrown when a streamed upload runs past <c>Media:MaxFileMb</c>.
/// </summary>
public sealed class MediaTooLargeException : Exception
{
    public MediaTooLargeException()
        : base("The uploaded file exceeds the configured size limit.")
    {
    }

    public MediaTooLargeException(string message)
        : base(message)
    {
    }

    public MediaTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Counts bytes as they pass and throws <see cref="MediaTooLargeException"/> the moment the cap is
/// crossed.
///
/// The cap has to be enforced *inside* the copy, not around it: the size of a streamed upload is not
/// known until it has been read, and a `Content-Length` is a claim by the caller (slice 1.5's
/// body-size middleware learned the same thing). Tripping mid-write leaves a partial blob, which the
/// upload path deletes on the way out and §7.3's orphan sweep collects if that delete fails too.
/// </summary>
internal sealed class LimitedStream(Stream inner, long maxBytes) : Stream
{
    public long BytesRead { get; private set; }

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

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

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

    private int Count(int read)
    {
        BytesRead += read;

        if (BytesRead > maxBytes)
        {
            throw new MediaTooLargeException(
                $"The uploaded file exceeds the {maxBytes}-byte limit (Media:MaxFileMb).");
        }

        return read;
    }
}
