using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Services.Files.Templates;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Output;

/// <summary>验证模板导出的同目录原子发布、覆盖准入与失败清理。</summary>
[TestClass]
public sealed class TemplateFileOutputTests
{
    /// <summary>验证默认导出拒绝覆盖既有文件，且不消耗模板流。</summary>
    [TestMethod]
    public async Task ExportToFileAsync_ShouldRejectExistingFileUnlessOverwriteIsEnabled()
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath;
        await File.WriteAllTextAsync(path, "既有内容");
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportToFileAsync(template, path, TemplateTestFixture.DetailData()));
        Assert.AreEqual("OutputAlreadyExists", exception.Code);
        Assert.AreEqual("既有内容", await File.ReadAllTextAsync(path));
        Assert.AreEqual(0, template.Position);
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>验证显式覆盖及新文件导出均发布完整 ZIP 文件并清理临时文件。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExportToFileAsync_ShouldPublishCompleteXlsx(bool overwrite)
    {
        using var directory = new TestDirectory();
        if (overwrite)
            await File.WriteAllTextAsync(directory.FilePath, "旧内容");
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        await new ClosedXmlReportTemplateExportService().ExportToFileAsync(template, directory.FilePath,
            TemplateTestFixture.DetailData(), overwrite);
        var bytes = await File.ReadAllBytesAsync(directory.FilePath);
        Assert.AreEqual((byte)'P', bytes[0]);
        Assert.AreEqual((byte)'K', bytes[1]);
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
        Assert.IsTrue(template.CanRead);
    }

    /// <summary>验证临时文件写完而发布前取消时保留原文件，并删除临时文件。</summary>
    [TestMethod]
    public async Task ExportToFileAsync_ShouldPreserveOriginalWhenCanceledBeforePublish()
    {
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(directory.FilePath, "原文件");
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var cancellation = new CancellationTokenSource();
        var progress = new TemplateTestFixture.StageProgress(p =>
        {
            if (p.Stage == "PublishingOutput")
                cancellation.Cancel();
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ClosedXmlReportTemplateExportService()
            .ExportToFileAsync(template, directory.FilePath, TemplateTestFixture.DetailData(), true, progress, cancellation.Token));
        Assert.AreEqual("原文件", await File.ReadAllTextAsync(directory.FilePath));
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>验证完成反馈期间后到的取消不会把已发布文件描述为失败。</summary>
    [TestMethod]
    public async Task ExportToFileAsync_ShouldReturnSuccessAfterCommitEvenWhenCancellationArrives()
    {
        using var directory = new TestDirectory();
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var cancellation = new CancellationTokenSource();
        var progress = new TemplateTestFixture.StageProgress(p =>
        {
            if (p.Stage == "Completed")
                cancellation.Cancel();
        });
        await new ClosedXmlReportTemplateExportService().ExportToFileAsync(template, directory.FilePath,
            TemplateTestFixture.DetailData(), progress: progress, cancellationToken: cancellation.Token);
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsTrue(File.Exists(directory.FilePath));
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>验证损坏输入或非法业务文本不会替换既有文件。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExportToFileAsync_ShouldPreserveOriginalWhenGenerationFails(bool invalidData)
    {
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(directory.FilePath, "受保护原文件");
        using var template = invalidData ? TemplateTestFixture.Open("头与明细模板.xlsx") : new MemoryStream([1, 2, 3]);
        var data = invalidData ? TemplateTestFixture.DetailData(note: "<<delete>>") : TemplateTestFixture.DetailData();
        await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportToFileAsync(template, directory.FilePath, data, overwrite: true));
        Assert.AreEqual("受保护原文件", await File.ReadAllTextAsync(directory.FilePath));
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>验证生成后目标竞争由最终发布决定，默认模式仍不能覆盖后来创建的文件。</summary>
    [TestMethod]
    public async Task ExportToFileAsync_ShouldNotOverwriteFileCreatedBeforeFinalMove()
    {
        using var directory = new TestDirectory();
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        var progress = new TemplateTestFixture.StageProgress(p =>
        {
            if (p.Stage == "PublishingOutput")
                File.WriteAllText(directory.FilePath, "竞争文件");
        });
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportToFileAsync(template, directory.FilePath, TemplateTestFixture.DetailData(), progress: progress));
        Assert.AreEqual("OutputWriteFailed", exception.Code);
        Assert.AreEqual("竞争文件", await File.ReadAllTextAsync(directory.FilePath));
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>验证 Windows 独占文件锁导致发布失败时保留原字节并清理临时输出。</summary>
    [TestMethod]
    public async Task ExportToFileAsync_ShouldPreserveLockedOriginalAndRemoveTemporaryFile()
    {
        using var directory = new TestDirectory();
        var original = new byte[] { 7, 8, 9, 10 };
        await File.WriteAllBytesAsync(directory.FilePath, original);
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using (var locked = new FileStream(directory.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
                .ExportToFileAsync(template, directory.FilePath, TemplateTestFixture.DetailData(), overwrite: true));
            Assert.AreEqual("OutputWriteFailed", exception.Code);
        }
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(directory.FilePath));
        Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>验证不支持的输出扩展名在生成前被拒绝。</summary>
    [TestMethod]
    public async Task ExportToFileAsync_ShouldRejectNonXlsxTarget()
    {
        using var directory = new TestDirectory();
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => new ClosedXmlReportTemplateExportService()
            .ExportToFileAsync(template, System.IO.Path.Combine(directory.Path, "report.pdf"), TemplateTestFixture.DetailData()));
        Assert.AreEqual("UnsupportedOutputFormat", exception.Code);
        Assert.AreEqual(0, template.Position);
        Assert.AreEqual(0, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>每个测试拥有唯一目录，清理仅删除本目录的文件且不递归处理其他路径。</summary>
    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SwiftExport-TemplateTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public string FilePath => System.IO.Path.Combine(Path, "output.xlsx");

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Path))
                File.Delete(file);
            Directory.Delete(Path);
        }
    }
}
