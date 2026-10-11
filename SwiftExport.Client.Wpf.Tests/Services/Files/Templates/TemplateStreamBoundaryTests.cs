using System.IO;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Templates;

/// <summary>验证模板输入快照、非定位流、读取及输出错误与数据模型安全边界。</summary>
[TestClass]
public sealed class TemplateStreamBoundaryTests
{
    /// <summary>验证非定位输入的内部副本使 Report 不依赖调用方流定位能力。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldReadNonSeekableTemplate()
    {
        using var fixture = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var bytes = new MemoryStream();
        await fixture.CopyToAsync(bytes);
        using var template = new NonSeekableInput(bytes.ToArray());
        using var target = new MemoryStream();
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData());
        Assert.IsTrue(template.CanRead);
        target.Position = 0;
        using var workbook = new XLWorkbook(target);
        Assert.AreEqual("测试订单", workbook.Worksheet("订单").Cell("B1").CachedValue.GetText());
    }

    /// <summary>验证有前缀的输入只消耗当前位置之后的 XLSX 数据。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldReadSeekableInputFromCurrentPosition()
    {
        using var fixture = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var template = new MemoryStream();
        template.Write([8, 9, 10, 11]);
        await fixture.CopyToAsync(template);
        template.Position = 4;
        using var target = new MemoryStream();
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData());
        Assert.AreEqual(template.Length, template.Position);
        Assert.IsTrue(template.CanRead);
        Assert.IsTrue(target.Length > 0);
    }

    /// <summary>验证流复制失败时返回稳定分类；目标可能含部分数据，调用方仍负责释放。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldClassifyPartialTargetWriteFailureAndLeaveStreamsOpen()
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new FailingOutput();
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(template, target, TemplateTestFixture.DetailData()));
        Assert.AreEqual("OutputWriteFailed", exception.Code);
        Assert.IsInstanceOfType<IOException>(exception.InnerException);
        Assert.IsTrue(target.Length > 0);
        Assert.IsTrue(target.CanWrite);
        Assert.IsTrue(template.CanRead);
    }

    /// <summary>验证读取错误被归一化且目标未被提前写入。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldClassifyInputReadFailure()
    {
        using var template = new FailingInput();
        using var target = new MemoryStream();
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(template, target, TemplateTestFixture.DetailData()));
        Assert.AreEqual("ReadFailed", exception.Code);
        Assert.IsInstanceOfType<IOException>(exception.InnerException);
        Assert.AreEqual(0, target.Length);
        Assert.IsTrue(template.CanRead);
    }

    /// <summary>验证共享输入输出流在消费前被拒绝，避免拼接损坏原模板。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldRejectSameTemplateAndTargetStream()
    {
        using var fixture = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var stream = new MemoryStream();
        await fixture.CopyToAsync(stream);
        var original = stream.ToArray();
        stream.Position = 0;
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(stream, stream, TemplateTestFixture.DetailData()));
        Assert.AreEqual("InvalidOutputStream", exception.Code);
        CollectionAssert.AreEqual(original, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>验证复制变量字典后，调用方增删键不能改变已建立的数据快照。</summary>
    [TestMethod]
    public void ExcelTemplateData_ShouldCopyTopLevelVariableDictionary()
    {
        var variables = new Dictionary<string, object?> { ["Title"] = "快照标题" };
        var snapshot = new ExcelTemplateData(variables);
        variables["Title"] = "后续标题";
        variables["Other"] = "后续字段";
        Assert.AreEqual("快照标题", snapshot.Variables["Title"]);
        Assert.AreEqual(1, snapshot.Variables.Count);
    }

    /// <summary>验证循环对象树不能导致无界校验递归。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldRejectCyclicDataTree()
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        var node = new SnapshotNode();
        node.Next = node;
        var data = new ExcelTemplateData(new Dictionary<string, object?> { ["Node"] = node });
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(template, target, data));
        Assert.AreEqual("TemplateDataCycle", exception.Issues.Single().Code);
        Assert.AreEqual("Node.Next", exception.Issues.Single().FieldKey);
        Assert.AreEqual(0, template.Position);
    }

    /// <summary>验证延迟枚举必须先形成稳定快照，防止校验和渲染读取不同数据。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldRejectDeferredEnumerable()
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        var data = new ExcelTemplateData(new Dictionary<string, object?> { ["Items"] = Enumerable.Range(1, 3).Select(n => new { Number = n }) });
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(template, target, data));
        Assert.AreEqual("TemplateDataNotSnapshot", exception.Issues.Single().Code);
        Assert.AreEqual(0, template.Position);
    }

    /// <summary>提供测试专用循环节点，不包含客户端绑定状态。</summary>
    private sealed class SnapshotNode
    {
        public SnapshotNode? Next { get; set; }
    }

    /// <summary>提供可读取但不支持位置操作的调用方流。</summary>
    private sealed class NonSeekableInput(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>仅由调用方释放时关闭内部读取缓冲。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>写入少量字节后模拟 I/O 失败。</summary>
    private sealed class FailingOutput : MemoryStream
    {
        /// <summary>覆盖内存流之间复制使用的同步数组写入入口。</summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, Math.Min(count, 13));
            throw new IOException("测试写入错误");
        }

        /// <summary>覆盖内存流之间复制使用的同步跨度写入入口。</summary>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer[..Math.Min(buffer.Length, 13)]);
            throw new IOException("测试写入错误");
        }

        /// <summary>覆盖 MemoryStream.CopyToAsync 使用的数组写入入口。</summary>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            base.Write(buffer, offset, Math.Min(count, 13));
            throw new IOException("测试写入错误");
        }

        /// <summary>模拟已发生部分写入后目标设备报错。</summary>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            base.Write(buffer.Span[..Math.Min(buffer.Length, 13)]);
            throw new IOException("测试写入错误");
        }
    }

    /// <summary>读取时模拟 I/O 失败，验证服务异常分类。</summary>
    private sealed class FailingInput : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new IOException("测试读取错误");
    }
}
