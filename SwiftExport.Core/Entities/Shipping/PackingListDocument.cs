using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Shipping;

/// <summary>装箱单头实体，保存客户最终出货编排及提交、反向状态。</summary>
[Table("PackingListDocument")]
public sealed class PackingListDocument : BaseEntity
{
    /// <summary>该实体保存的装箱单号。</summary>
    public string PackingListNo { get; set; } = string.Empty;
    /// <summary>关联的客户主数据唯一标识。</summary>
    public Guid CustomerId { get; set; }
    /// <summary>单据或业务事实形成时冻结的客户编码历史快照，不随主数据更新。</summary>
    public string? CustomerCodeSnapshot { get; set; }
    /// <summary>单据或业务事实形成时冻结的客户名称历史快照，不随主数据更新。</summary>
    public string? CustomerNameSnapshot { get; set; }
    /// <summary>该实体保存的装箱日期。</summary>
    public DateTime PackingDate { get; set; }
    /// <summary>该实体保存的实际出货日期。</summary>
    public DateTime ShipmentDate { get; set; }
    /// <summary>该实体保存的预计离港日期。</summary>
    public DateTime? Etd { get; set; }
    /// <summary>该实体保存的预计到达日期。</summary>
    public DateTime? Eta { get; set; }
    /// <summary>该实体保存的商业发票号。</summary>
    public string? InvoiceNo { get; set; }
    /// <summary>该实体保存的集装箱号。</summary>
    public string? ContainerNo { get; set; }
    /// <summary>该实体保存的集装箱封条号。</summary>
    public string? SealNo { get; set; }
    /// <summary>该实体保存的装运唛头。</summary>
    public string? ShippingMark { get; set; }
    /// <summary>单据或业务事实形成时冻结的交付地址历史快照，不随主数据更新。</summary>
    public string? DeliveryAddressSnapshot { get; set; }
    /// <summary>单据生命周期状态，用于判断当前记录允许执行的业务操作。</summary>
    public PackingListLifecycleStatusEnum LifecycleStatus { get; set; }
    /// <summary>库存过账状态，用于判断当前记录允许执行的业务操作。</summary>
    public PackingListPostingStatusEnum PostingStatus { get; set; }
    /// <summary>该实体保存的过账时间。</summary>
    public DateTime? PostedDate { get; set; }
    /// <summary>该实体保存的过账操作人。</summary>
    public string? PostedBy { get; set; }
    /// <summary>该实体保存的反向时间。</summary>
    public DateTime? ReversedDate { get; set; }
    /// <summary>该实体保存的反向操作人。</summary>
    public string? ReversedBy { get; set; }
    /// <summary>该实体保存的反向原因。</summary>
    public string? ReverseReason { get; set; }
    /// <summary>建单命令唯一标识，用于保障对应业务命令幂等执行。</summary>
    public Guid CreateCommandId { get; set; }
    /// <summary>该实体保存的建单请求指纹。</summary>
    public string? CreateRequestFingerprint { get; set; }
    /// <summary>过账命令唯一标识，用于保障对应业务命令幂等执行。</summary>
    public Guid? PostCommandId { get; set; }
    /// <summary>该实体保存的过账预览令牌。</summary>
    public string? PostPreviewToken { get; set; }
    /// <summary>反向命令唯一标识，用于保障对应业务命令幂等执行。</summary>
    public Guid? ReverseCommandId { get; set; }
    /// <summary>该实体保存的反向预览令牌。</summary>
    public string? ReversePreviewToken { get; set; }
}
