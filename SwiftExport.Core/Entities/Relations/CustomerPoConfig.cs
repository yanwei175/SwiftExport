using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Relations;

/// <summary>客户采购单默认配置实体，保存指定客户和供应商组合的包装、标识及经办人默认要求。</summary>
[Table("CustomerPoConfig")]
public class CustomerPoConfig : BaseEntity
{
    /// <summary>关联的客户主数据 Uid；旧数据按客户编码回填。</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>该实体保存的客户业务编码。</summary>
    public string? CustomerCode { get; set; }
    /// <summary>关联的供应商主数据 Uid；未指定供应商时允许为空。</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>该实体保存的供应商业务编码。</summary>
    public string? SupplierCode { get; set; }
    /// <summary>该实体保存的进水管类型。</summary>
    public string? InletHoseType { get; set; }
    /// <summary>该实体保存的起泡器类型。</summary>
    public string? AeratorType { get; set; }
    /// <summary>该实体保存的托盘类型。</summary>
    public string? PalletType { get; set; }
    /// <summary>该实体保存的包装盒类型。</summary>
    public string? BoxType { get; set; }
    /// <summary>该实体保存的标签类型。</summary>
    public string? LabelType { get; set; }
    /// <summary>该实体保存的说明书类型。</summary>
    public string? IMType { get; set; }
    /// <summary>该实体保存的外箱类型。</summary>
    public string? CartonType { get; set; }
    /// <summary>该实体保存的标志类型。</summary>
    public string? LogoType { get; set; }
    /// <summary>该实体保存的默认销售人员。</summary>
    public string? SalesPerson { get; set; }
    /// <summary>该实体保存的默认采购人员。</summary>
    public string? Purchaser { get; set; }
    /// <summary>该实体保存的业务备注。</summary>
    public string? Note { get; set; }

}
