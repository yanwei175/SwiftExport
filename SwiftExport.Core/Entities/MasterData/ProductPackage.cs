using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

[Table("ProductPackage")]
public class ProductPackage : BaseEntity
{/// <summary>
 /// 关联产品主键Id
 /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// 包装类型：InnerBox(内盒), Carton(外箱), Pallet(托盘)
    /// </summary>
    public string PackageType { get; set; } = "Carton";

    /// <summary>
    /// 装率：该层级包装内含有的单品(PCS)数量
    /// </summary>
    public int PcsPerPackage { get; set; } = 1;

    public decimal? Length_CM { get; set; }
    public decimal? Width_CM { get; set; }
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
    /// 体积CBM (数据库计算列)
    /// 特别注意：在EF Core中需标记为由数据库生成
    /// </summary>
    public decimal? Cbm_m3 { get; set; }

    /// <summary>
    /// 是否为出货默认计算规格
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 对应的包材物料号 (关联BOM)
    /// </summary>
    public string? PackagingMaterialSku { get; set; }

    public string? Note { get; set; }

}
