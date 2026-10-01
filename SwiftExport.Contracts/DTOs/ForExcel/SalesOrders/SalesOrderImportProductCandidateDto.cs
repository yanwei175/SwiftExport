using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>客户产品关联到产品主数据的导入候选，不返回成交价格覆盖原表。</summary>
    public class SalesOrderImportProductCandidateDto
    {
        /// <summary>客户产品关联身份。</summary>
        public Guid CustomerProductId { get; set; }
        /// <summary>实际关联产品身份，断链时为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>关联记录的客户型号。</summary>
        public string CustomerSku { get; set; }
        /// <summary>Product 主表真实 SKU。</summary>
        public string Sku { get; set; }
        /// <summary>客户中性型号优先，空值回退产品中性型号。</summary>
        public string LinkSku { get; set; }
        /// <summary>产品主表单位。</summary>
        public string Unit { get; set; }
        /// <summary>产品图片地址，不解析或缓存 Excel 内嵌图片。</summary>
        public string MainImage { get; set; }
        /// <summary>关联和产品均有效且产品单位不为空。</summary>
        public bool IsUsable { get; set; }
    }
}
