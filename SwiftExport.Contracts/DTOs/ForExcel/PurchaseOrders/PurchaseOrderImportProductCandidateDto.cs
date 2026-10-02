using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders
{
    /// <summary>当前客户型号的精确采购候选，停用或断链候选仍返回以解释问题。</summary>
    public sealed class PurchaseOrderImportProductCandidateDto
    {
        /// <summary>客户产品关联身份。</summary>
        public Guid CustomerProductId { get; set; }
        /// <summary>真实产品身份，断链时为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>客户型号。</summary>
        public string CustomerSku { get; set; }
        /// <summary>真实系统 SKU。</summary>
        public string Sku { get; set; }
        /// <summary>系统中性型号。</summary>
        public string LinkSku { get; set; }
        /// <summary>产品基础单位。</summary>
        public string Unit { get; set; }
        /// <summary>产品图片地址。</summary>
        public string MainImage { get; set; }
        /// <summary>物料类型，用于客供件送达校验。</summary>
        public string MaterialType { get; set; }
        /// <summary>关联和产品是否有标准采购资格。</summary>
        public bool IsUsable { get; set; }
    }
}

