using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品包装规格实体，保存产品装率、尺寸、重量、体积和包材要求。</summary>
[Table("product_package")]
public partial class ProductPackage : BaseEntity
{
    /// <summary>这套包装规格所属的被包装物料身份，逻辑引用 Product.Uid。</summary>
    public Guid? OwnerProductId { get; set; }

    /// <summary>
    /// 包装类型：InnerBox(内盒), Carton(外箱), Pallet(托盘)
    /// </summary>
    public string PackageType { get; set; } = "Carton";

    /// <summary>
    /// 装率：该层级包装内含有的单品(PCS)数量
    /// </summary>
    public int PcsPerPackage { get; set; } = 1;

    /// <summary>该实体保存的包装长度（厘米）。</summary>
    public decimal? Length_CM { get; set; }
    /// <summary>该实体保存的包装宽度（厘米）。</summary>
    public decimal? Width_CM { get; set; }
    /// <summary>该实体保存的包装高度（厘米）。</summary>
    public decimal? Height_CM { get; set; }

    /// <summary>
    /// 该级总净重(KG)
    /// </summary>
    public decimal? Nw_KG { get; set; }

    /// <summary>
    /// 该级总毛重(KG)
    /// </summary>
    public decimal? Gw_KG { get; set; }

    /// <summary>
    /// 包装体积（CBM）；由调用方提供，不自动按尺寸计算。
    /// </summary>
    public decimal? Cbm { get; set; }

    /// <summary>
    /// 是否为出货默认计算规格
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 使用的包材物料身份，逻辑引用 Product.Uid；不承担包装规格自身的 Uid。
    /// </summary>
    public Guid? PackagingMaterialId { get; set; }

    /// <summary>该实体保存的业务备注。</summary>
    public string? Note { get; set; }

}
