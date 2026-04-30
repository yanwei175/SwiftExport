using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Orders
{
    public class PackingListDto : BaseDto
    {
        public string PackingListNo { get; set; }
        public string PoNo { get; set; }
        public string PurchaseOrderNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; }
        public DateTime? PackingDate { get; set; }
        public string Status { get; set; }
        public bool IsPosted { get; set; }
        public DateTime? PostedDate { get; set; }
        public string PostedBy { get; set; }
        public string ReceiveNo { get; set; }
        public string Remark { get; set; }
        public decimal TotalQty { get; set; }
        public int LineCount { get; set; }
    }

    public class PackingListLineDto : BaseDto
    {
        public string PackingListNo { get; set; }
        public int? PackingListHeadId { get; set; }
        public string Sku { get; set; }
        public string CustomerSku { get; set; }
        public string Description { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public int? PoLineId { get; set; }
        public long? SoLineId { get; set; }
        public string PoNo { get; set; }
        public string PurchaseOrderNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string Remark { get; set; }
    }

    public class PackingListDetailDto
    {
        public PackingListDto Header { get; set; }
        public List<PackingListLineDto> Lines { get; set; } = new List<PackingListLineDto>();
    }

    public class PackingListSearchDto
    {
        public string Keyword { get; set; }
        public string PackingListNo { get; set; }
        public string SupplierCode { get; set; }
        public string PoNo { get; set; }
        public string SalesOrderNo { get; set; }
        public bool? IsPosted { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class PackingListPostRequestDto
    {
        public int PackingListId { get; set; }
        public bool AllowNegativeStock { get; set; }
        public string Operator { get; set; }
    }

    public class PackingListPostResultDto
    {
        public string PackingListNo { get; set; }
        public string ReceiveNo { get; set; }
        public bool NeedNegativeStockConfirm { get; set; }
        public string WarningMessage { get; set; }
        public List<PackingListStockShortageDto> Shortages { get; set; } = new List<PackingListStockShortageDto>();
    }

    public class PackingListStockShortageDto
    {
        public string Sku { get; set; }
        public string SupplierCode { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal OnhandQty { get; set; }
        public decimal ShortageQty { get; set; }
    }
}
