using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Inventory
{
    /// <summary>按期望版本和原因请求停用仓库。</summary>
    public class DeactivateWarehouseSiteCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }
}
