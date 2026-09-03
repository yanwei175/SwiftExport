using System;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups
{
    /// <summary>采购产品候选分页条件；客户和实际供应商用于读取各自有效的产品关系。</summary>
    public sealed class ProcurementProductSearchRequest : PagedRequestBase
    {
        /// <summary>当前采购归属客户；为空时不读取客户产品关系。</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>当前实际供应商；为空时不读取供应商产品关系。</summary>
        public Guid? SupplierId { get; set; }
    }
}
