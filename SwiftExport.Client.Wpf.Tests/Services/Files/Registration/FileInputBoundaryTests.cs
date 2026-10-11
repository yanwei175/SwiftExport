using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Services.Files.Reading;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Excel;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Registration;

/// <summary>通过公开读取接口验证输入类型和 XML 容器边界，避免仅凭文件扩展名接受不支持格式。</summary>
[TestClass]
public sealed class FileInputBoundaryTests
{
    /// <summary>空文件、旧版二进制头和普通 ZIP 都明确失败，且保持调用方流打开。</summary>
    [TestMethod]
    public async Task InspectAsync_ShouldRejectEmptyLegacyBinaryAndOrdinaryZip()
    {
        var inputs = new[]
        {
            Array.Empty<byte>(),
            new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 },
            PlainZip(),
        };
        foreach (var bytes in inputs)
        {
            using var stream = new MemoryStream(bytes);
            var exception = await Assert.ThrowsAsync<FileOperationException>(() =>
                new ClosedXmlExcelReadService().InspectAsync(stream));
            Assert.AreEqual("InvalidXlsx", exception.Code);
            Assert.IsTrue(stream.CanRead);
        }
    }

    /// <summary>宏工作簿即使由可读流提供也不被标准 XLSX 入口接受。</summary>
    [TestMethod]
    public async Task InspectAsync_ShouldRejectMacroWorkbookContentType()
    {
        using var stream = ExcelReadFixture.Create(book => book.AddWorksheet("Input").Cell("A1").Value = "header");
        ReplaceContentTypes(stream, text => text.Replace(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml", StringComparison.Ordinal));
        var exception = await Assert.ThrowsAsync<FileOperationException>(() =>
            new ClosedXmlExcelReadService().InspectAsync(stream));
        Assert.AreEqual("UnsupportedFileType", exception.Code);
        Assert.IsTrue(stream.CanRead);
    }

    /// <summary>容器 XML 不允许 DTD 和外部实体，失败分类不泄漏解析器细节给用户。</summary>
    [TestMethod]
    public async Task InspectAsync_ShouldRejectDtdBeforeWorkbookLoad()
    {
        using var stream = ExcelReadFixture.Create(book => book.AddWorksheet("Input").Cell("A1").Value = "header");
        ReplaceContentTypes(stream, _ =>
            "<!DOCTYPE Types [<!ENTITY content 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'>]>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/xl/workbook.xml\" ContentType=\"&content;\"/></Types>");
        var exception = await Assert.ThrowsAsync<FileOperationException>(() =>
            new ClosedXmlExcelReadService().InspectAsync(stream));
        Assert.AreEqual("InvalidXlsx", exception.Code);
    }

    /// <summary>构造不含 Open XML 工作簿的 ZIP，验证不能只检查 ZIP 魔数。</summary>
    private static byte[] PlainZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("plain.txt").Open()))
            writer.Write("ordinary archive");
        return stream.ToArray();
    }

    /// <summary>仅修改本测试的容器类型文件；结果流保持可复读，不改变真实工作簿渲染算法。</summary>
    private static void ReplaceContentTypes(MemoryStream stream, Func<string, string> replacement)
    {
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("[Content_Types].xml")!;
            string original;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                original = reader.ReadToEnd();
            using var output = entry.Open();
            output.SetLength(0);
            using var writer = new StreamWriter(output, new UTF8Encoding(false));
            writer.Write(replacement(original));
        }
        stream.Position = 0;
    }
}
