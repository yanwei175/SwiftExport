namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>定义产品仓储行级写冲突；服务层继续按现有规则转换为通用服务错误。</summary>
    public static class ProductErrorCodes
    {
        /// <summary>主型号编码已被其他记录占用。</summary>
        public const string ProductMasterSkuOccupied = "PRODUCT_MASTER_SKU_OCCUPIED";
        /// <summary>中性产品编码已被其他记录占用。</summary>
        public const string ProductNeutralCodeOccupied = "PRODUCT_NEUTRAL_CODE_OCCUPIED";
        /// <summary>同一主型号的颜色组合已被占用。</summary>
        public const string ProductNeutralMasterColorOccupied = "PRODUCT_NEUTRAL_MASTER_COLOR_OCCUPIED";
    }
}
