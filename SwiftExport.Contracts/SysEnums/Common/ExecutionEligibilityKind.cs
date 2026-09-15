namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>采购新业务引用所需的主数据执行资格对象，禁止作为任意表更新入口。</summary>
    public enum ExecutionEligibilityKind
    {
        /// <summary>产品身份。</summary>
        [System.ComponentModel.Description("产品")]
        Product = 1,
        /// <summary>客户归属。</summary>
        [System.ComponentModel.Description("客户")]
        Customer = 2,
        /// <summary>实际供应商或供应商仓库所属方。</summary>
        [System.ComponentModel.Description("供应商")]
        Supplier = 3
    }
}
