using SwiftExport.Contracts.SysEnums.StaticEnums;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Orders.RequirementPools
{
    public class DecomposeRequestDto
    {
        public string SourceType { get; set; } = string.Empty; // 如 "销售订单"
        public string SourceNo { get; set; } = string.Empty;    // 如 "SO20260429001"
        public List<DecomposeLineRequestDto> Lines { get; set; } = new List<DecomposeLineRequestDto>();
    }
}
