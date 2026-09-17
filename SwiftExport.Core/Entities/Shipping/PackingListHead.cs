using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Shipping;

/// <summary>装箱单头，保存客户文档快照及独立的过账事实，出货地点只属于单据文本。</summary>
[Table("PackingListHead")]
public sealed partial class PackingListHead : BaseEntity
{
    /// <summary>装箱单业务编号。</summary>
    public string PackingListNo { get; set; } = string.Empty;
    /// <summary>最终客户逻辑标识。</summary>
    public Guid CustomerId { get; set; }
    /// <summary>单据采用的客户名称快照，不随主数据更新。</summary>
    public string? CustomerNameSnapshot { get; set; }
    /// <summary>单据采用的客户地址快照。</summary>
    public string? CustomerAddressSnapshot { get; set; }
    /// <summary>自由填写的备注快照，不关联联系人实体。</summary>
    public string? AttentionSnapshot { get; set; }
    /// <summary>用户自定义单据日期，不作为库存记账日期。</summary>
    public DateTime InvoiceDate { get; set; }
    /// <summary>集装箱号。</summary>
    public string? ContainerNo { get; set; }
    /// <summary>整单出货地点文本快照，不决定库存仓库。</summary>
    public string? ShippingLocationSnapshot { get; set; }

    /// <summary>封条号。</summary>
    public string? SealNo { get; set; }
    /// <summary>装运箱唛。</summary>
    public string? ShippingMark { get; set; }
    /// <summary>单据备注。</summary>
    public string? Note { get; set; }
    /// <summary>预计离港日期。</summary>
    public DateTime? Etd { get; set; }
    /// <summary>预计到达日期。</summary>
    public DateTime? Eta { get; set; }
    /// <summary>单据生命周期，与库存状态分别表达。</summary>
    public PackingListLifecycleStatusEnum LifecycleStatus { get; set; }
    /// <summary>库存过账状态，不表示已实现过账入口。</summary>
    public PackingListPostingStatusEnum PostingStatus { get; set; }
    /// <summary>库存业务日期，未过账可空，与单据日期独立。</summary>
    public DateTime? PostingDate { get; set; }
    /// <summary>实际过账操作时间。</summary>
    public DateTime? PostedDate { get; set; }
    /// <summary>过账操作人。</summary>
    public string? PostedBy { get; set; }
    /// <summary>反向操作时间。</summary>
    public DateTime? ReversedDate { get; set; }
    /// <summary>反向操作人。</summary>
    public string? ReversedBy { get; set; }
    /// <summary>反向原因。</summary>
    public string? ReverseReason { get; set; }
    /// <summary>保留建单命令身份，具体命令校验另行实现。</summary>
    public Guid CreateCommandId { get; set; }
    /// <summary>过账命令身份，用于后续幂等处理。</summary>
    public Guid? PostCommandId { get; set; }
    /// <summary>反向命令身份，用于后续幂等处理。</summary>
    public Guid? ReverseCommandId { get; set; }
}
