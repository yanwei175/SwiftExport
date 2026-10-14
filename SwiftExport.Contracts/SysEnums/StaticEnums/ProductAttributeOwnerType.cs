namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>标签选择所属的产品对象类型；已用于数据库和契约的数值保持稳定。</summary>
    public enum ProductAttributeOwnerType
    {
        /// <summary>产品主型号，模板由其自身维护。</summary>
        ProductMaster = 1,
        /// <summary>客户产品，选择独立于其关联 SKU。</summary>
        CustomerProduct = 2,
        /// <summary>中性产品，选择独立于其所属主型号。</summary>
        ProductNeutral = 3,
        /// <summary>物料产品，选择归属实际物料身份。</summary>
        Product = 4
    }
}
