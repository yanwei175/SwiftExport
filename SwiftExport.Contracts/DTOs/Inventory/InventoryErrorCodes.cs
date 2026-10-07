namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>定义自动库存参与端口的资源、单据和历史完整性代码，保留当前失败与重试边界。</summary>
    public static class InventoryErrorCodes
    {
        /// <summary>原库存单据不是本箱单有效已过账自动单据。</summary>
        public const string DocumentStateInvalid = "DOCUMENT_STATE_INVALID";
        /// <summary>库存内核拒绝自动过账。</summary>
        public const string InventoryBlocked = "INVENTORY_BLOCKED";
        /// <summary>库存产品或采购产品单位不一致。</summary>
        public const string ProductUnitInvalid = "PRODUCT_UNIT_INVALID";
        /// <summary>本次自动收货超出采购剩余可收量。</summary>
        public const string PurchaseCapacityExceeded = "PURCHASE_CAPACITY_EXCEEDED";
        /// <summary>采购来源不允许当前自动收货。</summary>
        public const string PurchaseNotReceivable = "PURCHASE_NOT_RECEIVABLE";
        /// <summary>自动收货仓不是采购供应商的有效虚拟仓。</summary>
        public const string SupplierWarehouseInvalid = "SUPPLIER_WAREHOUSE_INVALID";
        /// <summary>自动库存单据类型不支持。</summary>
        public const string UnsupportedType = "UNSUPPORTED_TYPE";
        /// <summary>仓库不可用于新增库存事实。</summary>
        public const string WarehouseInvalid = "WAREHOUSE_INVALID";
        /// <summary>原库存流水缺失、重复或与单据证据不一致。</summary>
        public const string LedgerIntegrity = "LEDGER_INTEGRITY";
        /// <summary>自动库存单据取号失败。</summary>
        public const string NumberFailed = "NUMBER_FAILED";
        /// <summary>自动库存历史或内核结果不完整。</summary>
        public const string OutcomeInvalid = "OUTCOME_INVALID";
        /// <summary>正式收货历史缺少来源证明。</summary>
        public const string ReceiptHistoryInvalid = "RECEIPT_HISTORY_INVALID";
        /// <summary>自动过账资源已经准备，不能更换事务。</summary>
        public const string ResourcesAlreadyPrepared = "RESOURCES_ALREADY_PREPARED";
        /// <summary>自动过账尚未准备完整资源。</summary>
        public const string ResourcesNotPrepared = "RESOURCES_NOT_PREPARED";
        /// <summary>自动过账使用了未提前声明或锁定的资源。</summary>
        public const string UndeclaredResource = "UNDECLARED_RESOURCE";
    }
}
