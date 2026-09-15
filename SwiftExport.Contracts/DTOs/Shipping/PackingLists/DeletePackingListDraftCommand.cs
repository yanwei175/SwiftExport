using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单草稿命令契约，不含库存过账行为；后端待实现。</summary>
    public class DeletePackingListDraftCommand
    {
        /// <summary>删除命令身份。</summary>
        public Guid CommandId { get; set; }
        /// <summary>整单期望版本。</summary>
        public int ExpectedVersion { get; set; }
    }
}
