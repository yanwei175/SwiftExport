using System;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility
{
    /// <summary>已提交的主数据执行资格及其新版本；不携带主数据管理页面内容。</summary>
    public sealed class ExecutionEligibilityStatusResult
    {
        public ExecutionEligibilityKind Kind { get; set; }
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public bool IsActive { get; set; }
    }
}
