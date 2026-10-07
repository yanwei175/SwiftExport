using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Procurement;
/// <summary>确认批次的行依据，记录明确无 BOM 与实际结构指纹；永不回填当前 BOM。</summary>
[Table("purchase_confirmation_line")]
public sealed partial class PurchaseConfirmationLine : BaseEntity
{
    /// <summary>所属采购确认批次标识。</summary>
    public Guid ConfirmationId { get; set; }
    /// <summary>确认时对应的采购订单标识。</summary>
    public Guid PurchaseOrderId { get; set; }
    /// <summary>确认时对应的采购订单行标识。</summary>
    public Guid PurchaseOrderLineId { get; set; }
    /// <summary>建立本确认依据的稳定命令标识。</summary>
    public Guid CommandId { get; set; }
    /// <summary>采购确认结构的根产品标识。</summary>
    public Guid RootProductId { get; set; }
    /// <summary>确认时采用的采购订单行版本。</summary>
    public int LineVersion { get; set; }
    /// <summary>确认时根产品是否存在有效 BOM 结构。</summary>
    public bool HasBom { get; set; }
    /// <summary>确认结构的确定性指纹，用于证明执行依据未被替换。</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>采购行完成确认的时间。</summary>
    public DateTime ConfirmedAt { get; set; }
    /// <summary>BOM 比例计算、累计和舍入规则快照。</summary>
    public string RatioPolicy { get; set; } = "RawFactors-Final4-AwayFromZero";
}
