using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品主型号实体，保存主型号层级的默认描述、分类和标签。</summary>
[Table("ProductMaster")]
public class ProductMaster : BaseEntity
{
    /// <summary>
    /// 主型号
    /// </summary>
    public string MasterSku { get; set; } = default!;// 必须唯一 (Unique)

    // === 模具属性 (作为新增 SKU 时的默认值) ===
    /// <summary>描述。</summary>
    public string? Description { get; set; }//描述
    /// <summary>默认配置。</summary>
    public string? Features { get; set; }//默认配置
    /// <summary>分类。</summary>
    public string? ProductClass { get; set; }//分类
    /// <summary>类型。</summary>
    public string? ProductType { get; set; } //类型
    /// <summary>标签属性用来归类。</summary>
    public string? ProductTag { get; set; }//标签属性用来归类

}
