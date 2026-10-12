using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DvFileStorage.Abstractions;
using DvFileStorage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Application.Services.Files.ExportTemplates;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Application.Tests.Files.ExportTemplates;

/// <summary>验证流资源释放、读取限额、ZIP 包保护及读取中的取消，不依赖生产文件或数据库。</summary>
[TestClass]
public sealed class ExportTemplateResourceBoundaryTests
{
    /// <summary>清单与版本文件均按实际读取字节计数，不能依赖声明长度或可定位流。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ResourceLimit_ShouldBoundManifestAndTemplateActualBytes(bool manifest)
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item());
        var size = manifest ? 1024 * 1024 + 1 : 20 * 1024 * 1024 + 1;
        var path = manifest ? "模板目录.json" : "sales-standard/v1/模板.xlsx";
        var limitedStream = new ObservableStream(new byte[size]);
        var storage = new StorageOverride(scope.Storage, path, () => limitedStream);
        var result = await CreateService(scope, storage).ListAsync("SO", "SalesOrder.Export", 1);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.IsTrue(limitedStream.Disposed);
        Assert.IsFalse(result.Message.Contains(path, StringComparison.Ordinal));
    }

    /// <summary>底层读取故障释放输入流并只返回安全错误，异常路径留在日志中。</summary>
    [TestMethod]
    public async Task InputFailure_ShouldDisposeSourceAndHideTechnicalDetails()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item());
        var input = new ObservableStream([1]) { FailReads = true };
        var storage = new StorageOverride(scope.Storage, "模板目录.json", () => input);
        var result = await CreateService(scope, storage).ListAsync("SO", "SalesOrder.Export", 1);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.IsTrue(input.Disposed);
        Assert.IsFalse(result.Message.Contains("private-path", StringComparison.Ordinal));
    }

    /// <summary>读取过程中取消仍向调用方传播取消，输入流须释放，不能误报目录成功。</summary>
    [TestMethod]
    public async Task MidReadCancellation_ShouldDisposeInputAndPropagateCancellation()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item());
        using var cancellation = new CancellationTokenSource();
        var input = new ObservableStream([1, 2]) { OnRead = cancellation.Cancel };
        var storage = new StorageOverride(scope.Storage, "模板目录.json", () => input);
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService(scope, storage)
            .ListAsync("SO", "SalesOrder.Export", 1, cancellation.Token));
        Assert.IsTrue(input.Disposed);
    }

    /// <summary>摘要正确也不能绕过宏、重复部件、声明解压上限或禁止 DTD 的容器保护。</summary>
    [TestMethod]
    [DataRow("macro")]
    [DataRow("duplicate")]
    [DataRow("expanded")]
    [DataRow("dtd")]
    public async Task XlsxPackage_ShouldRejectUnsafeStructuresWithCorrectHash(string mode)
    {
        using var scope = new ExportTemplateTestScope();
        var bytes = AlterPackage(scope.Xlsx, mode);
        var item = scope.Item();
        item["fileLength"] = bytes.Length;
        item["sha256"] = Convert.ToHexString(SHA256.HashData(bytes));
        await scope.WriteAsync("sales-standard/v1/模板.xlsx", bytes);
        await scope.WriteManifestAsync(item);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Error.Code);
    }

    /// <summary>模板的公开结构校验不执行公式；合法元数据可包含非默认单据类型。</summary>
    [TestMethod]
    public async Task CustomDocumentType_ShouldRemainAllowed()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item(documentType: "CUSTOM", contractKey: "Custom.Export"));
        var result = await scope.Service.ListAsync("CUSTOM", "Custom.Export", 1);
        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Data);
    }

    private static ExportTemplateCatalogService CreateService(ExportTemplateTestScope scope, IFileStorage storage) =>
        new(new ExportTemplateTestScope.StorageProvider { Context = new ExportTemplateStorageContext { Storage = storage } },
            scope.Policy, NullLogger<ExportTemplateCatalogService>.Instance);

    /// <summary>按测试场景修改真实 ZIP 部件；解压上限场景只改中央目录声明，避免为保护测试分配百兆内容。</summary>
    private static byte[] AlterPackage(byte[] original, string mode)
    {
        using var input = new MemoryStream(original);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                if (mode == "dtd" && entry.FullName == "[Content_Types].xml")
                    text = "<!DOCTYPE Types [<!ENTITY forbidden 'invalid'>]>" + text;
                ExportTemplateTestScope.WriteEntry(archive, entry.FullName, text);
            }
            if (mode == "macro") ExportTemplateTestScope.WriteEntry(archive, "xl/vbaProject.bin", "macro");
            if (mode == "duplicate") ExportTemplateTestScope.WriteEntry(archive, "xl/workbook.xml", "duplicate");
            if (mode == "expanded") ExportTemplateTestScope.WriteEntry(archive, "large.bin", "small");
        }
        var bytes = output.ToArray();
        if (mode == "expanded")
        {
            for (var offset = 0; offset <= bytes.Length - 46; offset++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) != 0x02014b50) continue;
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
                if (offset + 46 + nameLength > bytes.Length || Encoding.UTF8.GetString(bytes, offset + 46, nameLength) != "large.bin") continue;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24, 4), 101 * 1024 * 1024);
                return bytes;
            }
            throw new InvalidOperationException("测试 ZIP 缺少待修改的中央目录条目。");
        }
        return bytes;
    }

    /// <summary>只替换指定读取资源，其他操作仍落在本次测试独占的存储中。</summary>
    private sealed class StorageOverride(IFileStorage inner, string targetPath, Func<Stream> open) : IFileStorage
    {
        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            path == targetPath ? Task.FromResult(open()) : inner.OpenReadAsync(path, cancellationToken);
        /// <inheritdoc />
        public Task WriteAsync(string path, Stream source, bool overwrite = false, CancellationToken cancellationToken = default) => inner.WriteAsync(path, source, overwrite, cancellationToken);
        /// <inheritdoc />
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => inner.ExistsAsync(path, cancellationToken);
        /// <inheritdoc />
        public Task<StoredFileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default) => inner.GetInfoAsync(path, cancellationToken);
        /// <inheritdoc />
        public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default) => inner.DeleteIfExistsAsync(path, cancellationToken);
    }

    /// <summary>观察释放和真实读取故障，以非定位流验证服务不依赖 Position/Length。</summary>
    private sealed class ObservableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        internal bool Disposed { get; private set; }
        internal bool FailReads { get; init; }
        internal Action? OnRead { get; init; }
        /// <inheritdoc />
        public override bool CanRead => true;
        /// <inheritdoc />
        public override bool CanSeek => false;
        /// <inheritdoc />
        public override bool CanWrite => false;
        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();
        /// <inheritdoc />
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (FailReads) throw new IOException("private-path 读取失败");
            OnRead?.Invoke();
            return await _inner.ReadAsync(buffer, cancellationToken);
        }
        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <inheritdoc />
        public override void Flush() => throw new NotSupportedException();
        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
