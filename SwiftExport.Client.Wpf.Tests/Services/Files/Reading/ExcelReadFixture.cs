using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Reading;

/// <summary>建立独立 XLSX 输入并修改文件中的公式缓存；不依赖安装 Excel 或触发计算引擎。</summary>
internal static class ExcelReadFixture
{
    /// <summary>把测试数据保存成真实文件流，调用方拥有返回流。</summary>
    internal static MemoryStream Create(Action<XLWorkbook> arrange)
    {
        using var workbook = new XLWorkbook();
        arrange(workbook);
        var stream = new MemoryStream();
        workbook.SaveAs(stream, new ClosedXML.Excel.SaveOptions { EvaluateFormulasBeforeSaving = false });
        stream.Position = 0;
        return stream;
    }

    /// <summary>直接设置指定工作表的 Open XML 缓存值，用错误或缺失缓存证明读取服务不会重新计算公式。</summary>
    internal static void SetFormulaCache(MemoryStream stream, int worksheetNumber, string address, string? value, string? type = null)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry($"xl/worksheets/sheet{worksheetNumber}.xml")!;
            XDocument document;
            using (var input = entry.Open())
                document = XDocument.Load(input);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cell = document.Descendants(ns + "c").Single(element => (string?)element.Attribute("r") == address);
            cell.SetAttributeValue("t", type);
            cell.Elements(ns + "v").Remove();
            if (value is not null)
                cell.Add(new XElement(ns + "v", value));
            using var output = entry.Open();
            output.SetLength(0);
            document.Save(output);
        }
        stream.Position = 0;
    }
}
