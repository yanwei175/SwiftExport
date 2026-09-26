using System;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand
{
    /// <summary>更新一条活动物料需求推荐采购供应商的窄命令；不修改采购事实或其他物料字段。</summary>
    public class UpdateSuggestedPurchaseSupplierCommand
    {
        /// <summary>物料需求 Uid。</summary>
        public Guid NetDemandId { get; set; }

        /// <summary>客户端读取到的物料需求并发版本。</summary>
        public int Version { get; set; }

        /// <summary>新的推荐采购供应商 Uid；为空表示显式清空建议。</summary>
        public Guid? SuggestedPurchaseSupplierId { get; set; }
    }
}
