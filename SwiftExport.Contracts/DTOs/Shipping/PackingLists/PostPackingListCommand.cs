using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>确认过账命令；服务端重新计算并验证预览令牌。</summary>
    public class PostPackingListCommand
    {
        /// <summary>稳定命令身份；网络结果不确定时继续使用原身份。</summary>
        public Guid CommandId { get; set; }
        /// <summary>预览时的箱单版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>预览时的库存业务日期。</summary>
        public DateTime BusinessDate { get; set; }
        /// <summary>服务端生成的规范化事实令牌。</summary>
        public string PreviewToken { get; set; }
    }
}
