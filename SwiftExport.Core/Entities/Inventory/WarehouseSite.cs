using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Inventory;

/// <summary>仓库站点实体，当前用于维护供应商虚拟仓及其启用状态。</summary>
[Table("WarehouseSite")]
public sealed class WarehouseSite : BaseEntity
{
    /// <summary>该实体保存的仓库编码。</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>该实体保存的仓库名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>该实体保存的仓库站点类型。</summary>
    public WarehouseSiteTypeEnum SiteType { get; set; }
    /// <summary>关联的供应商主数据唯一标识。</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>单据或业务事实形成时冻结的供应商编码历史快照，不随主数据更新。</summary>
    public string? SupplierCodeSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的供应商名称历史快照，不随主数据更新。</summary>
    public string? SupplierNameSnapshot { get; set; }
    /// <summary>业务关系启用标记，用于控制该记录的业务适用范围。</summary>
    public bool IsActive { get; set; }
}
