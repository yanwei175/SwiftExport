using SwiftExport.Contracts.DTOs.Products;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesProductDto : ProductDto
    {
        //public string State { get; set; }
        /// <summary>
        /// 客户产品主键 Id（来源于 CustomerProduct.Id，用于前端后续联动或回填时定位）。
        /// </summary>
        public Guid? CustomerProductId { get; set; }

        /// <summary>
        /// 产品主键 Id（来源于 Product.Id）。
        /// </summary>
        public Guid? ProductId { get; set; }
        public string CustomerCode { get; set; }

        public string CustomerSku { get; set; }

        /// <summary>
        /// 产品英文颜色名（对应 ProductColor.ColorNameEn）。
        /// </summary>
        public string ColorNameEn { get; set; }
    }
}
