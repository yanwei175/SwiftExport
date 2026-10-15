namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>ProductPackageType 内置字典的稳定英文代码；显示名称可改，用户扩展项仍由字典维护。</summary>
    public static class ProductPackageTypeCodes
    {
        /// <summary>内盒；包装层级可按实际需要省略。</summary>
        public const string InnerBox = "InnerBox";
        /// <summary>单套运输盒。</summary>
        public const string ShippingBox = "ShippingBox";
        /// <summary>出货外箱；物料和箱单来源的装箱参数只读取此角色。</summary>
        public const string Carton = "Carton";
        /// <summary>托盘。</summary>
        public const string Pallet = "Pallet";
    }
}
