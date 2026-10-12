using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DvFileStorage.Abstractions;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Files.ExportTemplates;

/// <summary>每次请求完整验证服务器清单及启用版本文件，只公开匹配的元数据和同一校验后的下载快照。</summary>
public sealed class ExportTemplateCatalogService(
    IExportTemplateStorageProvider storageProvider,
    IFileAccessPolicy accessPolicy,
    ILogger<ExportTemplateCatalogService> logger) : IExportTemplateCatalogService
{
    private const string ManifestPath = "模板目录.json";
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxTemplateBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    /// <inheritdoc />
    public async Task<ServiceResult<List<ExportTemplateInfoDto>>> ListAsync(
        string documentType,
        string dataContractKey,
        int dataContractVersion,
        CancellationToken cancellationToken = default)
    {
        var access = await accessPolicy.AuthorizeReadAsync(cancellationToken).ConfigureAwait(false);
        if (!access.Success) return CopyFailure<List<ExportTemplateInfoDto>>(access);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStableText(documentType, 64) || !IsStableText(dataContractKey, 128) || dataContractVersion <= 0)
            return ServiceResult<List<ExportTemplateInfoDto>>.Fail(ServiceErrorCodes.Validation, "单据类型、数据契约及正版本号不能为空。");
        if (!storageProvider.TryGetEnabledStorage(out var context) || context is null)
            return Unavailable<List<ExportTemplateInfoDto>>();

        try
        {
            using var snapshot = await ReadSnapshotAsync(context.Storage, null, 0, cancellationToken).ConfigureAwait(false);
            var items = snapshot.Items.Where(item => item.Enabled &&
                    item.DocumentType == documentType && item.DataContractKey == dataContractKey &&
                    item.DataContractVersion == dataContractVersion)
                .Select(ToInfo).ToList();
            return ServiceResult<List<ExportTemplateInfoDto>>.Ok(items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "服务器模板清单或版本文件校验失败。");
            return InvalidCatalog<List<ExportTemplateInfoDto>>();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FileReadHandle>> OpenReadAsync(
        string templateId,
        int templateVersion,
        CancellationToken cancellationToken = default)
    {
        var access = await accessPolicy.AuthorizeReadAsync(cancellationToken).ConfigureAwait(false);
        if (!access.Success) return CopyFailure<FileReadHandle>(access);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidTemplateId(templateId) || templateVersion <= 0)
            return ServiceResult<FileReadHandle>.Fail(ServiceErrorCodes.Validation, "模板标识或版本号无效。");
        if (!storageProvider.TryGetEnabledStorage(out var context) || context is null)
            return Unavailable<FileReadHandle>();

        try
        {
            using var snapshot = await ReadSnapshotAsync(context.Storage, templateId, templateVersion, cancellationToken).ConfigureAwait(false);
            var item = snapshot.Items.SingleOrDefault(candidate => candidate.TemplateId == templateId);
            if (item is null || !item.Enabled)
                return ServiceResult<FileReadHandle>.Fail(ServiceErrorCodes.NotFound, "模板不存在或已停用。");
            if (item.TemplateVersion != templateVersion)
                return ServiceResult<FileReadHandle>.Fail(ServiceErrorCodes.VersionConflict, "模板当前版本已变化，请刷新列表后重新选择。");

            return ServiceResult<FileReadHandle>.Ok(new FileReadHandle
            {
                Content = snapshot.DetachContent(),
                FileName = $"{templateId}-v{templateVersion}.xlsx"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "服务器模板清单或版本文件校验失败。");
            return InvalidCatalog<FileReadHandle>();
        }
    }

    /// <summary>以一个清单快照解释所有启用引用；任意坏引用都会失败，不按查询条件跳过全局完整性检查。</summary>
    private static async Task<CatalogSnapshot> ReadSnapshotAsync(
        IFileStorage storage, string? templateId, int templateVersion, CancellationToken token)
    {
        using var manifestBuffer = await ReadBoundedAsync(storage, ManifestPath, MaxManifestBytes, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(manifestBuffer, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateProperties(document.RootElement);
        var manifest = document.RootElement.Deserialize<ExportTemplateManifest>(ManifestJsonOptions)
            ?? throw new InvalidDataException("模板清单不能为空。");
        ValidateManifest(manifest);
        var snapshot = new CatalogSnapshot(manifest.Templates);
        try
        {
            foreach (var item in manifest.Templates.Where(item => item.Enabled))
            {
                token.ThrowIfCancellationRequested();
                var path = $"{item.TemplateId}/v{item.TemplateVersion}/模板.xlsx";
                var content = await ReadBoundedAsync(storage, path, MaxTemplateBytes, token).ConfigureAwait(false);
                try
                {
                    if (content.Length != item.FileLength)
                        throw new InvalidDataException($"模板文件长度与清单不符：{item.TemplateId}");
                    var hash = Convert.ToHexString(SHA256.HashData(content.GetBuffer().AsSpan(0, checked((int)content.Length))));
                    if (!hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"模板文件摘要与清单不符：{item.TemplateId}");
                    ExportTemplateXlsxValidator.Validate(content, token);
                    if (item.TemplateId == templateId && item.TemplateVersion == templateVersion)
                    {
                        snapshot.Content = content;
                        content = null!;
                    }
                }
                finally { content?.Dispose(); }
            }
            token.ThrowIfCancellationRequested();
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    /// <summary>流式计数后形成有限内存快照，不信任文件长度或可定位能力，始终释放存储输入流。</summary>
    private static async Task<MemoryStream> ReadBoundedAsync(IFileStorage storage, string path, int limit, CancellationToken token)
    {
        await using var source = await storage.OpenReadAsync(path, token).ConfigureAwait(false);
        var target = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read == 0) break;
                if (target.Length + read > limit) throw new InvalidDataException("模板资源超过允许的输入大小。");
                target.Write(buffer, 0, read);
            }
            token.ThrowIfCancellationRequested();
            target.Position = 0;
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    /// <summary>全部条目必须具备完整稳定元数据，ID 唯一；停用文件可以缺失，但停用元数据仍须合法。</summary>
    private static void ValidateManifest(ExportTemplateManifest manifest)
    {
        if (manifest.FormatVersion != 1 || manifest.Templates is null)
            throw new InvalidDataException("模板清单格式版本或列表无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.Templates)
        {
            if (item is null || !IsValidTemplateId(item.TemplateId) || !ids.Add(item.TemplateId) ||
                !IsStableText(item.DisplayName, 200) || !IsStableText(item.DocumentType, 64) ||
                !IsStableText(item.DataContractKey, 128) || item.DataContractVersion <= 0 || item.TemplateVersion <= 0 ||
                item.FileLength <= 0 || item.FileLength > MaxTemplateBytes || item.Sha256 is null ||
                item.Sha256.Length != 64 || !item.Sha256.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("模板清单包含非法、缺失或重复元数据。");
        }
    }

    /// <summary>拒绝路径分隔、首尾或连续短横线；标识只用于服务生成固定目录路径。</summary>
    private static bool IsValidTemplateId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id[0] == '-' || id[^1] == '-' || id.Contains("--", StringComparison.Ordinal))
            return false;
        return id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    /// <summary>发布字段与精确匹配条件不能含首尾空白，避免发布工具、服务和客户端产生不同契约键。</summary>
    private static bool IsStableText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && value == value.Trim();

    /// <summary>JSON 默认会接受重复属性，受控清单须拒绝歧义，避免发布者与客户端解释不同。</summary>
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("模板清单包含重复 JSON 属性。");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static ExportTemplateInfoDto ToInfo(ExportTemplateManifestItem item) => new()
    {
        TemplateId = item.TemplateId, DisplayName = item.DisplayName, DocumentType = item.DocumentType,
        DataContractKey = item.DataContractKey, DataContractVersion = item.DataContractVersion,
        TemplateVersion = item.TemplateVersion, FileLength = item.FileLength, Sha256 = item.Sha256
    };

    private static ServiceResult<T> CopyFailure<T>(ServiceResult<bool> result) =>
        ServiceResult<T>.Fail(result.Error?.Code ?? ServiceErrorCodes.Unexpected,
            result.Error?.Message ?? result.Message, result.Error?.Field);

    private static ServiceResult<T> Unavailable<T>() =>
        ServiceResult<T>.Fail(ServiceErrorCodes.ServiceUnavailable, "模板分发服务尚未启用。");

    private static ServiceResult<T> InvalidCatalog<T>() =>
        ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, "服务器模板清单或文件无效，请联系管理员。");

    /// <summary>拥有本次下载缓冲区；只有成功交给响应时才转移释放责任。</summary>
    private sealed class CatalogSnapshot(List<ExportTemplateManifestItem> items) : IDisposable
    {
        internal List<ExportTemplateManifestItem> Items { get; } = items;
        internal MemoryStream? Content { get; set; }
        internal Stream DetachContent()
        {
            var content = Content ?? throw new InvalidDataException("已验证模板的内容快照缺失。");
            Content = null;
            return content;
        }
        /// <summary>释放尚未交给响应的内部下载缓冲。</summary>
        public void Dispose() => Content?.Dispose();
    }
}
