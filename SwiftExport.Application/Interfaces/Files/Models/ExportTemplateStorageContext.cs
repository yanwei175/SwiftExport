using DvFileStorage.Abstractions;

namespace SwiftExport.Application.Interfaces.Files.Models;

/// <summary>保存宿主验证完成的专用模板存储；物理根目录不进入业务结果或客户端契约。</summary>
public sealed class ExportTemplateStorageContext
{
    /// <summary>获取用于读取固定清单和不可变版本文件的独立存储实例。</summary>
    public required IFileStorage Storage { get; init; }
}
