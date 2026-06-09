
using SwiftExport.Contracts.Common;

namespace SwiftExport.Core.Common;

public abstract class BaseEntity
{
    /// <summary>
    /// 全链路唯一主键（Guid）。
    /// 说明：已统一以 Uid 作为主键标识，不再使用整型 Id。
    /// </summary>
    public Guid Uid { get; set; } = GuidSequentialGenerator.NewGuid();

    /// <summary>
    /// 并发版本号(乐观并发控制).
    /// 约定：
    /// 1. 新增时 Version = 1
    /// 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1
    /// 3. 命中失败视为并发冲突（数据已被他人修改）
    /// </summary>
    public int Version { get; set; } = 1;
    // 软删除
    public bool IsDeleted { get; set; } = false;

    // 审计字段
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }



    // 业务层字段，不入库(Dapper忽略即可)
    public string? Even { get; set; }
    public string? Status { get; set; }
}


