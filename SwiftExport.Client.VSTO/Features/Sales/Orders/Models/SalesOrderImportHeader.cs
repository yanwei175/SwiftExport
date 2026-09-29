using System;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>导入预览单头的独立编辑副本，不修改缓存或维护服务器单据状态。</summary>
    public sealed class SalesOrderImportHeader : ObservableObject, IDataErrorInfo
    {
        private Guid customerId = Guid.Empty;
        public Guid CustomerId { get => customerId; set => SetProperty(ref customerId, value); }
        private string customerCode = string.Empty;
        public string CustomerCode { get => customerCode; set => SetProperty(ref customerCode, value); }
        private string orderNo = string.Empty;
        public string OrderNo { get => orderNo; set => SetProperty(ref orderNo, value); }
        private string invoiceNo = string.Empty;
        public string InvoiceNo { get => invoiceNo; set => SetProperty(ref invoiceNo, value); }
        private string customerPoNo = string.Empty;
        public string CustomerPoNo { get => customerPoNo; set => SetProperty(ref customerPoNo, value); }
        private string currency = "USD";
        public string Currency { get => currency; set { if (value != null) SetProperty(ref currency, value); } }
        private string exchangeRateText = "1";
        public string ExchangeRateText { get => exchangeRateText; set => SetProperty(ref exchangeRateText, value); }
        private decimal depositRate;
        public decimal DepositRate { get => depositRate; set => SetProperty(ref depositRate, value); }
        private int leadTimeDays;
        public int LeadTimeDays { get => leadTimeDays; set => SetProperty(ref leadTimeDays, value); }
        // 下拉重建时 WPF 可能短暂回写 null；只有显式空字符串选项表示清除。
        private string paymentMethod = string.Empty;
        public string PaymentMethod { get => paymentMethod; set { if (value != null) SetProperty(ref paymentMethod, value); } }
        private string incoterm = string.Empty;
        public string Incoterm { get => incoterm; set { if (value != null) SetProperty(ref incoterm, value); } }
        private string incotermSortRemark = string.Empty;
        public string IncotermSortRemark { get => incotermSortRemark; set => SetProperty(ref incotermSortRemark, value); }
        private string incotermLocation = string.Empty;
        public string IncotermLocation { get => incotermLocation; set => SetProperty(ref incotermLocation, value); }
        private string shippedBy = string.Empty;
        public string ShippedBy { get => shippedBy; set { if (value != null) SetProperty(ref shippedBy, value); } }
        private string shipToName = string.Empty;
        public string ShipToName { get => shipToName; set => SetProperty(ref shipToName, value); }
        private string shipToAddress = string.Empty;
        public string ShipToAddress { get => shipToAddress; set => SetProperty(ref shipToAddress, value); }
        private string shipToPhone = string.Empty;
        public string ShipToPhone { get => shipToPhone; set => SetProperty(ref shipToPhone, value); }
        private string remark = string.Empty;
        public string Remark { get => remark; set => SetProperty(ref remark, value); }
        private DateTime? orderDate = DateTime.Today;
        public DateTime? OrderDate { get => orderDate; set => SetProperty(ref orderDate, value); }
        private DateTime? deliveryDate = null;
        public DateTime? DeliveryDate { get => deliveryDate; set => SetProperty(ref deliveryDate, value); }
        private DateTime? replyDate = null;
        public DateTime? ReplyDate { get => replyDate; set => SetProperty(ref replyDate, value); }
        private DateTime? etd = null;
        public DateTime? Etd { get => etd; set => SetProperty(ref etd, value); }
        private DateTime? eta = null;
        public DateTime? Eta { get => eta; set => SetProperty(ref eta, value); }
        /// <summary>当前输入的汇率；无效文本不转换为零。</summary>
        public decimal? ExchangeRate => decimal.TryParse(ExchangeRateText, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && value > 0 ? value : (decimal?)null;
        /// <summary>提供单头必填和输入错误汇总。</summary>
        public string Error => CustomerId == Guid.Empty ? "请选择客户。" : this[nameof(ExchangeRateText)];
        /// <summary>供 WPF 输入框显示汇率格式错误。</summary>
        public string this[string name] => name == nameof(ExchangeRateText) && !ExchangeRate.HasValue ? "汇率必须是大于零的数字。" : string.Empty;
    }
}
