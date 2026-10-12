namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>在线选择受控服务器模板，按精确版本缓存并调用现有 Excel 生成器；不提供离线授权兜底。</summary>
public interface IServerExcelTemplateService
{
    /// <summary>在线查询与指定单据及数据结构版本匹配的模板快照，不返回本地过期目录。</summary>
    Task<IReadOnlyList<ServerExcelTemplateInfo>> ListAsync(
        ExportTemplateContract contract,
        CancellationToken cancellationToken = default);

    /// <summary>在线重新确认选定版本，校验模板内容后原子发布 XLSX；会话失效或发布前取消时保留原目标。</summary>
    Task ExportToFileAsync(
        ServerTemplateExportRequest request,
        string targetPath,
        bool overwrite = false,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
