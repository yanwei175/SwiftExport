using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Warehouses
{
    /// <summary>
    /// 入库单头 DTO（对应 ReceiveHead / PurchaseReceiveHead）。
    /// </summary>
    public class ReceiveHeadDto
        : BaseDto
    {
        /// <summary>入库单号（业务主键）。</summary>
        public string ReceiveNo { get; set; } = string.Empty;

        /// <summary>供应商编码。</summary>
        public string SupplierCode { get; set; }

        /// <summary>入库日期。</summary>
        public DateTime? ReceiveDate { get; set; }

        /// <summary>操作人。</summary>
        public string Handler { get; set; }

        /// <summary>备注。</summary>
        public string Remark { get; set; }

        /// <summary>是否已过账</summary>
        public bool IsPosted { get; set; }

        /// <summary>
        /// 冗余 PO 号串（示例：PO24001,PO24002）。
        /// 方便列表搜索，无需 Join 行表。
        /// </summary>
        public string RelatedPoNos { get; set; }
    }
}
