using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单草稿命令契约，不含库存过账行为；后端待实现。</summary>
    public class UpdatePackingListDraftCommand
    {
        /// <summary>同意图重试保持不变。</summary>
        public Guid CommandId { get; set; }
        /// <summary>整单期望版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>可写文档字段。</summary>
        public PackingListDraftHeaderInput Header { get; set; }
        /// <summary>全部有效行快照，遗漏不代表删除。</summary>
        public List<PackingListDraftLineInput> Lines { get; set; } = new List<PackingListDraftLineInput>();
        /// <summary>显式删除身份与版本。</summary>
        public List<PackingListDeletedLineInput> DeletedLines { get; set; } = new List<PackingListDeletedLineInput>();
    }
}
