using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>导入单头校验输入，不包含订单身份、版本或 UpdateDate。</summary>
    public class SalesOrderImportHeaderInput
    {
        /// <summary>原表发票号，用于提示重复订单。</summary>
        public string InvoiceNo { get; set; }
        /// <summary>原表客户采购单号，用于提示重复订单。</summary>
        public string CustomerPoNo { get; set; }
        /// <summary>Date 对应订单日期。</summary>
        public DateTime? OrderDate { get; set; }
        /// <summary>需明确确认的币种：CNY 或 USD。</summary>
        public string Currency { get; set; }
        /// <summary>明确输入的正汇率，不从双币种报价推算。</summary>
        public decimal? ExchangeRate { get; set; }
        /// <summary>可选定金比例，填写时在 0 到 1 之间。</summary>
        public decimal? DepositRate { get; set; }
    }
}
