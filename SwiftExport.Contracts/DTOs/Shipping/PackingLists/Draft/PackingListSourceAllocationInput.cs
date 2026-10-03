using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Shipping.PackingLists
{
    /// <summary>一条显式来源数量分配，版本在事务内复核。</summary>
    public sealed class PackingListSourceAllocationInput
    {
        public Guid GrossDemandId { get; set; }
        public int ExpectedDemandVersion { get; set; }
        public int? ExpectedSourceVersion { get; set; }
        public decimal Qty { get; set; }
    }
}
