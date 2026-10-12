using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace SwiftExport.Application.Services.Files.ExportTemplates;

/// <summary>校验模板为有界的无宏 XLSX 包；不加载 Excel、执行公式或解析 Report 业务表达式。</summary>
internal static class ExportTemplateXlsxValidator
{
    private const long MaxExpandedBytes = 100L * 1024 * 1024;
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

    /// <summary>限制 ZIP 声明解压规模并核对工作簿内容类型，拒绝重复部件、危险名称和宏包。</summary>
    internal static void Validate(MemoryStream content, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = new HashSet<string>(StringComparer.Ordinal);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!names.Add(entry.FullName) || entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') ||
                    entry.FullName.Split('/').Any(segment => segment is "." or ".."))
                    throw new InvalidDataException("模板 XLSX 包包含非法或重复部件。");
                expanded = checked(expanded + entry.Length);
                if (expanded > MaxExpandedBytes)
                    throw new InvalidDataException("模板 XLSX 包的声明解压大小超过限制。");
                if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("模板不能包含宏部件。");
            }

            var typesEntry = archive.GetEntry("[Content_Types].xml")
                ?? throw new InvalidDataException("模板缺少 XLSX 内容类型。");
            if (archive.GetEntry("xl/workbook.xml") is null)
                throw new InvalidDataException("模板缺少 XLSX 工作簿部件。");
            using var typeStream = typesEntry.Open();
            using var reader = XmlReader.Create(typeStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            });
            var document = XDocument.Load(reader);
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
            if (document.Root?.Name != ns + "Types" || !document.Root.Elements(ns + "Override").Any(element =>
                    (string?)element.Attribute("PartName") == "/xl/workbook.xml" &&
                    (string?)element.Attribute("ContentType") == WorkbookContentType))
                throw new InvalidDataException("模板必须是 XLSX 工作簿。");
            if (document.Root.Elements().Any(element =>
                    ((string?)element.Attribute("ContentType"))?.Contains("macro", StringComparison.OrdinalIgnoreCase) == true ||
                    ((string?)element.Attribute("ContentType"))?.Contains("vba", StringComparison.OrdinalIgnoreCase) == true))
                throw new InvalidDataException("模板不能包含宏内容类型。");
        }
        finally
        {
            content.Position = 0;
        }
    }
}
