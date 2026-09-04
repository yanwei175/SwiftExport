using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>中性产品实体，保存主型号下按颜色区分的业务编码、价格和图片路径。</summary>
[Table("ProductNeutral")]
public class ProductNeutral : BaseEntity
{
    /// <summary>中性产品业务编码；未删除记录内唯一。</summary>
    public string ProductCode { get; set; } = default!;
    /// <summary>所属主型号标识；使用逻辑外键，由聚合保存上下文赋值。</summary>
    public Guid ProductMasterId { get; set; }
    /// <summary>颜色标识；使用逻辑外键，由应用服务校验。</summary>
    public Guid ColorId { get; set; }
    /// <summary>默认美元销售价。</summary>
    public decimal? DefaultSalesPriceUsd { get; set; }
    /// <summary>默认汇率；非空时必须大于零。</summary>
    public decimal? DefaultExchangeRate { get; set; }
    /// <summary>利润率；本期仅保存用户输入，不参与价格计算。</summary>
    public decimal? ProfitMargin { get; set; }
    /// <summary>默认人民币采购价。</summary>
    public decimal? DefaultPurchasePriceCny { get; set; }
    /// <summary>图片路径；本期不承担文件上传或生命周期管理。</summary>
    public string? ImagePath { get; set; }
    /// <summary>是否作为所属主型号的主图；列表查询只读取有效记录中的主图路径。</summary>
    public bool IsMainImage { get; set; }
}
