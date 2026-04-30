using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.PurchaseOrderViews.Models
{
    /// <summary>
    /// 采购订单头 UI 模型。
    /// 中文注释：该模型用于采购工作台主表展示与行选择，不做复杂校验。
    /// </summary>
    public class PurchaseOrderHeadModel : ViewModelBaseEx
    {
        private int _id;
        public int Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        private string _poNo;
        public string PoNo
        {
            get { return _poNo; }
            set { SetProperty(ref _poNo, value); }
        }

        private string _orderNo;
        public string OrderNo
        {
            get { return _orderNo; }
            set { SetProperty(ref _orderNo, value); }
        }

        private string _supplierCode;
        public string SupplierCode
        {
            get { return _supplierCode; }
            set { SetProperty(ref _supplierCode, value); }
        }

        private string _supplierName;
        public string SupplierName
        {
            get { return _supplierName; }
            set { SetProperty(ref _supplierName, value); }
        }

        private string _salesOrderNo;
        public string SalesOrderNo
        {
            get { return _salesOrderNo; }
            set { SetProperty(ref _salesOrderNo, value); }
        }

        private string _approvalStatus;
        public string ApprovalStatus
        {
            get { return _approvalStatus; }
            set { SetProperty(ref _approvalStatus, value); }
        }

        private string _status;
        public string Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }

        private DateTime? _orderDate;
        public DateTime? OrderDate
        {
            get { return _orderDate; }
            set { SetProperty(ref _orderDate, value); }
        }

        private decimal? _totalQty;
        public decimal? TotalQty
        {
            get { return _totalQty; }
            set { SetProperty(ref _totalQty, value); }
        }

        private decimal? _totalAmountLocalCurrency;
        public decimal? TotalAmountLocalCurrency
        {
            get { return _totalAmountLocalCurrency; }
            set { SetProperty(ref _totalAmountLocalCurrency, value); }
        }

        /// <summary>
        /// 中文注释：用于主表多选勾选。
        /// </summary>
        private bool _isChecked;
        public bool IsChecked
        {
            get { return _isChecked; }
            set { SetProperty(ref _isChecked, value); }
        }

        /// <summary>
        /// 中文注释：DTO 转 UI 模型。
        /// </summary>
        public static PurchaseOrderHeadModel FromDto(PurchaseOrderDto dto)
        {
            if (dto == null) return null;

            return new PurchaseOrderHeadModel
            {
                Id = dto.Id,
                PoNo = dto.PoNo,
                OrderNo = dto.OrderNo,
                SupplierCode = dto.SupplierCode,
                SupplierName = dto.SupplierName,
                SalesOrderNo = dto.SalesOrderNo,
                ApprovalStatus = dto.ApprovalStatus,
                Status = dto.Status,
                OrderDate = dto.OrderDate,
                TotalQty = dto.TotalQty,
                TotalAmountLocalCurrency = dto.TotalAmountLocalCurrency
            };
        }

        /// <summary>
        /// 中文注释：UI 模型回写 DTO（保存明细时复用头信息）。
        /// </summary>
        public PurchaseOrderDto ToDto()
        {
            return new PurchaseOrderDto
            {
                Id = Id,
                PoNo = PoNo,
                OrderNo = OrderNo,
                SupplierCode = SupplierCode,
                SupplierName = SupplierName,
                SalesOrderNo = SalesOrderNo,
                ApprovalStatus = ApprovalStatus,
                Status = Status,
                OrderDate = OrderDate,
                TotalQty = TotalQty,
                TotalAmountLocalCurrency = TotalAmountLocalCurrency
            };
        }
    }
}
