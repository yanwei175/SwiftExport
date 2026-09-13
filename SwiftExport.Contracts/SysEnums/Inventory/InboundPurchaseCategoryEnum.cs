namespace SwiftExport.Contracts.SysEnums.Inventory
{
    /// <summary>采购入库选源分类，不改变单头入库业务类型。</summary>
    public enum InboundPurchaseCategoryEnum
    {
        /// <summary>非客供件采购行。</summary>
        [System.ComponentModel.Description("正常采购")]
        Normal = 0,
        /// <summary>客供件采购行。</summary>
        [System.ComponentModel.Description("客供件采购")]
        CustomerSupplied = 1
    }
}
