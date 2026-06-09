using Microsoft.VisualBasic;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

public class Product : BaseEntity
{
    public string? MasterSku { get; set; } // 逻辑外键：直接存字符串 "AT010011"
    public required string Sku { get; set; }
    public string? DescriptionEn { get; set; }
    public string? DescriptionZh { get; set; }
    public Guid ColorId { get; set; }
    public string? FeaturesEn { get; set; }
    public string? FeaturesZh { get; set; }
    public string? MaterialType { get; set; }
    /// <summary>
    /// 供应方式（仅表示采购/生产/外协，不再承担“是否有BOM”的语义）。
    /// </summary>
    public string SupplyType { get; set; } = default!;
    /// <summary>
    /// BOM版本号。
    /// 约定：0 表示无BOM，>0 表示存在对应版本BOM。
    /// </summary>
    public int BomVersion { get; set; }
    public string? BomNo { get; set; }
    public string? Note { get; set; }
    public string? LinkSku { get; set; }
    public string? MainImage { get; set; }

    //// 装箱基础数据（供销售订单行带出）已经转到 ProductPackage 了，这里先注释掉，等确认不需要了再删掉。
    //public decimal? PcsOfCarton { get; set; }
    //public decimal? NwOfCarton { get; set; }
    //public decimal? GwOfCarton { get; set; }
    //public decimal? CbmOfCarton { get; set; }

    // 产品价格（供订单行带出）
    public decimal? CNYPrice { get; set; }
    public decimal? USDPrice { get; set; }

    /// <summary>
    /// 单件体积（CBM），用于采购/物流统计。
    /// </summary>
    public decimal? UnitCBM { get; set; }

    /// <summary>
    /// 单件重量（KG），用于采购/物流统计。
    /// </summary>
    public decimal? UnitWeight { get; set; }

    public string? DefaultCustomerCode { get; set; }
    public string? DefaultCustomerSku { get; set; }
    public string? DefaultSupplierCode { get; set; }
    public string? DefaultSupplierSku { get; set; }
    public Guid CategoryId { get; set; }


    // Dapper 不会映射这个，因为它没有 setter
    public bool IsNonStandard => !string.IsNullOrWhiteSpace(Sku) && Sku.StartsWith(DocumentTypeStaticsEnum.NonStandardPart);


}
