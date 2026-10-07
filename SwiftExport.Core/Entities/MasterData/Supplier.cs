using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>供应商主数据实体，保存供应商身份、联系人及默认采购条件。</summary>
[Table("supplier")]
public partial class Supplier : BaseEntity
{
    /// <summary>是否允许新业务引用；停用不连带停用仓库或取消历史订单。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>该实体保存的供应商业务编码。</summary>
    public required string SupplierCode { get; set; }
    /// <summary>该实体保存的供应商名称。</summary>
    public required string SupplierName { get; set; }
    /// <summary>该实体保存的供应商折扣率。</summary>
    public decimal? ZheKou { get; set; }
    /// <summary>该实体保存的供应商折扣说明。</summary>
    public string? ZheKouBeiZhu { get; set; }
    /// <summary>该实体保存的资料关联编号。</summary>
    public required string DocRelateNo { get; set; }
    /// <summary>该实体保存的联系人。</summary>
    public string? ContactPerson { get; set; }
    /// <summary>该实体保存的联系人邮箱。</summary>
    public string? ContactEmail { get; set; }
    /// <summary>该实体保存的联系人电话。</summary>
    public string? ContactPhone { get; set; }
    /// <summary>该实体保存的联系地址。</summary>
    public string? Address { get; set; }
    /// <summary>该实体保存的交易币种。</summary>
    public string? Currency { get; set; } = "CNY";
    /// <summary>该实体保存的默认税率。</summary>
    public decimal? TaxRate { get; set; }
    /// <summary>该实体保存的付款条件。</summary>
    public string? PaymentTerms { get; set; }
    /// <summary>该实体保存的默认采购交期天数。</summary>
    public int? DefaultLeadTime { get; set; }
}
