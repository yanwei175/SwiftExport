using SwiftExport.Core.Common;
namespace SwiftExport.Core.Entities.Relations;

public class CustomerProduct : BaseEntity
{

    // ============================================================
    // 1. 核心关联（外键）
    // ============================================================
    public int CustomerId { get; set; }  // 关联客户主表 ID
    public required string CustomerCode { get; set; }  // 关联客户主表编码   
    public int? SkuId { get; set; }       // 关联咱们的产品主表 ID
    public string? Sku { get; set; }
    public string? LinkSku { get; set; }
    // ============================================================
    // 2. 客户自定义字段（这部分是核心，必须保留，因为是客户独有的）
    // ============================================================
    public required string CustomerSku { get; set; }         // 客户那边的料号
    public string? CustomerPdSeries { get; set; }           // 客户那边的系列名
    public string? CustomerPdDescription { get; set; }      // 客户那边的描述
    public string? CustomerColorName { get; set; }          // 客户那边的颜色称呼
    public string? CustomerPdFeatures { get; set; }         // 客户那边的规格/特性
    public string? CustomerBarcode1 { get; set; }           // 客户条码1
    public string? CustomerBarcode2 { get; set; }           // 客户条码2
    public string? CustomerSkuUnit { get; set; }            // 客户那边的单位（如：Set/Pc）
    public string? Note { get; set; }            // 客户那边的单位（如：Set/Pc）

    // ============================================================
    // 3. 商务与物流
    // ============================================================
    public decimal? USDPrice { get; set; }                  // 美元约定价
    public decimal? CNYPrice { get; set; }                  // 人民币约定价
    public string? Currency { get; set; } = "USD";          // 币种
    public int? LeadTimeDays { get; set; }                  // 生产提前期（天）
    public int? MOQ { get; set; }                           // 最小起订量

    // ============================================================
    // 4. 状态管控
    // ============================================================
    public bool IsActive { get; set; } = true;               // 是否启用

    public string? DescriptionEn { get; set; }

    public string? FeaturesEn { get; set; }

}

