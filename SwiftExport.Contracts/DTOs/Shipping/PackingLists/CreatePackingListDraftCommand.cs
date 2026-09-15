using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单草稿命令契约，不含库存过账行为；后端待实现。</summary>
    public class CreatePackingListDraftCommand
    {
        /// <summary>同意图重试保持不变。</summary>
        public Guid CommandId { get; set; }
        /// <summary>客户端生成的单据身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>可写文档字段。</summary>
        public PackingListDraftHeaderInput Header { get; set; }
        /// <summary>全部有效草稿行。</summary>
        public List<PackingListDraftLineInput> Lines { get; set; } = new List<PackingListDraftLineInput>();
    }
}
