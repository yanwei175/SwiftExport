using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Procurement;
/// <summary>独立采购命令原始结果，和业务操作日志分开存储，以命令身份和请求指纹检测重放冲突。</summary>
[Table("ProcurementCommandReceipt")]
public sealed partial class ProcurementCommandReceipt : BaseEntity
{
    /// <summary>客户端提供的稳定命令标识，用于识别重试。</summary>
    public Guid CommandId { get; set; }
    /// <summary>执行该命令的用户标识。</summary>
    public Guid ActorUid { get; set; }
    /// <summary>命令作用的采购订单标识。</summary>
    public Guid PurchaseOrderId { get; set; }
    /// <summary>采购命令操作类型。</summary>
    public string Operation { get; set; } = "";
    /// <summary>规范化请求内容的指纹，用于识别同命令不同内容的冲突。</summary>
    public string RequestFingerprint { get; set; } = "";
    /// <summary>首次命令执行结果的 JSON 快照，用于幂等重放。</summary>
    public string ResultJson { get; set; } = "";
}
