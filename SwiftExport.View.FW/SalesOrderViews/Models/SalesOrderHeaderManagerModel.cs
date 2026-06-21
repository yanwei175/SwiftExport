using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    public class SalesOrderHeaderManagerModel : BaseLineModel
    {
        public bool IsRmbCurrency => Currency == "CNY" || Currency == "RMB"; //提供一个给UI判断是否显示某些字段的属性


        #region 基本信息

        private SalesOrderStatusEnum _orderStatus;
        /// <summary>
        /// OrderStatus（未发货/部分发货/已发货）
        /// </summary>
        public SalesOrderStatusEnum OrderStatus
        {
            get => _orderStatus;
            set
            {
                if (SetProperty(ref _orderStatus, value))
                {
                    RaisePropertyChanged(nameof(OrderStatusText));
                }
            }
        }
        public string OrderStatusText => OrderStatus.ToString();

        private ConfirmationStatusEnum _confirmationStatus;
        public ConfirmationStatusEnum ConfirmationStatus
        {
            get => _confirmationStatus;
            set
            {
                if (SetProperty(ref _confirmationStatus, value))
                {
                    RaisePropertyChanged(nameof(ConfirmationStatusText));
                }
            }
        }
        public string ConfirmationStatusText => ConfirmationStatus.ToString();

        private string _orderNo;
        public string OrderNo
        {
            get => _orderNo;
            set => SetProperty(ref _orderNo, value);
        }

        private string _invoiceNo;
        /// <summary>发票号（如有）</summary>
        public string InvoiceNo
        {
            get => _invoiceNo;
            set => SetProperty(ref _invoiceNo, value);
        }

        private Guid _customerId;
        public Guid CustomerId
        {
            get => _customerId;
            set => SetProperty(ref _customerId, value);
        }

        private string _customerCode;
        public string CustomerCode
        {
            get => _customerCode;
            set => SetProperty(ref _customerCode, value);
        }

        private string _customerPoNo;
        public string CustomerPoNo
        {
            get => _customerPoNo;
            set => SetProperty(ref _customerPoNo, value);
        }

        #endregion

        #region 数量字段
        private decimal? _totalQty;
        public decimal? TotalQty
        {
            get => _totalQty;
            set => SetProperty(ref _totalQty, value);
        }
        private decimal? _totalShippedQty;
        public decimal? TotalShippedQty
        {
            get => _totalShippedQty;
            set => SetProperty(ref _totalShippedQty, value);
        }

        private decimal? _totalUnshippedQty;
        public decimal? TotalUnshippedQty
        {
            get => _totalUnshippedQty;
            set => SetProperty(ref _totalUnshippedQty, value);
        }



        #endregion

        #region 金额与汇率

        private string _currency;
        /// <summary>币种（USD / RMB / EUR 等）</summary>
        public string Currency
        {
            get => _currency;
            set
            {
                if (SetProperty(ref _currency, value))
                {
                    RaisePropertyChanged(nameof(IsRmbCurrency));
                }
            }
        }

        private decimal _exchangeRate;
        /// <summary>ExchangeRate（用于本币金额计算）</summary>
        public decimal ExchangeRate
        {
            get => _exchangeRate;
            set => SetProperty(ref _exchangeRate, value);
        }

        private decimal _totalAmountOriginalCurrency;
        /// <summary>原币总金额（由行集合自动计算）</summary>
        public decimal TotalAmountOriginalCurrency
        {
            get => _totalAmountOriginalCurrency;
            set => SetProperty(ref _totalAmountOriginalCurrency, value);
        }

        private decimal _totalAmountLocalCurrency;
        /// <summary>本币总金额（原币金额 * ExchangeRate）</summary>
        public decimal TotalAmountLocalCurrency
        {
            get => _totalAmountLocalCurrency;
            set => SetProperty(ref _totalAmountLocalCurrency, value);
        }


        #endregion

        #region 日期字段

        private DateTime? _orderDate;
        /// <summary>
        /// 订单日期
        /// </summary>
        public DateTime? OrderDate
        {
            get => _orderDate;
            set => SetProperty(ref _orderDate, value);
        }

        private DateTime? _deliveryDate;
        /// <summary>
        /// 交期（预计发货日期）
        /// </summary>
        public DateTime? DeliveryDate
        {
            get => _deliveryDate;
            set => SetProperty(ref _deliveryDate, value);
        }

        private DateTime? _replyDate;
        /// <summary>
        /// 回复客户的日期（如确认交期）
        /// </summary>
        public DateTime? ReplyDate
        {
            get => _replyDate;
            set => SetProperty(ref _replyDate, value);
        }


        #endregion

        #region 备注

        private string _soFolder;
        /// <summary>
        /// 订单文件夹路径（如存放附件）
        /// </summary>
        public string SoFolder
        {
            get => _soFolder;
            set => SetProperty(ref _soFolder, value);
        }

        private string _remark;
        /// <summary>
        /// SubNote
        /// </summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        #endregion

        #region 验证

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        public override string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(OrderNo):
                        if (string.IsNullOrWhiteSpace(OrderNo))
                            return "订单编号不能为空";
                        break;
                    case nameof(CustomerCode):
                        if (string.IsNullOrWhiteSpace(CustomerCode))
                            return "客户不能为空";
                        break;
                    case nameof(InvoiceNo):
                        if (string.IsNullOrWhiteSpace(InvoiceNo))
                            return "发票号不能为空";
                        break;

                }
                return null;
            }
        }

        #endregion
    }
}
