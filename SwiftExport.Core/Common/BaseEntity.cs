
namespace SwiftExport.Core.Common;

public abstract class BaseEntity
{
    public int Id { get; set; }

    // 业务层字段，不入库（Dapper 忽略即可）
    public string? Even { get; set; }
    public string? Status { get; set; }

    // 软删除
    public bool IsDeleted { get; set; } = false;

    // 审计字段
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
}


