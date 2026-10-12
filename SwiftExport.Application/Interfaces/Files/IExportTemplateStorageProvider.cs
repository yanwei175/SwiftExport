using SwiftExport.Application.Interfaces.Files.Models;

namespace SwiftExport.Application.Interfaces.Files;

/// <summary>由宿主提供独立且已验证的模板存储快照，避免模板经通用容器上传入口被改写。</summary>
public interface IExportTemplateStorageProvider
{
    /// <summary>获取已启用模板存储；未启用时返回 false，不触碰磁盘或其他文件容器。</summary>
    bool TryGetEnabledStorage(out ExportTemplateStorageContext? context);
}
