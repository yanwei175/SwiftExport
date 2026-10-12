using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DvFileStorage.Abstractions;
using DvFileStorage.Configuration;
using DvFileStorage.Local;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Application.Services.Files.ExportTemplates;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Files.ExportTemplates;

/// <summary>创建当前测试独占的随机目录及合法 XLSX 包，不访问真实服务器或数据库。</summary>
internal sealed class ExportTemplateTestScope : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "SwiftExport-template-catalog-tests-" + Guid.NewGuid().ToString("N"));
    internal LocalFileStorage Storage { get; }
    internal byte[] Xlsx { get; } = CreateXlsx();
    internal AccessPolicy Policy { get; } = new();
    internal StorageProvider Provider { get; }
    internal ExportTemplateCatalogService Service { get; }

    internal ExportTemplateTestScope()
    {
        Directory.CreateDirectory(Root);
        Storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = Root });
        Provider = new StorageProvider { Context = new ExportTemplateStorageContext { Storage = Storage } };
        Service = new(Provider, Policy, NullLogger<ExportTemplateCatalogService>.Instance);
    }

    internal JsonObject Item(string id = "sales-standard", string documentType = "SO", string contractKey = "SalesOrder.Export", int contractVersion = 1, int version = 1, bool enabled = true) => new()
    {
        ["templateId"] = id, ["displayName"] = "标准销售单", ["documentType"] = documentType,
        ["dataContractKey"] = contractKey, ["dataContractVersion"] = contractVersion,
        ["templateVersion"] = version, ["fileLength"] = Xlsx.Length,
        ["sha256"] = Convert.ToHexString(SHA256.HashData(Xlsx)), ["enabled"] = enabled
    };

    internal async Task PublishAsync(params JsonObject[] items)
    {
        foreach (var item in items)
            if (item["enabled"]!.GetValue<bool>())
                await WriteAsync($"{item["templateId"]!.GetValue<string>()}/v{item["templateVersion"]!.GetValue<int>()}/模板.xlsx", Xlsx);
        await WriteManifestAsync(items);
    }

    internal Task WriteManifestAsync(params JsonObject[] items)
    {
        var root = new JsonObject { ["formatVersion"] = 1, ["templates"] = new JsonArray(items.Select(item => (JsonNode)item.DeepClone()).ToArray()) };
        return WriteAsync("模板目录.json", Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    internal async Task WriteAsync(string path, byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        await Storage.WriteAsync(path, source, overwrite: true);
    }

    /// <summary>夹具包含 OOXML 关系、工作簿和插值单元格，避免用任意 ZIP 冒充 Excel。</summary>
    private static byte[] CreateXlsx()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            WriteEntry(zip, "_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            WriteEntry(zip, "xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="模板" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            WriteEntry(zip, "xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>{{Title}}</t></is></c></row></sheetData></worksheet>""");
        }
        return buffer.ToArray();
    }

    internal static void WriteEntry(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>只清理核对过绝对路径的本次随机测试目录，不清理其他临时文件。</summary>
    public void Dispose()
    {
        var absolute = Path.GetFullPath(Root);
        var temporary = Path.GetFullPath(Path.GetTempPath());
        if (!absolute.StartsWith(temporary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Path.GetFileName(absolute).StartsWith("SwiftExport-template-catalog-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试清理目标越界。");
        Directory.Delete(absolute, recursive: true);
    }

    /// <summary>记录实时授权调用，拒绝时服务不得进入存储。</summary>
    internal sealed class AccessPolicy : IFileAccessPolicy
    {
        internal string? DeniedCode { get; set; }
        internal int Reads { get; private set; }
        /// <summary>返回当前测试授权状态，不访问真实用户数据。</summary>
        public Task<ServiceResult<bool>> AuthorizeReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(DeniedCode is null ? ServiceResult<bool>.Ok(true) : ServiceResult<bool>.Fail(DeniedCode, "测试拒绝"));
        }
        /// <summary>模板没有上传用例，写入授权始终拒绝。</summary>
        public Task<ServiceResult<bool>> AuthorizeWriteAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<bool>.Fail(ServiceErrorCodes.Forbidden, "模板没有上传入口"));
    }

    /// <summary>模拟模板开关并记录访问，检查授权拒绝路径。</summary>
    internal sealed class StorageProvider : IExportTemplateStorageProvider
    {
        internal ExportTemplateStorageContext? Context { get; set; }
        internal int Calls { get; private set; }
        /// <summary>只返回当前测试存储，不创建目录或文件。</summary>
        public bool TryGetEnabledStorage(out ExportTemplateStorageContext? context)
        {
            Calls++;
            context = Context;
            return context is not null;
        }
    }
}
