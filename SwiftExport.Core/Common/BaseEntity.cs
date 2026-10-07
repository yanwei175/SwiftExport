
using SwiftExport.Contracts.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Common;

/// <summary>持久化实体公共基类，统一提供主键、乐观并发、软删除和审计字段。</summary>
[DbCheck("CK_{Table}_Version", "\"version\" >= 1")]
public abstract class BaseEntity
{
    /// <summary>
    /// 全链路唯一主键（Guid）。
    /// 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。
    /// </summary>
    public Guid Uid { get; set; } = GuidSequentialGenerator.NewGuid();

    /// <summary>
    /// 并发版本号(乐观并发控制).
    /// 约定：
    /// 1. 新增时 Version = 1
    /// 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1
    /// 3. 命中失败视为并发冲突（数据已被他人修改）
    /// </summary>
    [DbColumn(DefaultExpression = "1")]
    public int Version { get; set; } = 1;
    /// <summary>软删除标记；为真时业务查询默认不再返回该记录。</summary>
    [DbColumn(DefaultExpression = "FALSE")]
    public bool IsDeleted { get; set; } = false;

    /// <summary>记录创建时间。</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>记录创建人标识。</summary>
    public string? CreatedBy { get; set; }
    /// <summary>记录最后更新时间。</summary>
    public DateTime? UpdatedDate { get; set; }
    /// <summary>记录最后更新人标识。</summary>
    public string? UpdatedBy { get; set; }

}


