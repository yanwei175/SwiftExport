using DvFileStorage.Configuration;
using DvFileStorage.Exceptions;
using DvFileStorage.Local;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DvFileStorage.Tests;

/// <summary>验证本地存储的流所有权、路径边界、发布并发和失败清理语义。</summary>
[TestClass]
public sealed class LocalFileStorageTests
{
    /// <summary>验证非 seek 源流从当前位置流式写入，信息不泄漏根目录，输入输出流所有权清晰。</summary>
    [TestMethod]
    public async Task WriteReadInfo_ShouldRoundTripWithoutOwningSourceOrBufferingWholeFile()
    {
        await using var scope = new StorageScope();
        var bytes = new byte[256 * 1024];
        new Random(17).NextBytes(bytes);
        using var underlying = new MemoryStream(bytes) { Position = 3 };
        using var source = new NonSeekReadStream(underlying);

        await scope.Storage.WriteAsync("中文 目录/数据.bin", source);

        Assert.IsFalse(source.IsDisposed, "写入服务不得释放调用方源流。");
        Assert.IsTrue(
            source.MaximumRequestedCount < bytes.Length - 3,
            "写入必须分块读取，不得一次请求整个大文件。");
        var info = await scope.Storage.GetInfoAsync("中文 目录/数据.bin");
        Assert.IsNotNull(info);
        Assert.AreEqual("中文 目录/数据.bin", info.Path);
        Assert.AreEqual(bytes.Length - 3, info.Length);
        Assert.AreEqual(TimeSpan.Zero, info.LastModifiedUtc.Offset);
        Assert.IsFalse(info.Path.Contains(scope.Root, StringComparison.OrdinalIgnoreCase));

        var read = await scope.Storage.OpenReadAsync("中文 目录/数据.bin");
        await using (read)
        {
            CollectionAssert.AreEqual(bytes[3..], await ReadAllAsync(read));
        }

        Assert.ThrowsExactly<ObjectDisposedException>(() => read.ReadByte());
        using var empty = new MemoryStream();
        await scope.Storage.WriteAsync("empty.bin", empty);
        Assert.AreEqual(0L, (await scope.Storage.GetInfoAsync("empty.bin"))!.Length);
    }

    /// <summary>验证默认拒绝覆盖且保留旧内容，显式覆盖可在旧读取流持有期间完整发布。</summary>
    [TestMethod]
    public async Task Overwrite_ShouldBeExplicitAndKeepOldContentOnRejection()
    {
        await using var scope = new StorageScope();
        await WriteBytesAsync(scope.Storage, "same.bin", [1, 2, 3]);

        using var rejected = new MemoryStream([9, 9]);
        await Assert.ThrowsExactlyAsync<FileAlreadyExistsException>(
            () => scope.Storage.WriteAsync("same.bin", rejected));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await ReadBytesAsync(scope.Storage, "same.bin"));

        await using var oldRead = await scope.Storage.OpenReadAsync("same.bin");
        await WriteBytesAsync(scope.Storage, "same.bin", [4, 5, 6, 7], overwrite: true);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await ReadAllAsync(oldRead));
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, await ReadBytesAsync(scope.Storage, "same.bin"));
    }

    /// <summary>验证同路径并发且不允许覆盖时，文件系统发布保证只有一个写入成功。</summary>
    [TestMethod]
    public async Task ConcurrentCreateWithoutOverwrite_ShouldHaveSingleWinner()
    {
        await using var scope = new StorageScope();
        var tasks = Enumerable.Range(1, 12).Select(async value =>
        {
            using var source = new MemoryStream([checked((byte)value)]);
            try
            {
                await scope.Storage.WriteAsync("race.bin", source);
                return true;
            }
            catch (FileAlreadyExistsException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);

        Assert.AreEqual(1, results.Count(result => result));
        Assert.AreEqual(1, (await ReadBytesAsync(scope.Storage, "race.bin")).Length);

        await WriteBytesAsync(scope.Storage, "overwrite-race.bin", [0]);
        var versions = Enumerable.Range(1, 4)
            .Select(value => Enumerable.Repeat(checked((byte)value), 64 * 1024).ToArray())
            .ToArray();
        await Task.WhenAll(versions.Select(version =>
            WriteBytesAsync(scope.Storage, "overwrite-race.bin", version, overwrite: true)));
        var published = await ReadBytesAsync(scope.Storage, "overwrite-race.bin");
        Assert.IsTrue(versions.Any(version => version.SequenceEqual(published)),
            "并发覆盖的最终文件必须是某个写入者的完整版本，不能混合内容。");
        AssertTemporaryDirectoryEmpty(scope.Root);
    }

    /// <summary>验证缺失约定和目录类型错误，不把 I/O 类型故障伪装成文件不存在。</summary>
    [TestMethod]
    public async Task MissingAndDirectoryTargets_ShouldHaveDistinctSemantics()
    {
        await using var scope = new StorageScope();

        Assert.IsFalse(await scope.Storage.ExistsAsync("missing.bin"));
        Assert.IsNull(await scope.Storage.GetInfoAsync("missing.bin"));
        await scope.Storage.DeleteIfExistsAsync("missing.bin");
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => scope.Storage.OpenReadAsync("missing.bin"));

        Directory.CreateDirectory(Path.Combine(scope.Root, "directory"));
        await Assert.ThrowsExactlyAsync<IOException>(() => scope.Storage.ExistsAsync("directory"));
        await Assert.ThrowsExactlyAsync<IOException>(() => scope.Storage.GetInfoAsync("directory"));
        await Assert.ThrowsExactlyAsync<IOException>(() => scope.Storage.DeleteIfExistsAsync("directory"));
    }

    /// <summary>验证绝对路径、穿越、空段、ADS、保留目录和 Windows 不稳定名称均被拒绝。</summary>
    [TestMethod]
    public async Task Paths_ShouldRemainInsideRootAndRejectReservedForms()
    {
        await using var scope = new StorageScope();
        var sibling = scope.Root + "-sibling";
        Directory.CreateDirectory(sibling);
        try
        {
            var invalidPaths = new[]
            {
                "", "../outside.bin", "/absolute.bin", "C:/absolute.bin", "a\\b.bin",
                "a//b.bin", "a/./b.bin", "a/../b.bin", "a/file.bin:stream",
                ".dvfilestorage-tmp/file.bin", ".DVFILESTORAGE-TMP/file.bin",
                "tail./file.bin", "tail /file.bin", "CON.txt", "CONIN$.txt",
                "folder/LPT1.log", "folder/COM¹.log"
            };
            foreach (var invalidPath in invalidPaths)
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(
                    () => scope.Storage.ExistsAsync(invalidPath),
                    invalidPath);
            }

            using var source = new MemoryStream([1]);
            await scope.Storage.WriteAsync("允许 空格/中文.txt", source);
            Assert.IsTrue(await scope.Storage.ExistsAsync("允许 空格/中文.txt"));
            Assert.IsFalse(File.Exists(Path.Combine(sibling, "outside.bin")));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    /// <summary>验证复制异常和发布前取消会清理模块临时文件，并保持原正式文件不变。</summary>
    [TestMethod]
    public async Task FailureAndCancellation_ShouldCleanupTemporaryFilesAndPreservePublishedFile()
    {
        await using var scope = new StorageScope();
        using var failing = new ThrowingReadStream();
        await Assert.ThrowsExactlyAsync<IOException>(
            () => scope.Storage.WriteAsync("failed.bin", failing));
        Assert.IsFalse(failing.IsDisposed);
        Assert.IsFalse(await scope.Storage.ExistsAsync("failed.bin"));
        AssertTemporaryDirectoryEmpty(scope.Root);

        await WriteBytesAsync(scope.Storage, "stable.bin", [1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        using var canceling = new CancelAtEndReadStream([8, 8, 8], cancellation);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => scope.Storage.WriteAsync("stable.bin", canceling, overwrite: true, cancellation.Token));
        Assert.IsFalse(canceling.IsDisposed);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await ReadBytesAsync(scope.Storage, "stable.bin"));
        AssertTemporaryDirectoryEmpty(scope.Root);
    }

    /// <summary>验证不同根目录实例互不串写，且同一文件可重复独立读取。</summary>
    [TestMethod]
    public async Task InstancesAndReadStreams_ShouldRemainIsolated()
    {
        await using var first = new StorageScope();
        await using var second = new StorageScope();
        await WriteBytesAsync(first.Storage, "same.bin", [1]);
        await WriteBytesAsync(second.Storage, "same.bin", [2]);

        CollectionAssert.AreEqual(new byte[] { 1 }, await ReadBytesAsync(first.Storage, "same.bin"));
        CollectionAssert.AreEqual(new byte[] { 1 }, await ReadBytesAsync(first.Storage, "same.bin"));
        CollectionAssert.AreEqual(new byte[] { 2 }, await ReadBytesAsync(second.Storage, "same.bin"));

        await first.Storage.DeleteIfExistsAsync("same.bin");
        Assert.IsFalse(await first.Storage.ExistsAsync("same.bin"));
        Assert.IsTrue(await second.Storage.ExistsAsync("same.bin"));
    }

    /// <summary>验证已有目录符号链接不会被跟随到根目录之外；环境禁止创建链接时明确标记未验证。</summary>
    [TestMethod]
    public async Task ReparsePoint_ShouldBeRejectedInsteadOfFollowed()
    {
        await using var scope = new StorageScope();
        var outside = Path.Combine(scope.Workspace, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "secret");
        var link = Path.Combine(scope.Root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Inconclusive($"当前环境不能创建目录符号链接：{exception.GetType().Name}");
        }

        await Assert.ThrowsExactlyAsync<IOException>(() => scope.Storage.OpenReadAsync("link/secret.txt"));
    }

    private static async Task WriteBytesAsync(
        LocalFileStorage storage,
        string path,
        byte[] bytes,
        bool overwrite = false)
    {
        using var source = new MemoryStream(bytes);
        await storage.WriteAsync(path, source, overwrite);
    }

    private static async Task<byte[]> ReadBytesAsync(LocalFileStorage storage, string path)
    {
        await using var stream = await storage.OpenReadAsync(path);
        return await ReadAllAsync(stream);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);
        return destination.ToArray();
    }

    private static void AssertTemporaryDirectoryEmpty(string root)
    {
        var path = Path.Combine(root, ".dvfilestorage-tmp");
        Assert.IsFalse(Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any());
    }

    /// <summary>创建彼此隔离且只由当前测试清理的存储根目录。</summary>
    private sealed class StorageScope : IAsyncDisposable
    {
        public StorageScope()
        {
            Workspace = Path.Combine(Path.GetTempPath(), "DvFileStorage.Tests", Guid.NewGuid().ToString("N"));
            Root = Path.Combine(Workspace, "root");
            Directory.CreateDirectory(Root);
            Storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = Root });
        }

        public string Workspace { get; }

        public string Root { get; }

        public LocalFileStorage Storage { get; }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Workspace, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>隐藏 seek 能力并记录服务请求的最大读取块，同时观察源流是否被错误释放。</summary>
    private sealed class NonSeekReadStream(Stream inner) : Stream
    {
        public bool IsDisposed { get; private set; }

        public int MaximumRequestedCount { get; private set; }

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
            MaximumRequestedCount = Math.Max(MaximumRequestedCount, count);
            return inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            MaximumRequestedCount = Math.Max(MaximumRequestedCount, buffer.Length);
            return inner.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>在输出少量数据后模拟源端 I/O 故障。</summary>
    private sealed class ThrowingReadStream : Stream
    {
        private bool _returnedData;

        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_returnedData)
            {
                return ValueTask.FromException<int>(new IOException("模拟源流故障。"));
            }

            _returnedData = true;
            buffer.Span[0] = 42;
            return ValueTask.FromResult(1);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>在报告流结束时触发取消，用于验证发布前取消提交点。</summary>
    private sealed class CancelAtEndReadStream(byte[] bytes, CancellationTokenSource cancellation) : Stream
    {
        private int _position;

        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position >= bytes.Length)
            {
                cancellation.Cancel();
                return ValueTask.FromResult(0);
            }

            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
