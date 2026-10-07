
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.SalesOrders;

/// <summary>销售订单明细实体，保存客户订购产品、数量、价格及交易成立时的产品快照。</summary>
[Table("sales_order_line")]
public partial class SalesOrderLine : BaseEntity
{
    /// <summary>该实体保存的单据行号。</summary>
    public int LineNumber { get; set; }
    /// <summary>关联的销售订单头唯一标识。</summary>
    public required Guid SalesOrderId { get; set; }
    /// <summary>该实体保存的销售订单号。</summary>
    public string? OrderNo { get; set; }
    /// <summary>非标准产品标记，用于控制该记录的业务适用范围。</summary>
    public bool IsNonStandard { get; set; } = false;
    /// <summary>关联的产品主数据唯一标识。</summary>
    public Guid? ProductId { get; set; }
    /// <summary>关联的客户产品关系唯一标识。</summary>
    public Guid? CustomerProductId { get; set; }

    /// <summary>该实体保存的公司产品编码。</summary>
    public required string Sku { get; set; }
    /// <summary>该实体保存的关联产品编码。</summary>
    public string? LinkSku { get; set; }

    /// <summary>该实体保存的客户产品编码。</summary>
    public string? CustomerSku { get; set; }
    /// <summary>该实体保存的英文产品描述。</summary>
    public string? DescriptionEn { get; set; }
    /// <summary>该实体保存的颜色英文名称。</summary>
    public string? ColorNameEn { get; set; }
    /// <summary>该实体保存的英文规格特性。</summary>
    public string? FeaturesEn { get; set; }

    /// <summary>该实体保存的人民币单价或参考价。</summary>
    public decimal? CnyPrice { get; set; }
    /// <summary>该实体保存的美元参考价。</summary>
    public decimal? UsdPrice { get; set; }
    /// <summary>客户在本销售订单明细中订购的数量。</summary>
    public decimal Qty { get; set; }
    /// <summary>销售行订购数量使用的计量单位代码快照；保存时必须明确单位。</summary>
    public string UnitSnapshot { get; set; } = string.Empty;

    /// <summary>该实体保存的人民币金额。</summary>
    public decimal? CnyAmount { get; set; }
    /// <summary>该实体保存的美元金额。</summary>
    public decimal? UsdAmount { get; set; }

    /// <summary>该实体保存的每箱装量。</summary>
    public decimal? PcsOfCarton { get; set; }
    /// <summary>该实体保存的每箱净重。</summary>
    public decimal? NwOfCarton { get; set; }
    /// <summary>该实体保存的每箱毛重。</summary>
    public decimal? GwOfCarton { get; set; }
    /// <summary>该实体保存的每箱体积。</summary>
    public decimal? CbmOfCarton { get; set; }

    /// <summary>该实体保存的箱数。</summary>
    public decimal? Ctns { get; set; }
    /// <summary>该实体保存的明细总净重。</summary>
    public decimal? NwSubTotal { get; set; }
    /// <summary>该实体保存的明细总毛重。</summary>
    public decimal? GwSubTotal { get; set; }
    /// <summary>该实体保存的明细总体积。</summary>
    public decimal? CbmSubTotal { get; set; }

    /// <summary>该实体保存的销售明细备注。</summary>
    public string? SubNote { get; set; }

    /// <summary>
    /// 预计交货日期
    /// </summary>
    public DateTime? ExpectedDeliveryDate { get; set; }
    /// <summary>该实体保存的产品主图路径。</summary>
    public string? MainImage { get; set; }

}

