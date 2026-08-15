using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;
namespace SwiftExport.Core.Entities.Relations;

/// <summary>客户产品关系实体，保存客户专属产品编码、描述、价格和交付约定。</summary>
[Table("CustomerProduct")]
public class CustomerProduct : BaseEntity
{

    // ============================================================
    // 1. 核心关联（外键）
    // ============================================================
    /// <summary>关联客户主表 ID。</summary>
    public Guid CustomerId { get; set; }  // 关联客户主表 ID
    /// <summary>该实体保存的客户业务编码。</summary>
    public required string CustomerCode { get; set; }  // 关联客户主表编码   
    /// <summary>关联咱们的产品主表 ID。</summary>
    public Guid? SkuId { get; set; }       // 关联咱们的产品主表 ID
    /// <summary>该实体保存的公司产品编码。</summary>
    public string? Sku { get; set; }
    /// <summary>该实体保存的关联产品编码。</summary>
    public string? LinkSku { get; set; }
    // ============================================================
    // 2. 客户自定义字段（这部分是核心，必须保留，因为是客户独有的）
    // ============================================================
    /// <summary>该实体保存的客户产品编码。</summary>
    public required string CustomerSku { get; set; }         // 客户那边的料号
    /// <summary>客户那边的系列名。</summary>
    public string? CustomerPdSeries { get; set; }           // 客户那边的系列名
    /// <summary>客户那边的描述。</summary>
    public string? CustomerPdDescription { get; set; }      // 客户那边的描述
    /// <summary>客户那边的颜色称呼。</summary>
    public string? CustomerColorName { get; set; }          // 客户那边的颜色称呼
    /// <summary>客户那边的规格/特性。</summary>
    public string? CustomerPdFeatures { get; set; }         // 客户那边的规格/特性
    /// <summary>客户条码1。</summary>
    public string? CustomerBarcode1 { get; set; }           // 客户条码1
    /// <summary>客户条码2。</summary>
    public string? CustomerBarcode2 { get; set; }           // 客户条码2
    /// <summary>客户产品关系备注。</summary>
    public string? Note { get; set; }

    // ============================================================
    // 3. 商务与物流
    // ============================================================
    /// <summary>美元约定价。</summary>
    public decimal? USDPrice { get; set; }                  // 美元约定价
    /// <summary>人民币约定价。</summary>
    public decimal? CNYPrice { get; set; }                  // 人民币约定价
    /// <summary>生产提前期（天）。</summary>
    public int? LeadTimeDays { get; set; }                  // 生产提前期（天）
    /// <summary>最小起订量。</summary>
    public int? MOQ { get; set; }                           // 最小起订量

    // ============================================================
    // 4. 状态管控
    // ============================================================
    /// <summary>是否启用。</summary>
    public bool IsActive { get; set; } = true;               // 是否启用

    /// <summary>该实体保存的英文产品描述。</summary>
    public string? DescriptionEn { get; set; }

    /// <summary>该实体保存的英文规格特性。</summary>
    public string? FeaturesEn { get; set; }

}

