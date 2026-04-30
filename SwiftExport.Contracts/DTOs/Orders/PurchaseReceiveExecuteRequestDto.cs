using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 执行采购入库请求 DTO。
    /// </summary>
    public class PurchaseReceiveExecuteRequestDto
    {
        /// <summary>
        /// 关联采购业务单号。
        /// </summary>
        public string PoNo { get; set; } = string.Empty;

        /// <summary>
        /// 关联采购系统内码。
        /// </summary>
        public string OrderNo { get; set; } = string.Empty;

        /// <summary>
        /// 供应商编码。
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;

        /// <summary>
        /// 入库/装柜日期。
        /// </summary>
        public DateTime? ReceiveDate { get; set; }

        /// <summary>
        /// 操作人。
        /// </summary>
        public string Handler { get; set; }

        /// <summary>
        /// 备注。
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// 入库行列表。
        /// </summary>
        public List<PurchaseReceiveExecuteLineDto> Lines { get; set; } = new List<PurchaseReceiveExecuteLineDto>();
    }
}
