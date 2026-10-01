using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>客户范围内命中任一非空商业编号的已有订单提示。</summary>
    public class SalesOrderImportExistingOrderDto
    {
        /// <summary>已有销售订单身份，不作为新订单头身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>系统订单号。</summary>
        public string OrderNo { get; set; }
        /// <summary>发票号。</summary>
        public string InvoiceNo { get; set; }
        /// <summary>客户采购单号。</summary>
        public string CustomerPoNo { get; set; }
        /// <summary>现有单据状态原值。</summary>
        public int DocumentStatus { get; set; }
        /// <summary>现有确认状态原值。</summary>
        public int ConfirmationStatus { get; set; }
    }
}
