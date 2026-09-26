using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>整单反向预览输入；不得指定原库存数量和仓库。</summary>
    public class PackingListReversePreviewRequest
    {
        /// <summary>期望箱单版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>反向库存业务日期。</summary>
        public DateTime BusinessDate { get; set; }
        /// <summary>必填反向原因。</summary>
        public string Reason { get; set; }
    }
}
