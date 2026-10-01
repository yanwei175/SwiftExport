using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>导入行检查输入，保留用户原始商业值，允许显式标记非标。</summary>
    public class SalesOrderImportLineInput
    {
        /// <summary>前端行关联标识，仅用于结果对应，不是 ProductId。</summary>
        public Guid ClientLineId { get; set; }
        /// <summary>Excel Customer Code，客户产品型号。</summary>
        public string CustomerSku { get; set; }
        /// <summary>多候选时用户明确选择的客户产品关联；必须属于本次客户及型号候选。</summary>
        public Guid? SelectedCustomerProductId { get; set; }
        /// <summary>Excel Aquavit Code，中性型号。</summary>
        public string LinkSku { get; set; }
        /// <summary>用户明确选择非标，不能由匹配失败自动赋值。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>非标行必填描述。</summary>
        public string DescriptionEn { get; set; }
        /// <summary>非标行必填单位；标准行读取产品单位。</summary>
        public string Unit { get; set; }
        /// <summary>数量必须大于零。</summary>
        public decimal? Qty { get; set; }
        /// <summary>可空或非负人民币成交单价。</summary>
        public decimal? CnyPrice { get; set; }
        /// <summary>可空或非负美元成交单价。</summary>
        public decimal? UsdPrice { get; set; }
        /// <summary>可空；填写时须大于零。</summary>
        public decimal? PcsOfCarton { get; set; }
        /// <summary>可空或非负每箱毛重。</summary>
        public decimal? GwOfCarton { get; set; }
        /// <summary>可空或非负每箱净重。</summary>
        public decimal? NwOfCarton { get; set; }
        /// <summary>可空或非负每箱体积。</summary>
        public decimal? CbmOfCarton { get; set; }
        /// <summary>用户比较原表及系统型号后确认差异。</summary>
        public bool AcceptLinkSkuMismatch { get; set; }
    }
}
