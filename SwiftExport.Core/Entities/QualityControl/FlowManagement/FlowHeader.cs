using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Entities.QualityControl.FlowManagement;

/// <summary>独立保存某物料在一套配件配置下的一次流量测试；物料身份与 SKU 快照创建后固定。</summary>
[Table("flow_header")]
[DbIndex("IX_FlowHeader_Material_Date", nameof(MaterialId), nameof(TestDate), nameof(Uid), Filter = "\"is_deleted\" = FALSE")]
[DbIndex("IX_FlowHeader_Date", nameof(TestDate), nameof(Uid), Filter = "\"is_deleted\" = FALSE")]
[LogicalReference(nameof(MaterialId), typeof(Product), IsRequired = true)]
[DbImmutableColumns(nameof(MaterialId), nameof(SkuSnapshot))]
public sealed class FlowHeader : BaseEntity
{
    /// <summary>测试物料身份，仅逻辑关联，不建立数据库外键。</summary>
    public Guid MaterialId { get; set; }
    /// <summary>首次测试建档时的物料 SKU，后续物料改名不回写。</summary>
    public string SkuSnapshot { get; set; } = string.Empty;
    /// <summary>实际测试日期，不包含时间或时区。</summary>
    [DbColumn(StoreType = "date")]
    public DateTime TestDate { get; set; }
    /// <summary>本次测试的市场和方法背景说明。</summary>
    public string? MarketDescription { get; set; }
    /// <summary>各功能共用的配件配置说明；各自限流器规格保存在行上。</summary>
    public string? ConfigurationDescription { get; set; }
    /// <summary>测试组补充说明。</summary>
    public string? Remark { get; set; }
}

