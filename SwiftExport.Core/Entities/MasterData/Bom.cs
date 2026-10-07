using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.MasterData
{
    /// <summary>物料清单组成行实体，保存父产品在指定 BOM 版本下的组件及用量比例。</summary>
    [Table("bom")]
public partial class Bom : BaseEntity
    {
        /// <summary>该实体保存的BOM 编号。</summary>
        public string BomNo { get; set; } = default!;
        /// <summary>该实体保存的BOM 版本。</summary>
        public int BomVersion { get; set; }
        /// <summary>关联的父产品唯一标识。</summary>
        public Guid? ParentProductId { get; set; }
        /// <summary>该实体保存的父产品编码。</summary>
        public string ParentSkuSnapshot { get; set; } = default!;
        /// <summary>该实体保存的单据行号。</summary>
        public int LineNumber { get; set; } = 1;
        /// <summary>关联的组件产品唯一标识。</summary>
        public Guid? ComponentProductId { get; set; }
        /// <summary>该实体保存的组件产品编码。</summary>
        public string ComponentSkuSnapshot { get; set; } = default!;

        /// <summary>组件用量比例的分子，与分母共同计算父需求对应的组件需求量。</summary>
        public int Numerator { get; set; } = 1;
        /// <summary>组件用量比例的分母，必须大于零。</summary>
        public int Denominator { get; set; } = 1;
        /// <summary>该实体保存的业务备注。</summary>
        public string? Note { get; set; }
    }
}
