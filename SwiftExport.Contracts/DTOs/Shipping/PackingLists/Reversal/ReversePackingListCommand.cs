using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>整单反过账命令；只允许依据原执行事实恢复库存。</summary>
    public class ReversePackingListCommand
    {
        /// <summary>稳定反向命令身份。</summary>
        public Guid CommandId { get; set; }
        /// <summary>预览时的箱单版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>反向库存业务日期。</summary>
        public DateTime BusinessDate { get; set; }
        /// <summary>与预览一致的反向原因。</summary>
        public string Reason { get; set; }
        /// <summary>反向事实预览令牌。</summary>
        public string PreviewToken { get; set; }
    }
}
