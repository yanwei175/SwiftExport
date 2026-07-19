using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData;

/// <summary>产品文件资料实体，保存产品图纸、说明书、标签和条码文件位置。</summary>
[Table("ProductDetails")]
public class ProductDetail : BaseEntity
{
    /// <summary>该实体保存的产品编码。</summary>
    public string ProductSKU { get; set; } = default!;
    /// <summary>该实体保存的产品标签文件路径。</summary>
    public string? LabelPath { get; set; }
    /// <summary>该实体保存的说明书文件路径。</summary>
    public string? IMPath { get; set; }
    /// <summary>该实体保存的产品图纸文件路径。</summary>
    public string? DrawingPath { get; set; }
    /// <summary>该实体保存的产品线稿文件路径。</summary>
    public string? LinedrawingPath { get; set; }
    /// <summary>该实体保存的产品规格书文件路径。</summary>
    public string? SpecSheetPath { get; set; }
    /// <summary>该实体保存的第一条码文件路径。</summary>
    public string? Barcode1Path { get; set; }
    /// <summary>该实体保存的第二条码文件路径。</summary>
    public string? Barcode2Path { get; set; }

}
