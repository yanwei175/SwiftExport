using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.AppLayer.DTOs
{
    public class ProductCategoryDto : BaseDto
    {
        [Column("Even")]
        public string Even { get; set; }

        [Column("Status")]
        public string 状态 { get; set; }

        [Column("CategoryName")]
        public string 分类名称 { get; set; } // 对应 CategoryName

        [Column("ParentId")]
        public int? 父级ID { get; set; }

        [Column("ParentName")]
        public string 父级名称 { get; set; }

        [Column("SortNo")]
        public int 排序编号 { get; set; }

        [Column("CategoryNo")]
        public string 分类编号 { get; set; }

        // 树形结构必须
        public List<ProductCategoryDto> 子集列表 { get; set; } = new List<ProductCategoryDto>();
    }
}
