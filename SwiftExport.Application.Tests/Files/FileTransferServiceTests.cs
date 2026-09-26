using DvFileStorage.Configuration;
using DvFileStorage.Local;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Application.Services.Files;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Files;

/// <summary>验证文件用例的权限前置、流量限制、稳定引用和底层冲突映射。</summary>
[TestClass]
public sealed class FileTransferServiceTests
{
    /// <summary>授权上传从当前流位置读取并生成配置基址下的编码地址，读取返回同一字节。</summary>
    [TestMethod]
    public async Task AuthorizedUploadAndRead_ShouldRoundTripAndBuildStableReference()
    {
        await using var scope = new StorageScope(maxUploadBytes: 16);
        using var source = new MemoryStream([0, 1, 2, 3, 4]) { Position = 1 };

        var upload = await scope.Service.UploadAsync(
            "files", "中文 目录/a #.bin", source, contentLength: 4, overwrite: false);

        Assert.IsTrue(upload.Success);
        Assert.AreEqual("files/中文 目录/a #.bin", upload.Data.Reference);
        Assert.AreEqual(
            "https://example.test/base/api/files/files?path=%E4%B8%AD%E6%96%87%20%E7%9B%AE%E5%BD%95%2Fa%20%23.bin",
            upload.Data.ReadUrl);
        Assert.AreEqual(5L, source.Position);
        var read = await scope.Service.OpenReadAsync("files", "中文 目录/a #.bin");
        Assert.IsTrue(read.Success);
        await using (read.Data.Content)
        {
            using var destination = new MemoryStream();
            await read.Data.Content.CopyToAsync(destination);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, destination.ToArray());
        }
    }

    /// <summary>已知长度超限在读取主体前拒绝；无长度的 limit+1 在发布前失败并清理临时文件。</summary>
    [TestMethod]
    public async Task UploadLimit_ShouldRejectDeclaredAndStreamedOverflowBeforePublication()
    {
        await using var scope = new StorageScope(maxUploadBytes: 4);
        using var declaredSource = new TrackingReadStream([1, 2, 3, 4, 5]);

        var declared = await scope.Service.UploadAsync(
            "files", "declared.bin", declaredSource, contentLength: 5, overwrite: false);

        Assert.IsFalse(declared.Success);
        Assert.AreEqual(FileErrorCodes.UploadTooLarge, declared.Error.Code);
        Assert.AreEqual(0, declaredSource.BytesRead);
        Assert.IsFalse(File.Exists(Path.Combine(scope.Root, "declared.bin")));

        using var exactSource = new MemoryStream([1, 2, 3, 4]);
        var exact = await scope.Service.UploadAsync(
            "files", "exact.bin", exactSource, contentLength: null, overwrite: false);
        Assert.IsTrue(exact.Success);

        using var streamedSource = new MemoryStream([1, 2, 3, 4, 5]);
        var streamed = await scope.Service.UploadAsync(
            "files", "streamed.bin", streamedSource, contentLength: null, overwrite: false);
        Assert.IsFalse(streamed.Success);
        Assert.AreEqual(FileErrorCodes.UploadTooLarge, streamed.Error.Code);
        Assert.IsFalse(File.Exists(Path.Combine(scope.Root, "streamed.bin")));
        AssertTemporaryDirectoryEmpty(scope.Root);
    }

    /// <summary>权限拒绝先于容器解析和主体读取，保证拒绝路径零文件写入。</summary>
    [TestMethod]
    public async Task DeniedUpload_ShouldNotReadBodyOrTouchStorage()
    {
        await using var scope = new StorageScope(maxUploadBytes: 8, allowWrite: false);
        using var source = new TrackingReadStream([1, 2, 3]);

        var result = await scope.Service.UploadAsync(
            "unknown", "file.bin", source, contentLength: null, overwrite: false);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(0, source.BytesRead);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(scope.Root).Any());
    }

    /// <summary>默认冲突使用稳定错误码，显式覆盖成功且其他 I/O 不会被统一伪装为冲突。</summary>
    [TestMethod]
    public async Task Overwrite_ShouldRequireExplicitFlagAndMapOnlyStableConflict()
    {
        await using var scope = new StorageScope(maxUploadBytes: 8);
        using var first = new MemoryStream([1]);
        Assert.IsTrue((await scope.Service.UploadAsync("files", "same.bin", first, 1, false)).Success);

        using var rejected = new MemoryStream([2]);
        var conflict = await scope.Service.UploadAsync("files", "same.bin", rejected, 1, false);
        Assert.IsFalse(conflict.Success);
        Assert.AreEqual(FileErrorCodes.Conflict, conflict.Error.Code);

        using var replacement = new MemoryStream([3, 4]);
        var replaced = await scope.Service.UploadAsync("files", "same.bin", replacement, 2, true);
        Assert.IsTrue(replaced.Success);
        CollectionAssert.AreEqual(new byte[] { 3, 4 }, await File.ReadAllBytesAsync(Path.Combine(scope.Root, "same.bin")));
    }

    private static void AssertTemporaryDirectoryEmpty(string root)
    {
        var temporary = Path.Combine(root, ".dvfilestorage-tmp");
        Assert.IsFalse(Directory.Exists(temporary) && Directory.EnumerateFileSystemEntries(temporary).Any());
    }

    /// <summary>创建当前测试独占的真实本地容器并提供可控访问策略。</summary>
    private sealed class StorageScope : IAsyncDisposable
    {
        public StorageScope(long maxUploadBytes, bool allowWrite = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "SwiftExport-file-transfer-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var container = new FileContainer
            {
                Id = "files",
                Storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = Root }),
                MaxUploadBytes = maxUploadBytes,
                ApiBaseUri = new Uri("https://example.test/base/")
            };
            Service = new FileTransferService(
                new StubContainerProvider(container),
                new StubAccessPolicy(allowRead: true, allowWrite));
        }

        public string Root { get; }
        public FileTransferService Service { get; }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubContainerProvider(FileContainer container) : IFileContainerProvider
    {
        public bool TryGetContainer(string containerId, out FileContainer? result)
        {
            result = string.Equals(container.Id, containerId, StringComparison.Ordinal) ? container : null;
            return result is not null;
        }
    }

    private sealed class StubAccessPolicy(bool allowRead, bool allowWrite) : IFileAccessPolicy
    {
        public Task<ServiceResult<bool>> AuthorizeReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Decision(allowRead));

        public Task<ServiceResult<bool>> AuthorizeWriteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Decision(allowWrite));

        private static ServiceResult<bool> Decision(bool allowed) => allowed
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Fail(ServiceErrorCodes.Forbidden, "拒绝访问。");
    }

    /// <summary>记录服务是否读取请求主体，且不提供 seek 能力。</summary>
    private sealed class TrackingReadStream(byte[] content) : Stream
    {
        private int _position;
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = Math.Min(buffer.Length, content.Length - _position);
            if (count <= 0) return ValueTask.FromResult(0);
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
