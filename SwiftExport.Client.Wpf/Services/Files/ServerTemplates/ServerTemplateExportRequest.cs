using SwiftExportClient.Services.Files.Templates;

namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>固定一次导出的模板 ID、文件版本及实际数据契约；数据须由 Feature 提前形成稳定快照。</summary>
public sealed record ServerTemplateExportRequest(
    string TemplateId,
    int TemplateVersion,
    ExportTemplateContract Contract,
    ExcelTemplateData Data);
