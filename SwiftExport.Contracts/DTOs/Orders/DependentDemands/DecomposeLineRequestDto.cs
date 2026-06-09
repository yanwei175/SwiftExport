using System;

namespace SwiftExport.Contracts.DTOs.Orders.DependentDemands
{
    /// <summary>
    /// 分解行请求数据传输行对象
    /// </summary>
    public class DecomposeLineRequestDto
    {
        public Guid SourceLineId { get; set; } // 来源行的数据库 ID
        public string Sku { get; set; } = string.Empty;// 产品 SKU
        /// <summary>
        /// 物料描述（SKU 为空的非标件场景用于直接落需求池）。
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 颜色名称（非标件场景可选）。
        /// </summary>
        public string ColorName { get; set; }
        /// <summary>
        /// 规格特征（非标件场景可选）。
        /// </summary>
        public string Features { get; set; }
        public decimal Quantity { get; set; }// 需求数量
        public DateTime? RequiredDate { get; set; } // 需求日期（交期）
    }
}
