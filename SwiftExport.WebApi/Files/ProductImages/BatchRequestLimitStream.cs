namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>在 multipart reader 下限制实际请求总量，且不拥有框架请求体。</summary>
internal sealed class BatchRequestLimitStream(Stream source, long limit) : Stream
{
    private long _consumed;

    public override bool CanRead => source.CanRead;
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
        ValidateBufferArguments(buffer, offset, count);
        if (count == 0) return 0;
        if (_consumed == limit)
        {
            if (source.ReadByte() < 0) return 0;
            throw new ProductImageBatchLimitExceededException();
        }

        var read = source.Read(buffer, offset, (int)Math.Min(count, limit - _consumed));
        _consumed += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        if (_consumed == limit)
        {
            var probe = new byte[1];
            if (await source.ReadAsync(probe.AsMemory(), cancellationToken).ConfigureAwait(false) == 0) return 0;
            throw new ProductImageBatchLimitExceededException();
        }

        var read = await source.ReadAsync(
            buffer[..(int)Math.Min(buffer.Length, limit - _consumed)],
            cancellationToken).ConfigureAwait(false);
        _consumed += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing) => base.Dispose(disposing);
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>表示 multipart 请求实际总量超过配置上限。</summary>
internal sealed class ProductImageBatchLimitExceededException : IOException;
