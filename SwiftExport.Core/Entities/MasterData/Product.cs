using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品主数据实体，保存公司内部稳定产品身份、物料属性和当前默认供应商关系。</summary>
[Table("Product")]
public partial class Product : BaseEntity
{
    /// <summary>是否允许新业务引用；停用保留历史身份，不等同软删除。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>所属主型号标识；使用逻辑外键，由聚合保存上下文赋值。</summary>
    public Guid? ProductMasterId { get; set; }
    /// <summary>逻辑外键：直接存字符串 &quot;AT010011&quot;。</summary>
    public string? MasterSku { get; set; } // 逻辑外键：直接存字符串 "AT010011"
    /// <summary>该实体保存的公司产品编码。</summary>
    public required string Sku { get; set; }
    /// <summary>该实体保存的英文产品描述。</summary>
    public string? DescriptionEn { get; set; }
    /// <summary>该实体保存的中文产品描述。</summary>
    public string? DescriptionZh { get; set; }
    /// <summary>关联的产品颜色唯一标识。</summary>
    public Guid? ColorId { get; set; }
    /// <summary>该实体保存的英文规格特性。</summary>
    public string? FeaturesEn { get; set; }
    /// <summary>该实体保存的中文规格特性。</summary>
    public string? FeaturesZh { get; set; }
    /// <summary>该实体保存的物料角色类型。</summary>
    public string? MaterialType { get; set; }
    /// <summary>
    /// 产品当前基础计量单位，保存 Unit 字典项的英文代码；历史交易使用各自的 UnitSnapshot。
    /// </summary>
    public string? Unit { get; set; }
    /// <summary>
    /// 供应方式（仅表示采购/生产/外协，不再承担“是否有BOM”的语义）。
    /// </summary>
    public string SupplyType { get; set; } = default!;
    /// <summary>
    /// BOM版本号。
    /// 约定：0 表示无BOM，>0 表示存在对应版本BOM。
    /// </summary>
    public int BomVersion { get; set; }
    /// <summary>该实体保存的BOM 编号。</summary>
    public string? BomNo { get; set; }
    /// <summary>该实体保存的业务备注。</summary>
    public string? Note { get; set; }
    /// <summary>该实体保存的关联产品编码。</summary>
    public string? LinkSku { get; set; }
    /// <summary>该实体保存的产品主图路径。</summary>
    public string? MainImage { get; set; }

    // 产品价格（供订单行带出）
    /// <summary>该实体保存的人民币单价或参考价。</summary>
    public decimal? CNYPrice { get; set; }
    /// <summary>该实体保存的美元参考价。</summary>
    public decimal? USDPrice { get; set; }

    /// <summary>
    /// 每基础单位体积（CBM），用于采购/物流统计。
    /// </summary>
    public decimal? UnitCBM { get; set; }

    /// <summary>
    /// 每基础单位重量（KG），用于采购/物流统计。
    /// </summary>
    public decimal? UnitWeight { get; set; }

    /// <summary>旧模型保留的默认客户编码兼容字段；目标模型以 CustomerProduct 关系为准。</summary>
    public string? DefaultCustomerCode { get; set; }
    /// <summary>旧模型保留的默认客户产品编码兼容字段；目标模型以 CustomerProduct 关系为准。</summary>
    public string? DefaultCustomerSku { get; set; }
    /// <summary>默认供应商权威关系；编码仅作为兼容和人工查账字段。</summary>
    public Guid? DefaultSupplierId { get; set; }
    /// <summary>旧模型保留的默认供应商编码兼容字段；目标模型以 DefaultSupplierId 及 SupplierProduct 关系为准。</summary>
    public string? DefaultSupplierCode { get; set; }
    /// <summary>旧模型保留的默认供应商产品编码兼容字段；目标模型以 SupplierProduct 关系为准。</summary>
    public string? DefaultSupplierSku { get; set; }

    // Dapper 不会映射这个，因为它没有 setter
    public bool IsNonStandard => !string.IsNullOrWhiteSpace(Sku) && Sku.StartsWith(DocumentTypeStaticsEnum.NonStandardPart);


}
