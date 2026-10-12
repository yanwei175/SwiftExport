namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>保存服务器目录的不可变模板版本快照；文件版本与数据结构版本分别表达。</summary>
public sealed record ServerExcelTemplateInfo(
    string TemplateId,
    string DisplayName,
    string DocumentType,
    string DataContractKey,
    int DataContractVersion,
    int TemplateVersion,
    long FileLength,
    string Sha256);
