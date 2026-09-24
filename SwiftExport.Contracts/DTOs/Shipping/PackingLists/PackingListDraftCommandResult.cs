using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单草稿命令契约，不含库存过账行为；后端待实现。</summary>
    public class PackingListDraftCommandResult
    {
        /// <summary>命令处理的单据身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>同命令已成功应用。</summary>
        public bool AlreadyApplied { get; set; }
        /// <summary>保存成功的最新详情；删除成功为空。</summary>
        public PackingListDetailDto Detail { get; set; }
    }
}
