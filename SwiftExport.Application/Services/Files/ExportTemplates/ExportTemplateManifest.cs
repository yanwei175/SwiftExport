namespace SwiftExport.Application.Services.Files.ExportTemplates;

/// <summary>描述固定版本的服务器受控清单，不作为客户端 DTO 或文件系统路径输入。</summary>
internal sealed class ExportTemplateManifest
{
    public required int FormatVersion { get; init; }
    public required List<ExportTemplateManifestItem> Templates { get; init; }
}

/// <summary>保存一个模板的当前发布版本；停用项保留元数据，但不会被读取或列举。</summary>
internal sealed class ExportTemplateManifestItem
{
    public required string TemplateId { get; init; }
    public required string DisplayName { get; init; }
    public required string DocumentType { get; init; }
    public required string DataContractKey { get; init; }
    public required int DataContractVersion { get; init; }
    public required int TemplateVersion { get; init; }
    public required long FileLength { get; init; }
    public required string Sha256 { get; init; }
    public required bool Enabled { get; init; }
}
