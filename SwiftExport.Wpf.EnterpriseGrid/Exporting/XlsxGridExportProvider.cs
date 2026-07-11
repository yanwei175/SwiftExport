using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace SwiftExport.Wpf.EnterpriseGrid.Exporting;

/// <summary>零第三方依赖的最小 Open XML .xlsx 提供器。</summary>
public sealed class XlsxGridExportProvider : IGridExportProvider
{
    private const int MaximumRows = 1_048_576;
    private const int MaximumColumns = 16_384;

    /// <inheritdoc />
    public Task ExportAsync(
        GridExportRequest request,
        Stream target,
        IProgress<GridExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        var dataRows = request.Rows.Count + (request.Options.IncludeHeaders ? 1 : 0);
        if (dataRows > MaximumRows || request.Columns.Count > MaximumColumns)
        {
            throw new InvalidOperationException("导出数据超过 Excel 工作表的行列上限。");
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
            WriteTextEntry(archive, "[Content_Types].xml", ContentTypesXml);
            WriteTextEntry(archive, "_rels/.rels", PackageRelationshipsXml);
            WriteTextEntry(archive, "xl/workbook.xml", CreateWorkbookXml(SanitizeWorksheetName(request.Options.WorksheetName)));
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
            WriteTextEntry(archive, "xl/styles.xml", StylesXml);
            WriteWorksheet(archive, request, progress, cancellationToken);
        }, cancellationToken);
    }

    private static void WriteWorksheet(
        ZipArchive archive,
        GridExportRequest request,
        IProgress<GridExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            CloseOutput = false
        });
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        var excelRow = 1;
        if (request.Options.IncludeHeaders)
        {
            WriteRow(writer, excelRow++, request.Columns.Select(column => new GridExportCell(column.Header, column.Header)));
        }

        for (var rowIndex = 0; rowIndex < request.Rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(writer, excelRow++, request.Rows[rowIndex].Cells);
            progress?.Report(new GridExportProgress(rowIndex + 1, request.Rows.Count));
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IEnumerable<GridExportCell> cells)
    {
        writer.WriteStartElement("row", SpreadsheetNamespace);
        writer.WriteAttributeString("r", rowIndex.ToString(CultureInfo.InvariantCulture));
        var columnIndex = 0;
        foreach (var cell in cells)
        {
            columnIndex++;
            WriteCell(writer, $"{GetColumnName(columnIndex)}{rowIndex}", cell);
        }

        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, string reference, GridExportCell cell)
    {
        if (cell.RawValue is null && string.IsNullOrEmpty(cell.DisplayText))
        {
            return;
        }

        writer.WriteStartElement("c", SpreadsheetNamespace);
        writer.WriteAttributeString("r", reference);
        switch (cell.RawValue)
        {
            case bool boolean:
                writer.WriteAttributeString("t", "b");
                writer.WriteElementString("v", SpreadsheetNamespace, boolean ? "1" : "0");
                break;
            case DateTime dateTime:
                writer.WriteAttributeString("s", "1");
                writer.WriteElementString("v", SpreadsheetNamespace, dateTime.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteAttributeString("s", "1");
                writer.WriteElementString("v", SpreadsheetNamespace, dateTimeOffset.DateTime.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                writer.WriteAttributeString("t", "n");
                writer.WriteElementString("v", SpreadsheetNamespace, Convert.ToString(cell.RawValue, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is", SpreadsheetNamespace);
                writer.WriteStartElement("t", SpreadsheetNamespace);
                if (cell.DisplayText.Length != cell.DisplayText.Trim().Length)
                {
                    writer.WriteAttributeString("xml", "space", null, "preserve");
                }

                writer.WriteString(cell.DisplayText);
                writer.WriteEndElement();
                writer.WriteEndElement();
                break;
        }

        writer.WriteEndElement();
    }

    private static string GetColumnName(int index)
    {
        var result = string.Empty;
        while (index > 0)
        {
            index--;
            result = (char)('A' + (index % 26)) + result;
            index /= 26;
        }

        return result;
    }

    private static string SanitizeWorksheetName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "Data" : name.Trim();
        foreach (var invalid in new[] { ':', '\\', '/', '?', '*', '[', ']' })
        {
            value = value.Replace(invalid, '_');
        }

        value = new string(value.Select(character => char.IsControl(character) ? '_' : character).ToArray())
            .Trim('\'');
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "Data";
        }

        return value.Length > 31 ? value[..31] : value;
    }

    private static string CreateWorkbookXml(string worksheetName) =>
        $"""<?xml version="1.0" encoding="utf-8"?><workbook xmlns="{SpreadsheetNamespace}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{SecurityElementEscape(worksheetName)}" sheetId="1" r:id="rId1" /></sheets></workbook>""";

    private static string SecurityElementEscape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }

    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
          <Default Extension="xml" ContentType="application/xml" />
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" />
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml" />
        </Types>
        """;
    private const string PackageRelationshipsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml" />
        </Relationships>
        """;
    private const string WorkbookRelationshipsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml" />
        </Relationships>
        """;
    private const string StylesXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font><sz val="11" /><name val="Calibri" /></font></fonts>
          <fills count="2">
            <fill><patternFill patternType="none" /></fill>
            <fill><patternFill patternType="gray125" /></fill>
          </fills>
          <borders count="1"><border /></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" /></cellStyleXfs>
          <cellXfs count="2">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" />
            <xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" />
          </cellXfs>
        </styleSheet>
        """;
}
