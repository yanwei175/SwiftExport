using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单草稿行输入；来源身份由服务端复核，包装接受手工输入，箱数与小计由服务端重算。</summary>
    public class PackingListDraftLineInput
    {
        /// <summary>行稳定身份；新增行也由客户端生成非空 Uid。</summary>
        public Guid Uid { get; set; }
        /// <summary>新增为0，已有行须提交原版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>单据内行号，同来源允许多行。</summary>
       public int LineNumber { get; set; }
        /// <summary>同单据内一只拼箱的稳定标识；为空表示普通包装行，不关联独立组表。</summary>
        public Guid? PackingGroupId { get; set; }
        /// <summary>交付来源锚点；仅未关联草稿允许为空，不表示采购或物料分摊。</summary>
        public Guid? GrossDemandId { get; set; }
        /// <summary>标准行产品标识；非标行为空。</summary>
        public Guid? ProductId { get; set; }
        /// <summary>未关联行由用户明确声明非标；已关联行由来源复核。</summary>
        public bool IsNonStandardSnapshot { get; set; }
        /// <summary>未关联标准行显式选定的客户产品关系，非标及已关联行不使用。</summary>
        public Guid? SelectedCustomerProductId { get; set; }
        /// <summary>Excel 中性型号原文，用于检查差异，不替代系统产品身份。</summary>
        public string OriginalLinkSku { get; set; }
        /// <summary>用户明确确认原文与系统中性型号的差异。</summary>
        public bool AcceptLinkSkuMismatch { get; set; }
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
        /// <summary>本行计划交付数量，草稿不形成正式交付事实。</summary>
        public decimal Qty { get; set; }
        /// <summary>每包数量；未知为空，已知时须大于零。</summary>
        public decimal? PcsPerPackage { get; set; }
        /// <summary>客户端计算的兼容展示值；服务端忽略此输入并以数量及每箱数量重算。</summary>
        public decimal? PackageCount { get; set; }
        /// <summary>每包净重。未知为空。</summary>
        public decimal? NwPerPackage { get; set; }
        /// <summary>每包毛重。未知为空。</summary>
        public decimal? GwPerPackage { get; set; }
        /// <summary>每包体积。未知为空。</summary>
        public decimal? CbmPerPackage { get; set; }
        /// <summary>客户采购编号文本快照，不作为关系主键。</summary>
        public string CustomerPoNo { get; set; }
        /// <summary>行上手工填写的发票号文本，不关联财务发票。</summary>
        public string InvoiceNo { get; set; }
        /// <summary>本行箱唛。</summary>
        public string Marks { get; set; }
        /// <summary>本行备注。</summary>
        public string Note { get; set; }


    }
}
