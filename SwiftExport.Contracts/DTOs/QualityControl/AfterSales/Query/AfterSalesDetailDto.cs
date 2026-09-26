using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后详情聚合，包含单头、有效行及操作能力。</summary>
    public class AfterSalesDetailDto
    {

    public AfterSalesHeaderDto Header { get; set; } = new AfterSalesHeaderDto();
    public List<AfterSalesLineDto> Lines { get; set; } = new List<AfterSalesLineDto>();
    public AfterSalesCapabilitiesDto Capabilities { get; set; } = new AfterSalesCapabilitiesDto();
    }
}

