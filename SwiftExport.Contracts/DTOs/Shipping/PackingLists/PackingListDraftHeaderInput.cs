using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListDraftHeaderInput
    {
        /// <summary>最终客户逻辑标识。</summary>
        public Guid CustomerId { get; set; }
        /// <summary>单据采用的客户名称快照，不随主数据更新。</summary>
        public string CustomerNameSnapshot { get; set; }
        /// <summary>单据采用的客户地址快照。</summary>
        public string CustomerAddressSnapshot { get; set; }
        /// <summary>自由填写的备注快照，不关联联系人实体。</summary>
        public string AttentionSnapshot { get; set; }
        /// <summary>用户自定义单据日期，不作为库存记账日期。</summary>
        public DateTime InvoiceDate { get; set; }
        /// <summary>集装箱号。</summary>
        public string ContainerNo { get; set; }
        /// <summary>封条号。</summary>
        public string SealNo { get; set; }
        /// <summary>装运箱唛。</summary>
        public string ShippingMark { get; set; }
        /// <summary>单据备注。</summary>
        public string Note { get; set; }
        /// <summary>预计离港日期。</summary>
        public DateTime? Etd { get; set; }
        /// <summary>预计到达日期。</summary>
        public DateTime? Eta { get; set; }
    }
}
