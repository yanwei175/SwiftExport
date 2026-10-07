namespace SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts
{
    /// <summary>定义需求转采购的请求失败和逐来源结果代码；逐来源失败不改变整批成功包装。</summary>
    public static class PurchaseDraftErrorCodes
    {
        /// <summary>当前版本或剩余数量与预览依据不一致。</summary>
        public const string PreviewExpired = "PREVIEW_EXPIRED";
        /// <summary>准备预览时来源或计划事实已变化。</summary>
        public const string PreviewFactChanged = "PREVIEW_FACT_CHANGED";
        /// <summary>提交的预览令牌不一致。</summary>
        public const string PreviewTokenMismatch = "PREVIEW_TOKEN_MISMATCH";
        /// <summary>所选活动物料需求不属于本次采购来源。</summary>
        public const string NetDemandNotFound = "NET_DEMAND_NOT_FOUND";
        /// <summary>采购来源没有活动需求。</summary>
        public const string SourceNotFound = "SOURCE_NOT_FOUND";
        /// <summary>采购预览关联的客户或供应商资料已不存在。</summary>
        public const string MasterDataNotFound = "MASTER_DATA_NOT_FOUND";
        /// <summary>采购预览所需来源单头已不存在。</summary>
        public const string SourceHeaderNotFound = "SOURCE_HEADER_NOT_FOUND";
        /// <summary>计划项身份、数量或重复集合无效。</summary>
        public const string InvalidItem = "INVALID_ITEM";
        /// <summary>采购草稿目标身份或所属来源无效。</summary>
        public const string InvalidTarget = "INVALID_TARGET";
        /// <summary>来源身份、模式或计划项无效。</summary>
        public const string InvalidSource = "INVALID_SOURCE";
        /// <summary>采购预览模式无效。</summary>
        public const string InvalidPreviewMode = "INVALID_PREVIEW_MODE";
        /// <summary>采购创建入口模式无效。</summary>
        public const string InvalidEntryMode = "INVALID_ENTRY_MODE";
        /// <summary>人工预览必须选择计划项。</summary>
        public const string ItemRequired = "ITEM_REQUIRED";
        /// <summary>本次采购操作必须指定来源。</summary>
        public const string SourceRequired = "SOURCE_REQUIRED";
        /// <summary>采购预览缺少客户身份。</summary>
        public const string CustomerRequired = "CUSTOMER_REQUIRED";
        /// <summary>存在重复活动供应商产品资料，不能确定采购价格。</summary>
        public const string SupplierProductDuplicate = "SUPPLIER_PRODUCT_DUPLICATE";
        /// <summary>追加操作缺少目标草稿和版本。</summary>
        public const string AppendTargetRequired = "APPEND_TARGET_REQUIRED";
        /// <summary>采购执行请求身份缺失。</summary>
        public const string ExecutionRequestRequired = "EXECUTION_REQUEST_REQUIRED";
        /// <summary>本次采购数量无效或超出剩余量。</summary>
        public const string InvalidPurchaseQuantity = "INVALID_PURCHASE_QUANTITY";
        /// <summary>不同来源重复提交命令项或物料需求。</summary>
        public const string DuplicateItem = "DUPLICATE_ITEM";
        /// <summary>同一创建请求重复提交来源。</summary>
        public const string DuplicateSource = "DUPLICATE_SOURCE";
        /// <summary>同一来源供应商具有多个草稿目标。</summary>
        public const string DuplicateTarget = "DUPLICATE_TARGET";
        /// <summary>追加采购草稿时版本已变化。</summary>
        public const string AppendConcurrencyConflict = "APPEND_CONCURRENCY_CONFLICT";
        /// <summary>采购命令项已被不同内容占用。</summary>
        public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
        /// <summary>草稿客户、来源或供应商不一致。</summary>
        public const string AppendTargetMismatch = "APPEND_TARGET_MISMATCH";
        /// <summary>草稿当前不满足安全追加条件。</summary>
        public const string AppendTargetUnsafe = "APPEND_TARGET_UNSAFE";
        /// <summary>来源没有剩余可创建数量。</summary>
        public const string NothingToCreate = "NOTHING_TO_CREATE";
        /// <summary>来源一键采购只允许新建草稿。</summary>
        public const string OneClickMustCreateNew = "ONE_CLICK_MUST_CREATE_NEW";
        /// <summary>来源一键采购必须提交全部剩余数量。</summary>
        public const string OneClickRequiresAllRemaining = "ONE_CLICK_REQUIRES_ALL_REMAINING";
        /// <summary>来源一键采购只允许单个来源。</summary>
        public const string OneClickSingleSource = "ONE_CLICK_SINGLE_SOURCE";
        /// <summary>本来源预览有阻断项，不能创建。</summary>
        public const string PreviewBlocked = "PREVIEW_BLOCKED";
        /// <summary>来源预览中的警告尚未明确确认。</summary>
        public const string WarningConfirmationRequired = "WARNING_CONFIRMATION_REQUIRED";
        /// <summary>逐来源创建不能接管已有事务。</summary>
        public const string TransactionAlreadyActive = "TRANSACTION_ALREADY_ACTIVE";
        /// <summary>逐来源创建发生未预期故障。</summary>
        public const string UnexpectedError = "UNEXPECTED_ERROR";
        /// <summary>采购创建依据不完整，不能推断写入。</summary>
        public const string CorruptedState = "CORRUPTED_STATE";
        /// <summary>采购单号获取失败。</summary>
        public const string DocumentNoFailed = "DOCUMENT_NO_FAILED";
        /// <summary>采购命令项只具有部分历史事实，不能重放。</summary>
        public const string IdempotencyPartialState = "IDEMPOTENCY_PARTIAL_STATE";
        /// <summary>采购头写入失败，来源事务不提交。</summary>
        public const string PurchaseHeadInsertFailed = "PURCHASE_HEAD_INSERT_FAILED";
        /// <summary>采购行写入失败，来源事务不提交。</summary>
        public const string PurchaseLineInsertFailed = "PURCHASE_LINE_INSERT_FAILED";
        /// <summary>采购贡献写入失败，来源事务不提交。</summary>
        public const string PurchaseLinkInsertFailed = "PURCHASE_LINK_INSERT_FAILED";
    }
}
