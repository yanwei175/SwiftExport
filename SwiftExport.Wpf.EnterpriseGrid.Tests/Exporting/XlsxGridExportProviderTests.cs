using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Exporting;

[TestClass]
public sealed class XlsxGridExportProviderTests
{
    [TestMethod]
    public async Task ExportAsync_ShouldCreateValidOpenXmlPartsAndNeverEmitTextAsFormula()
    {
        var request = new GridExportRequest(
            new[]
            {
                new GridExportColumn("text", "Text", null, 0, null),
                new GridExportColumn("number", "Number", null, 1, null),
                new GridExportColumn("bool", "Bool", null, 2, null),
                new GridExportColumn("date", "Date", null, 3, null)
            },
            new[]
            {
                new GridExportRow(new[]
                {
                    new GridExportCell("=SUM(A1:A2)", "=SUM(A1:A2)"),
                    new GridExportCell(-5.5m, "-5.5"),
                    new GridExportCell(true, "True"),
                    new GridExportCell(new DateTime(2026, 7, 11), "2026-07-11")
                })
            },
            new GridExportOptions { WorksheetName = "ERP/导出*测试" });
        await using var stream = new MemoryStream();

        await new XlsxGridExportProvider().ExportAsync(request, stream);

        Assert.IsTrue(stream.CanWrite);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.IsNotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.IsNotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.IsNotNull(archive.GetEntry("xl/styles.xml"));
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.IsNotNull(sheetEntry);
        using var sheetStream = sheetEntry.Open();
        var document = XDocument.Load(sheetStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.IsFalse(document.Descendants(ns + "f").Any());
        Assert.IsTrue(document.Descendants(ns + "t").Any(node => node.Value == "=SUM(A1:A2)"));
        Assert.IsTrue(document.Descendants(ns + "v").Any(node => node.Value == "-5.5"));
        Assert.IsTrue(document.Descendants(ns + "c").Any(cell => (string?)cell.Attribute("s") == "1"));

        using var workbookReader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
        var workbook = await workbookReader.ReadToEndAsync();
        StringAssert.Contains(workbook, "ERP_导出_测试");
    }

    [TestMethod]
    public async Task ExportAsync_ShouldRejectExcelLimitsBeforeEnumeratingRows()
    {
        var request = new GridExportRequest(
            Array.Empty<GridExportColumn>(),
            new CountOnlyRows(1_048_577),
            new GridExportOptions { IncludeHeaders = false });
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new XlsxGridExportProvider().ExportAsync(request, stream));
    }

    private sealed class CountOnlyRows(int count) : IReadOnlyList<GridExportRow>
    {
        public int Count { get; } = count;

        public GridExportRow this[int index] => throw new NotSupportedException();

        public IEnumerator<GridExportRow> GetEnumerator() => throw new NotSupportedException();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
