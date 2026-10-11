namespace SwiftExportClient.Services.Files;

/// <summary>报告实际操作阶段和可计数的行进度；同步加载、生成或保存阶段不虚构百分比。</summary>
public sealed record FileOperationProgress(string Stage, int? ProcessedRows = null, int? TotalRows = null);
