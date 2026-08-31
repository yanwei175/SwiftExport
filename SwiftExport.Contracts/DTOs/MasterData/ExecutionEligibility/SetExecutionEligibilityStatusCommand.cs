using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility
{
    /// <summary>明确启用或停用一种主数据的新业务资格，不修改其资料或已有业务单据。</summary>
    public sealed class SetExecutionEligibilityStatusCommand
    {
        public ExecutionEligibilityKind Kind { get; set; }
        public Guid Uid { get; set; }
        public int ExpectedVersion { get; set; }
        public bool IsActive { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
