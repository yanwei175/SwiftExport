namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>入库业务类型，确定来源行所属单据；历史值仅供旧查询解释，不得用于新建。</summary>
    public enum InboundDocumentTypeEnum
    {
        [System.ComponentModel.Description("采购收货")]
        PurchaseReceipt = 10,

        /// <summary>生产入库，来源凭证由未来生产模块定义，本阶段不开通写入。</summary>
        [System.ComponentModel.Description("生产入库")]
        ProductionReceipt = 20,

        [System.ComponentModel.Description("历史客供件收货")]
        LegacyCustomerSuppliedReceipt = 11,

        [System.ComponentModel.Description("历史装箱单自动")]
        LegacyPackingListAutoReceipt = 12,

        [System.ComponentModel.Description("期初余额")]
        OpeningBalance = 100
    }
}

