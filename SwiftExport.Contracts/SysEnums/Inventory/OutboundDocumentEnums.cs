using System.ComponentModel;

namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>库存出库单据类型；普通库存出库不代表客户交付。</summary>
    public enum OutboundDocumentTypeEnum
    {
        [Description("客户最终发货")]
        FinalCustomerShipment = 20,
        [Description("客供料消耗")]
        CustomerSuppliedConsumption = 21,
        /// <summary>人工普通库存出库。</summary>
        [Description("普通库存出库")]
        StockIssue = 22
    }

    /// <summary>库存出库过账状态；草稿不预占库存。</summary>
    public enum OutboundPostingStatusEnum
    {
        /// <summary>尚未写入库存流水。</summary>
        [Description("未过账")]
        Unposted = 0,
        [Description("已过账")]
        Posted = 1,
        [Description("已反过账")]
        Reversed = 8
    }
}
