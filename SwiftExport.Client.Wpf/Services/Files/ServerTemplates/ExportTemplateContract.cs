namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>声明调用方实际提供的单据类型及模板数据结构版本；稳定键不使用 CLR 类型名。</summary>
public sealed record ExportTemplateContract(string DocumentType, string DataContractKey, int DataContractVersion);
