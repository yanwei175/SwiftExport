using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品主数据实体，保存稳定产品身份、物料属性及当前可选的采购、制造和仓库参考关系。</summary>
[Table("product")]
public partial class Product : BaseEntity
{
    /// <summary>是否允许新业务引用；停用保留历史身份，不等同软删除。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>独立引用的主型号身份；允许为空或失效，不从中性产品推导。</summary>
    public Guid? ProductMasterId { get; set; }
    /// <summary>物料自身的产品型号；可自由维护，按同名编码反查中性产品，无匹配也不影响业务。</summary>
    public string? LinkSku { get; set; }
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

    /// <summary>默认供应商权威关系；当前编码由供应商资料实时投影。</summary>
    public Guid? DefaultSupplierId { get; set; }
    /// <summary>产品建议使用的仓库标识；仅作当前参考，不决定单据实际仓库。</summary>
    public Guid? DefaultWarehouseSiteId { get; set; }
    /// <summary>实际制造商标识，独立于默认采购供应商。</summary>
    public Guid? ManufacturerId { get; set; }
    /// <summary>制造商型号；允许不选择制造商时独立记录。</summary>
    [DbColumn(StoreType = "varchar(200)")]
    public string? ManufacturerModel { get; set; }
}
