namespace SwiftExport.Application.Services.Files;

/// <summary>以不拥有源流的方式限制实际读取字节数，并在第一个超限字节到达时失败。</summary>
internal sealed class UploadLimitStream(Stream source, long limit) : Stream
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

    /// <summary>同步读取有限块；达到上限后额外探测一个字节以区分 exact limit 与 limit+1。</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (count == 0) return 0;
        if (_consumed == limit)
        {
            if (source.ReadByte() < 0) return 0;
            throw new FileUploadLimitExceededException(limit);
        }

        var read = source.Read(buffer, offset, (int)Math.Min(count, limit - _consumed));
        _consumed += read;
        return read;
    }

    /// <summary>异步读取有限块；探测超限时只读取一个额外字节且透传取消令牌。</summary>
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        if (_consumed == limit)
        {
            var probe = new byte[1];
            if (await source.ReadAsync(probe.AsMemory(), cancellationToken).ConfigureAwait(false) == 0)
                return 0;
            throw new FileUploadLimitExceededException(limit);
        }

        var read = await source.ReadAsync(
            buffer[..(int)Math.Min(buffer.Length, limit - _consumed)],
            cancellationToken).ConfigureAwait(false);
        _consumed += read;
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>释放限制包装器但不释放调用方拥有的源流。</summary>
    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    /// <summary>异步释放限制包装器但不释放调用方拥有的源流。</summary>
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
