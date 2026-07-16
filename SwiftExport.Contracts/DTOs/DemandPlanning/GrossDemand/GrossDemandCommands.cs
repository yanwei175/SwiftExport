using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand
{
    public class GrossDemandCommandItem
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public int ExpectedDecompositionRevision { get; set; }
        public string ClientKey { get; set; }
        public int RowIndex { get; set; }
    }

    public class GrossDemandDecomposeCommand
    {
        public List<GrossDemandCommandItem> Items { get; set; } = new List<GrossDemandCommandItem>();
    }

    public class GrossDemandDecomposeResult
    {
        public List<GrossDemandDecomposeItemResult> Items { get; set; } = new List<GrossDemandDecomposeItemResult>();
    }

    public class GrossDemandDecomposeItemResult
    {
        public Guid Uid { get; set; }
        public string ClientKey { get; set; }
        public int RowIndex { get; set; }
        public GrossDemandCommandOutcomeEnum Outcome { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public int OldVersion { get; set; }
        public int NewVersion { get; set; }
        public int DecompositionRevision { get; set; }
        public int CreatedNetDemandCount { get; set; }
    }

    public class GrossDemandRollbackCommand
    {
        public List<GrossDemandCommandItem> Items { get; set; } = new List<GrossDemandCommandItem>();
        public string Reason { get; set; }
    }

    public class GrossDemandRollbackResult
    {
        public List<GrossDemandRollbackItemResult> Items { get; set; } = new List<GrossDemandRollbackItemResult>();
    }

    public class GrossDemandRollbackItemResult
    {
        public Guid Uid { get; set; }
        public string ClientKey { get; set; }
        public int RowIndex { get; set; }
        public GrossDemandCommandOutcomeEnum Outcome { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public int OldVersion { get; set; }
        public int NewVersion { get; set; }
        public int DecompositionRevision { get; set; }
        public List<GrossDemandRollbackBlockerDto> Blockers { get; set; } = new List<GrossDemandRollbackBlockerDto>();
    }

    public class GrossDemandRollbackBlockerDto
    {
        public Guid NetDemandId { get; set; }
        public string TargetType { get; set; }
        public string TargetNo { get; set; }
        public Guid TargetLineId { get; set; }
        public DemandExecutionLinkStatusEnum LinkStatus { get; set; }
        public string Message { get; set; }
    }
}
