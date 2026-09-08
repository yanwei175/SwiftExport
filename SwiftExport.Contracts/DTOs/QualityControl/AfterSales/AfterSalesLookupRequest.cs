using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后候选的关键词、客户上下文与分页条件。</summary>
    public class AfterSalesLookupRequest
    {
    /// <summary>候选关键词。</summary>
    public string Keyword { get; set; } = string.Empty;
    /// <summary>客户上下文。</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>页码。</summary>
    public int PageIndex { get; set; }
    /// <summary>页大小。</summary>
    public int PageSize { get; set; }

    }
}

