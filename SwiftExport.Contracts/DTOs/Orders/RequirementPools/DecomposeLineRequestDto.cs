using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.DTOs.Orders.RequirementPools
{
    public class DecomposeLineRequestDto
    {
        public long SourceLineId { get; set; } // 来源行的数据库 ID
        public string Sku { get; set; } = string.Empty;// 产品 SKU
        public decimal Quantity { get; set; }// 需求数量
        public DateTime? RequiredDate { get; set; } // 需求日期（交期）
    }
}
