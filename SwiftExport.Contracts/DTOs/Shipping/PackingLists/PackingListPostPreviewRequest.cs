using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>过账预览输入；库存业务日期独立于单据 InvoiceDate。</summary>
    public class PackingListPostPreviewRequest
    {
        /// <summary>期望箱单版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>库存业务日期。</summary>
        public DateTime BusinessDate { get; set; }
    }
}
