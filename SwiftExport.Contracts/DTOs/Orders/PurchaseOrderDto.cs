using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购订单头 DTO。
    /// </summary>
    public class PurchaseOrderDto : BaseDto
    {
        public string SoPerson { get; set; }
        public string PoPerson { get; set; }
        public string Even { get; set; }
        public string Status { get; set; }

        public string OrderStatus { get; set; }
        public string PoNo { get; set; }
        public string SalesOrderNo { get; set; }
        public string CustomerPoNo { get; set; }
        public string CustomerCode { get; set; }
        public string Currency { get; set; }
        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;

        public string InletrHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string IMType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }

        public string ReviewedBy { get; set; }
        public string ApprovalStatus { get; set; }
        public string ApprovedBy { get; set; }
        public string PoFolder { get; set; }
        public string ZheKouBeiZhu { get; set; }
        public string Remark { get; set; }

        public bool IsDeleted { get; set; }
        public bool? IsLocked { get; set; }
        public int? LineCount { get; set; }
        public decimal? TotalAmountLocalCurrency { get; set; }
        public decimal? TotalQty { get; set; }
        public decimal? TotalShippedQty { get; set; }
        public decimal? TotalUnshippedQty { get; set; }
        public decimal? TotalPartiallyShippedQty { get; set; }

        public DateTime? OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public DateTime? ReplyDate { get; set; }
        public DateTime? ChangeTime { get; set; }

        public decimal? ZhiJieYouHuiJinE { get; set; }
        public decimal? ZheKou { get; set; }
        public string OrderNo { get; set; } = string.Empty;
    }
}
