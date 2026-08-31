using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Procurement;
/// <summary>确认批次的行依据，记录明确无 BOM 与实际结构指纹；永不回填当前 BOM。</summary>
[Table("PurchaseConfirmationLine")]
public sealed class PurchaseConfirmationLine : BaseEntity
{
    public Guid ConfirmationId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid CommandId { get; set; }
    public Guid RootProductId { get; set; }
    public int LineVersion { get; set; }
    public bool HasBom { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTime ConfirmedAt { get; set; }
    public string RatioPolicy { get; set; } = "RawFactors-Final4-AwayFromZero";
}
