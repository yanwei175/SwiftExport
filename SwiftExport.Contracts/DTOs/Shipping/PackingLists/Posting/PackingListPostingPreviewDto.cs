using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>只读过账或整单反向方案；存在阻断时不能执行。</summary>
    public class PackingListPostingPreviewDto
    {
        /// <summary>箱单身份。</summary>
        public Guid PackingListId { get; set; }
        /// <summary>参与预览的箱单版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>参与计算的库存业务日期。</summary>
        public DateTime BusinessDate { get; set; }
        /// <summary>是否为整单反向预览。</summary>
        public bool IsReverse { get; set; }
        /// <summary>无阻断时提供；客户端不得修改。</summary>
        public string PreviewToken { get; set; }
        /// <summary>完整动作列表，不返回可执行的部分计划。</summary>
        public List<PackingListPostingActionDto> Actions { get; set; } = new List<PackingListPostingActionDto>();
        /// <summary>逐来源或整单阻断列表。</summary>
        public List<PackingListPostingBlockerDto> Blockers { get; set; } = new List<PackingListPostingBlockerDto>();
    }
}
