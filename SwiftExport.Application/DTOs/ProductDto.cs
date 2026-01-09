using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.AppLayer.DTOs
{
    public class ProductDto : BaseDto
    {
        [Column("Even")]
        public string Even { get; set; }

        [Column("Status")]
        public string 状态 { get; set; }

        [Column("SKU")]
        public string SKU { get; set; }

        [Column("Description")]
        public string 描述 { get; set; }

        [Column("Finish")]
        public string 颜色 { get; set; }

        [Column("Features")]
        public string 配置 { get; set; }

        [Column("SupplyType")]
        public string 供货类型 { get; set; }

        [Column("BomNo")]
        public string BOM编号 { get; set; }

        [Column("Class")]
        public string 类别 { get; set; }

        [Column("Note")]
        public string 备注 { get; set; }

        [Column("LinkSku")]
        public string 关联SKU { get; set; }

        [Column("MainImage")]
        public string 主图 { get; set; }

        [Column("CustomerCode")]
        public string 客户代码 { get; set; }

        [Column("CSKU")]
        public string 客户SKU { get; set; }

        [Column("SupplierCode")]
        public string 供应商代码 { get; set; }

        [Column("SSKU")]
        public string 供应商SKU { get; set; }
    }
}
