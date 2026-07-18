using Microsoft.Win32;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml;

namespace SwiftExportClient.Features.Shipping.PackingLists.Services;

public sealed class PackingListDocumentOutputService : IPackingListDocumentOutputService
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public async Task<string?> ExportAsync(PackingListExportDto data, CancellationToken cancellationToken = default)
    {
        var dialog = new SaveFileDialog { Title = "导出 PackingList", Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true, FileName = $"PackingList_{Safe(data.Header?.PackingListNo)}_{DateTime.Now:yyyyMMddHHmm}.xlsx" };
        if (dialog.ShowDialog() != true) return null;
        await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
        await Task.Run(() => WriteWorkbook(stream, data, cancellationToken), cancellationToken);
        return dialog.FileName;
    }

    public Task PrintAsync(PackingListExportDto data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        var document = CreatePrintDocument(data, dialog.PrintableAreaWidth);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, $"PackingList {data.Header?.PackingListNo}");
        return Task.CompletedTask;
    }

    private static FlowDocument CreatePrintDocument(PackingListExportDto data, double width)
    {
        var document = new FlowDocument { PageWidth = Math.Max(width, 600), PagePadding = new Thickness(32), FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 10 };
        document.Blocks.Add(new Paragraph(new Run($"PACKING LIST  {data.Header?.PackingListNo}")) { FontSize = 18, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        document.Blocks.Add(new Paragraph(new Run($"客户：{data.Header?.CustomerName}    Invoice：{data.Header?.InvoiceNo}    发货日期：{data.Header?.ShipmentDate:yyyy-MM-dd}")));
        var table = new Table { CellSpacing = 0 }; foreach (var widthValue in new[] { 110d, 110d, 260d, 70d, 90d }) table.Columns.Add(new TableColumn { Width = new GridLength(widthValue) });
        var group = new TableRowGroup(); table.RowGroups.Add(group); group.Rows.Add(Row("SKU", "客户型号", "描述", "单位", "数量"));
        foreach (var item in data.ProductSummary) group.Rows.Add(Row(item.Sku, item.CustomerSku, item.Description, item.Unit, item.Quantity.ToString("N4")));
        document.Blocks.Add(table); document.Blocks.Add(new Paragraph(new Run($"箱唛：{data.Header?.ShippingMark}\n柜号：{data.Header?.ContainerNo}    封条：{data.Header?.SealNo}")));
        return document;
    }

    private static TableRow Row(params string?[] values)
    {
        var row = new TableRow(); foreach (var value in values) row.Cells.Add(new TableCell(new Paragraph(new Run(value ?? string.Empty))) { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Padding = new Thickness(4) }); return row;
    }

    private static void WriteWorkbook(Stream target, PackingListExportDto data, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(target, ZipArchiveMode.Create, true);
        WriteText(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
            """);
        WriteText(archive, "_rels/.rels", """<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        WriteText(archive, "xl/workbook.xml", """<?xml version="1.0" encoding="utf-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="产品汇总" sheetId="1" r:id="rId1"/><sheet name="逐来源追溯" sheetId="2" r:id="rId2"/></sheets></workbook>""");
        WriteText(archive, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/></Relationships>""");
        WriteSheet(archive, "xl/worksheets/sheet1.xml",
            new[] { "SKU", "客户型号", "描述", "单位", "数量", "箱数", "净重", "毛重", "体积" },
            data.ProductSummary.Select(x => new object?[] { x.Sku, x.CustomerSku, x.Description, x.Unit, x.Quantity, x.PackageCount, x.NetWeight, x.GrossWeight, x.Volume }), cancellationToken);
        WriteSheet(archive, "xl/worksheets/sheet2.xml",
            new[] { "行", "销售单", "销售行", "SKU", "数量", "供应商", "虚拟仓", "GrossDemand", "NetDemand", "Allocation" },
            data.SourceTrace.Select(x => new object?[] { x.LineNumber, x.SalesOrderNo, x.SalesOrderLineNumber, x.Sku, x.Quantity, x.SourceSupplierName, x.WarehouseName, x.GrossDemandId, x.NetDemandId, x.NetDemandAllocationId }), cancellationToken);
    }

    private static void WriteSheet(ZipArchive archive, string path, IReadOnlyList<string> headers, IEnumerable<object?[]> rows, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest); using var stream = entry.Open(); using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false) });
        writer.WriteStartDocument(); writer.WriteStartElement("worksheet", SpreadsheetNamespace); writer.WriteStartElement("sheetData", SpreadsheetNamespace); var rowIndex = 1;
        WriteRow(writer, rowIndex++, headers.Cast<object?>().ToArray()); foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); WriteRow(writer, rowIndex++, row); }
        writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IReadOnlyList<object?> values)
    {
        writer.WriteStartElement("row", SpreadsheetNamespace); writer.WriteAttributeString("r", rowIndex.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i]; if (value is null) continue; writer.WriteStartElement("c", SpreadsheetNamespace); writer.WriteAttributeString("r", $"{ColumnName(i + 1)}{rowIndex}");
            if (value is byte or short or int or long or float or double or decimal) { writer.WriteAttributeString("t", "n"); writer.WriteElementString("v", SpreadsheetNamespace, Convert.ToString(value, CultureInfo.InvariantCulture)); }
            else { writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is", SpreadsheetNamespace); writer.WriteElementString("t", SpreadsheetNamespace, Convert.ToString(value, CultureInfo.GetCultureInfo("zh-CN")) ?? string.Empty); writer.WriteEndElement(); }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string ColumnName(int index) { var result = string.Empty; while (index > 0) { index--; result = (char)('A' + index % 26) + result; index /= 26; } return result; }
    private static void WriteText(ZipArchive archive, string path, string text) { var entry = archive.CreateEntry(path, CompressionLevel.Fastest); using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)); writer.Write(text); }
    private static string Safe(string? value) { var text = string.IsNullOrWhiteSpace(value) ? "未命名" : value.Trim(); foreach (var ch in Path.GetInvalidFileNameChars()) text = text.Replace(ch, '_'); return text; }
}
