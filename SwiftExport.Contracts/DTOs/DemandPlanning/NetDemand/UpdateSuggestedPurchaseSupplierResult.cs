using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>推荐采购供应商局部更新后的服务端接纳结果。</summary>
    public class UpdateSuggestedPurchaseSupplierResult
    {
        /// <summary>物料需求 Uid。</summary>
        public Guid NetDemandId { get; set; }

        /// <summary>更新后的并发版本。</summary>
        public int Version { get; set; }

        /// <summary>服务端保存的推荐采购供应商 Uid。</summary>
        public Guid? SuggestedPurchaseSupplierId { get; set; }

        /// <summary>保存时读取到的供应商编码；清空时为空。</summary>
        public string SuggestedPurchaseSupplierCode { get; set; }

        /// <summary>保存时读取到的供应商名称；清空时为空。</summary>
        public string SuggestedPurchaseSupplierName { get; set; }
    }
}
