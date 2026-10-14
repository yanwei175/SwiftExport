namespace SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import
{
    /// <summary>中性产品导入的一行原始文本；编码用于匹配，空白可空字段清空对应资料。</summary>
    public class ProductNeutralImportRowDto
    {
        /// <summary>原工作表一基行号，由保存回执的 RowIndex 原样返回。</summary>
        public int SourceRowNumber { get; set; }
        public string MasterSku { get; set; }
        public string ProductCode { get; set; }
        public string ColorCodeUnique { get; set; }
        public string DefaultPurchasePriceCny { get; set; }
        public string DefaultSalesPriceUsd { get; set; }
        public string DefaultExchangeRate { get; set; }
        public string ProfitMargin { get; set; }
        public string ImagePath { get; set; }
    }
}
