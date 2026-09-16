using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Procurement;
/// <summary>独立采购命令原始结果，和业务操作日志分开存储，以命令身份和请求指纹检测重放冲突。</summary>
[Table("ProcurementCommandReceipt")]
public sealed partial class ProcurementCommandReceipt : BaseEntity
{
    public Guid CommandId { get; set; }
    public Guid ActorUid { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public string Operation { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
