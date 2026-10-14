using System;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后明细读取快照，包含补件要求和审计信息。</summary>
    public class AfterSalesLineDto : SwiftExport.Contracts.DTOs.Base.BaseDto
    {
        /// <summary>初始化补件读取版本。</summary>
        public AfterSalesLineDto() { Version = 1; }
        /// <summary>所属售后单。</summary>
        public Guid HeaderId { get; set; }
        /// <summary>稳定行号。</summary>
        public int LineNumber { get; set; }
        /// <summary>实际补发物料。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>实际补发非标标记。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>补发SKU。</summary>
        public string SkuSnapshot { get; set; } = string.Empty;
        /// <summary>补发客户型号。</summary>
        public string CustomerSkuSnapshot { get; set; } = string.Empty;
        /// <summary>补发AQ型号。</summary>
        public string LinkSkuSnapshot { get; set; } = string.Empty;
        /// <summary>补发描述。</summary>
        public string DescriptionSnapshot { get; set; } = string.Empty;
        /// <summary>补发颜色。</summary>
        public Guid? ColorId { get; set; }
        /// <summary>补发颜色快照。</summary>
        public string ColorSnapshot { get; set; } = string.Empty;
        /// <summary>补发规格。</summary>
        public string SpecificationSnapshot { get; set; } = string.Empty;
        /// <summary>补发数量，空或零不创建成品需求。</summary>
        public decimal? Quantity { get; set; }
        /// <summary>补件单位快照；缺失保持未知，确认实物需求前必须明确。</summary>
        public string UnitSnapshot { get; set; } = string.Empty;
        /// <summary>行备注。</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>产品控件提供的图片路径，仅供显示，不作为售后存储事实。</summary>
        public string ImagePath { get; set; }

    }
}
