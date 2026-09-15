using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListManagerListItemDto : BaseDto
    {
        /// <summary>装箱单业务编号。</summary>
        public string PackingListNo { get; set; }
        /// <summary>最终客户逻辑标识。</summary>
        public Guid CustomerId { get; set; }
        /// <summary>单据采用的客户名称快照，不随主数据更新。</summary>
        public string CustomerNameSnapshot { get; set; }
        /// <summary>用户自定义单据日期，不作为库存记账日期。</summary>
        public DateTime InvoiceDate { get; set; }
        /// <summary>集装箱号。</summary>
        public string ContainerNo { get; set; }
        /// <summary>封条号。</summary>
        public string SealNo { get; set; }
        /// <summary>预计离港日期。</summary>
        public DateTime? Etd { get; set; }
        /// <summary>预计到达日期。</summary>
        public DateTime? Eta { get; set; }
        /// <summary>单据生命周期，与库存状态分别表达。</summary>
        public PackingListLifecycleStatusEnum LifecycleStatus { get; set; }
        /// <summary>库存过账状态，不表示已实现过账入口。</summary>
        public PackingListPostingStatusEnum PostingStatus { get; set; }
        /// <summary>有效行发票号去重汇总，只读。</summary>
        public string InvoiceNosSummary { get; set; }
    }
}
