namespace SwiftExport.Contracts.DTOs.DemandPlanning
{
    /// <summary>定义来源接收、物料建立和回退的专用代码，不把需求结果项误作为 HTTP 失败。</summary>
    public static class DemandPlanningErrorCodes
    {
        /// <summary>来源拆解数量必须为正数。</summary>
        public const string InvalidQuantity = "INVALID_QUANTITY";
        /// <summary>本次需求拆解修订必须为正数。</summary>
        public const string InvalidRevision = "INVALID_REVISION";
        /// <summary>推荐采购供应商身份无效或不可用。</summary>
        public const string InvalidSupplier = "INVALID_SUPPLIER";
        /// <summary>需求根产品不存在或已删除。</summary>
        public const string ProductNotFound = "PRODUCT_NOT_FOUND";
        /// <summary>根产品声明的有效 BOM 缺少明细。</summary>
        public const string BomNotFound = "BOM_NOT_FOUND";
        /// <summary>BOM 子产品不存在。</summary>
        public const string BomChildNotFound = "BOM_CHILD_NOT_FOUND";
        /// <summary>活动物料需求修订已变化。</summary>
        public const string RevisionConflict = "REVISION_CONFLICT";
        /// <summary>相同来源版本提交了不同内容。</summary>
        public const string SourceVersionConflict = "SOURCE_VERSION_CONFLICT";
        /// <summary>提交来源版本早于已接收版本。</summary>
        public const string StaleSourceVersion = "STALE_SOURCE_VERSION";
        /// <summary>待建立或回退的成品需求不存在。</summary>
        public const string SourceNotFound = "SOURCE_NOT_FOUND";
        /// <summary>成品需求自身版本已变化。</summary>
        public const string SourceChanged = "SOURCE_CHANGED";
        /// <summary>成品需求不是可建立物料的活动状态。</summary>
        public const string InvalidSourceLifecycle = "INVALID_SOURCE_LIFECYCLE";
        /// <summary>来源行存在重复活动需求。</summary>
        public const string DuplicateActiveSource = "DUPLICATE_ACTIVE_SOURCE";
        /// <summary>物料需求不存在、回退或作废。</summary>
        public const string MaterialDemandNotActive = "MATERIAL_DEMAND_NOT_ACTIVE";
        /// <summary>客供件不能作为标准来源根产品。</summary>
        public const string CustomerSuppliedRootNotAllowed = "CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED";
        /// <summary>物料需求还有活动执行贡献，不能回退。</summary>
        public const string DownstreamExecutionExists = "DOWNSTREAM_EXECUTION_EXISTS";
        /// <summary>组成边数量、比例或子产品口径无效。</summary>
        public const string InvalidBomLine = "INVALID_BOM_LINE";
        /// <summary>客供件资料不满足需求采用条件。</summary>
        public const string InvalidCustomerSuppliedProduct = "INVALID_CUSTOMER_SUPPLIED_PRODUCT";
        /// <summary>根产品 BOM 编号与有效标记不一致。</summary>
        public const string InvalidProductBom = "INVALID_PRODUCT_BOM";
        /// <summary>非标来源不得关联标准产品。</summary>
        public const string NonStandardProductForbidden = "NON_STANDARD_PRODUCT_FORBIDDEN";
        /// <summary>来源产品缺少明确计量单位。</summary>
        public const string ProductUnitRequired = "PRODUCT_UNIT_REQUIRED";
        /// <summary>生产根产品客供件流程尚无可执行定义。</summary>
        public const string ProductionCustomerSuppliedNotSupported = "PRODUCTION_CUSTOMER_SUPPLIED_NOT_SUPPORTED";
        /// <summary>来源商业事实变化需要先处置既有采用。</summary>
        public const string SourceChangeRequiresDisposition = "SOURCE_CHANGE_REQUIRES_DISPOSITION";
        /// <summary>成品需求数量与来源行不一致。</summary>
        public const string SourceDemandMismatch = "SOURCE_DEMAND_MISMATCH";
        /// <summary>来源仍有有效下游采用，不能退出。</summary>
        public const string SourceExitBlocked = "SOURCE_EXIT_BLOCKED";
        /// <summary>来源类型不受当前需求链支持。</summary>
        public const string SourceTypeNotSupported = "SOURCE_TYPE_NOT_SUPPORTED";
        /// <summary>仍有本次未提交的活动来源行。</summary>
        public const string UnsubmittedActiveSource = "UNSUBMITTED_ACTIVE_SOURCE";
        /// <summary>需求根产品供货类型无效。</summary>
        public const string UnsupportedSupplyType = "UNSUPPORTED_SUPPLY_TYPE";
        /// <summary>来源接收请求重复提交行身份。</summary>
        public const string DuplicateSourceLine = "DUPLICATE_SOURCE_LINE";
        /// <summary>来源退出的物料和执行历史无法证明。</summary>
        public const string SourceHistoryInvalid = "SOURCE_HISTORY_INVALID";
        /// <summary>历史活动来源缺少上游版本，须核查事实。</summary>
        public const string SourceVersionMissing = "SOURCE_VERSION_MISSING";
    }
}
