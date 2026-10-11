using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Templates;

/// <summary>读取固定嵌入模板并准备与业务无关的稳定数据；不复制生产模板渲染算法。</summary>
internal static class TemplateTestFixture
{
    internal static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>每次返回新的模板流，隔离并行测试的位置和所有权。</summary>
    internal static Stream Open(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(name)!;
    }

    /// <summary>准备头与明细样例的固定字段快照。</summary>
    internal static ExcelTemplateData DetailData(string title = "测试订单", string note = "  +保留文本  ", int rows = 2)
    {
        return new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Title"] = title,
            ["Order"] = new { CustomerName = "客户甲" },
            ["Note"] = note,
            ["Lines"] = Enumerable.Range(1, rows).Select(index => new
            {
                Code = $"CODE-{index}", Quantity = index + 1, Price = 1.25m,
                Text = index == 1 ? "=SUM(1,2)" : "@业务文本"
            }).ToArray()
        });
    }

    /// <summary>从输出 ZIP 读取原始 XML，验证公式结构而不触发公式计算。</summary>
    internal static XDocument Xml(MemoryStream stream, string path)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var entry = archive.GetEntry(path)!.Open();
        return XDocument.Load(entry);
    }

    /// <summary>解析实际工作表文本，兼容共享字符串和内联富文本，避免把存储布局误当作渲染结果。</summary>
    internal static string[] TextValues(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = Array.Empty<string>();
        var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry is not null)
        {
            using var entry = sharedEntry.Open();
            sharedStrings = XDocument.Load(entry).Descendants(Spreadsheet + "si")
                .Select(si => string.Concat(si.Descendants(Spreadsheet + "t").Select(t => t.Value))).ToArray();
        }

        using var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var worksheet = XDocument.Load(worksheetEntry);
        return worksheet.Descendants(Spreadsheet + "c").Where(c => (string?)c.Attribute("t") is "s" or "inlineStr")
            .Select(c => (string?)c.Attribute("t") == "s"
                ? sharedStrings[int.Parse(c.Element(Spreadsheet + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture)]
                : string.Concat(c.Descendants(Spreadsheet + "t").Select(t => t.Value))).ToArray();
    }

    /// <summary>提供同步阶段进度接收方，令取消测试具有确定的阶段边界。</summary>
    internal sealed class StageProgress(Action<FileOperationProgress> callback) : IProgress<FileOperationProgress>
    {
        public void Report(FileOperationProgress value) => callback(value);
    }
}
