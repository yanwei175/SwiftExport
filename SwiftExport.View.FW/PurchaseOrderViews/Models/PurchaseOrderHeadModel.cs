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

        private string _boNo;
        public string BoNo
        {
            get { return _boNo; }
            set { SetProperty(ref _boNo, value); }
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

        private int _approvalStatus;
        public int ApprovalStatus
        {
            get { return _approvalStatus; }
            set { SetProperty(ref _approvalStatus, value); }
        }

        private int _orderStatus;
        public int OrderStatus
        {
            get { return _orderStatus; }
            set { SetProperty(ref _orderStatus, value); }
        }

        private DateTime? _orderDate;
        public DateTime? OrderDate
        {
            get { return _orderDate; }
            set { SetProperty(ref _orderDate, value); }
        }

        private DateTime? _expectedDate;
        /// <summary>
        /// 预计交货日期
        /// </summary>
        public DateTime? ExpectedDate
        {
            get { return _expectedDate; }
            set { SetProperty(ref _expectedDate, value); }
        }
        private DateTime? _replyDate;
        /// <summary>
        /// 回复交货日期
        /// </summary>
        public DateTime? ReplyDate
        {
            get { return _replyDate; }
            set { SetProperty(ref _replyDate, value); }
        }

        private decimal? _totalQty;
        public decimal? TotalQty
        {
            get { return _totalQty; }
            set { SetProperty(ref _totalQty, value); }
        }

        private decimal? _totalShippedQty;
        public decimal? TotalShippedQty
        {
            get { return _totalShippedQty; }
            set { SetProperty(ref _totalShippedQty, value); }
        }
        private decimal? _totalUnshippedQty;
        public decimal? TotalUnshippedQty
        {
            get { return _totalUnshippedQty; }
            set { SetProperty(ref _totalUnshippedQty, value); }
        }

        private decimal? _totalReadyQty;
        public decimal? TotalReadyQty
        {
            get { return _totalReadyQty; }
            set { SetProperty(ref _totalReadyQty, value); }
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

    }
}
