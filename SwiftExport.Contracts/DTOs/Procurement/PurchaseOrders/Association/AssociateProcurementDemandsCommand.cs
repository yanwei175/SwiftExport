using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Procurement.OrderManagement
{
    /// <summary>冻结后的追加关联命令；未知结果必须保持全部输入及命令身份重试。</summary>
    public sealed class AssociateProcurementDemandsCommand
    {
        public Guid CommandId { get; set; }
        public int ExpectedVersion { get; set; }
        public string PreviewToken { get; set; }
        public string Reason { get; set; }
        public List<ProcurementDemandAssociationItem> Items { get; set; } = new List<ProcurementDemandAssociationItem>();
    }
}
