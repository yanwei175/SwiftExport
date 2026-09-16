using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单 Picker 使用的可交付来源及唯一默认包装投影。</summary>
    public class PackingListSourceCandidateDto
    {
        /// <summary>来源需求唯一标识。</summary>
        public Guid GrossDemandId { get; set; }
        /// <summary>候选来源并发版本。</summary>
        public int DemandVersion { get; set; }
        /// <summary>上游事实版本，未知为空。</summary>
        public int? SourceVersion { get; set; }
        /// <summary>已证明的最终客户。</summary>
        public Guid CustomerId { get; set; }
        /// <summary>标准产品标识；非标来源为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>是否为不产生库存事实的非标来源。</summary>
        public bool IsNonStandard { get; set; }
        /// <summary>来源类型。</summary>
        public string SourceType { get; set; }
        /// <summary>来源单号快照。</summary>
        public string SourceNo { get; set; }
        /// <summary>来源行号快照。</summary>
        public int? SourceLineNumber { get; set; }
        /// <summary>来源要求数量。</summary>
        public decimal DemandQty { get; set; }
        /// <summary>正式交付净额；不可用时为空，不伪装零。</summary>
        public decimal? DeliveredQty { get; set; }
        /// <summary>剩余交付量；不可用时为空。</summary>
        public decimal? RemainingQty { get; set; }
        /// <summary>交付投影不可用原因。</summary>
        public string DeliveryUnavailableReason { get; set; }
        /// <summary>产品编码快照。</summary>
        public string SkuSnapshot { get; set; }
        /// <summary>关联型号快照。</summary>
        public string LinkSkuSnapshot { get; set; }
        /// <summary>客户型号快照。</summary>
        public string CustomerSkuSnapshot { get; set; }
        /// <summary>英文描述快照。</summary>
        public string DescriptionEnSnapshot { get; set; }
        /// <summary>英文颜色名称快照。</summary>
        public string ColorNameEnSnapshot { get; set; }
        /// <summary>库存及交付数量的计量单位快照。</summary>
        public string UnitSnapshot { get; set; }
        /// <summary>每包数量；未知为空，已知时须大于零。</summary>
        public decimal? PcsPerPackage { get; set; }
        /// <summary>每包净重。未知为空。</summary>
        public decimal? NwPerPackage { get; set; }
        /// <summary>每包毛重。未知为空。</summary>
        public decimal? GwPerPackage { get; set; }
        /// <summary>每包体积。未知为空。</summary>
        public decimal? CbmPerPackage { get; set; }
        /// <summary>客户采购编号文本快照，不作为关系主键。</summary>
        public string CustomerPoNo { get; set; }
        /// <summary>查询展示图片，不持久化。</summary>
        public string ImagePath { get; set; }
    }
}
