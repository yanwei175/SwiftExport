using System;
using System.Collections.Generic;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Contracts.DTOs.QualityControl.AfterSales
{
    /// <summary>售后管理列表的客户、责任方、状态、日期和型号筛选条件。</summary>
    public class AfterSalesSearchRequest
    {
    /// <summary>单号和型号关键词。</summary>
    public string Keyword { get; set; } = string.Empty;
    /// <summary>客户筛选。</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>责任方筛选。</summary>
    public Guid? ResponsibleSupplierId { get; set; }
    /// <summary>生命周期筛选。</summary>
    public DocumentStatusEnum? DocumentStatus { get; set; }
    /// <summary>确认筛选。</summary>
    public ConfirmationStatusEnum? ConfirmationStatus { get; set; }
    /// <summary>受理起始日期。</summary>
    public DateTime? DateFrom { get; set; }
    /// <summary>受理结束日期。</summary>
    public DateTime? DateTo { get; set; }
    /// <summary>页码，从1开始。</summary>
    public int PageIndex { get; set; }
    /// <summary>分页大小。</summary>
    public int PageSize { get; set; }

    }
}

