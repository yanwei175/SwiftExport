using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>装箱单专用数据契约；后端待实现。</summary>
    public class PackingListDeletedLineInput
    {
        /// <summary>显式删除行身份。</summary>
        public Guid Uid { get; set; }
        /// <summary>删除行期望版本。</summary>
        public int ExpectedVersion { get; set; }
    }
}
