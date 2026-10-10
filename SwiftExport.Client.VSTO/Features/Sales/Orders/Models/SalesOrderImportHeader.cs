using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using System;
using System.ComponentModel;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>导入预览单头的独立编辑副本，不修改缓存或维护服务器单据状态。</summary>
    public sealed partial class SalesOrderImportHeader : ObservableObject, IDataErrorInfo
    {
        private Guid customerId = Guid.Empty;
        public Guid CustomerId { get => customerId; set => SetProperty(ref customerId, value); }
        private string customerCode = string.Empty;
        public string CustomerCode { get => customerCode; set => SetProperty(ref customerCode, value); }
        private string orderNo = string.Empty;
        public string OrderNo { get => orderNo; set => SetProperty(ref orderNo, value); }
        private string shipToName = string.Empty;
        [ExcelImportField("Header.ShipToName", "ShipToName", DisplayOrder = 10)]
        public string ShipToName { get => shipToName; set => SetProperty(ref shipToName, value); }
        private string shipToAddress = string.Empty;
        [ExcelImportField("Header.ShipToAddress", "ShipToAddress", DisplayOrder = 20)]
        public string ShipToAddress { get => shipToAddress; set => SetProperty(ref shipToAddress, value); }
        private string shipToPhone = string.Empty;
        [ExcelImportField("Header.ShipToPhone", "ShipToPhone", DisplayOrder = 30)]
        public string ShipToPhone { get => shipToPhone; set => SetProperty(ref shipToPhone, value); }

        private string incotermSortRemark = string.Empty;
        [ExcelImportField("Header.IncotermSortRemark", "Trims", DisplayOrder = 40)]
        public string IncotermSortRemark { get => incotermSortRemark; set => SetProperty(ref incotermSortRemark, value); }

        private string customerPoNo = string.Empty;
        [ExcelImportField("Header.CustomerPoNo", "CustomerPoNo", DisplayOrder = 50)]
        public string CustomerPoNo { get => customerPoNo; set => SetProperty(ref customerPoNo, value); }

        private string invoiceNo = string.Empty;
        [ExcelImportField("Header.InvoiceNo", "InvoiceNo", DisplayOrder = 60)]
        public string InvoiceNo { get => invoiceNo; set => SetProperty(ref invoiceNo, value); }

        private DateTime? orderDate = DateTime.Today;
        [ExcelImportField("Header.OrderDate", "OrderDate", DisplayOrder = 70)]
        public DateTime? OrderDate { get => orderDate; set => SetProperty(ref orderDate, value); }

        private DateTime? invoiceUpdateDate;
        /// <summary>Excel 发票更新日期快照，可读取或手工填写，不参与业务计算。</summary>
        [ExcelImportField("Header.InvoiceUpdateDate", "发票更新日期", DisplayOrder = 75)]
        public DateTime? InvoiceUpdateDate { get => invoiceUpdateDate; set => SetProperty(ref invoiceUpdateDate, value); }

        private DateTime? etd = null;
        [ExcelImportField("Header.Etd", "ETD", DisplayOrder = 80)]
        public DateTime? Etd { get => etd; set => SetProperty(ref etd, value); }
        private DateTime? eta = null;
        [ExcelImportField("Header.Eta", "ETA", DisplayOrder = 90)]
        public DateTime? Eta { get => eta; set => SetProperty(ref eta, value); }
        private string shippedBy = string.Empty;
        [ExcelImportField("Header.ShippedBy", "ShippedBy", DisplayOrder = 91)]
        public string ShippedBy { get => shippedBy; set { if (value != null) SetProperty(ref shippedBy, value); } }

        private string remark = string.Empty;
        [ExcelImportField("Header.Remark", "Remark", DisplayOrder = 130)]
        public string Remark { get => remark; set => SetProperty(ref remark, value); }

        private string currency = "USD";
        public string Currency { get => currency; set { if (value != null) SetProperty(ref currency, value); } }

        private string exchangeRateText = "";
        /// <summary>仅保留旧内存调用的文本属性；不再解析、导入或用于销售检查及保存。</summary>
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

        private string incotermLocation = string.Empty;
        public string IncotermLocation { get => incotermLocation; set => SetProperty(ref incotermLocation, value); }



        private DateTime? deliveryDate = null;
        public DateTime? DeliveryDate { get => deliveryDate; set => SetProperty(ref deliveryDate, value); }
        private DateTime? replyDate = null;
        public DateTime? ReplyDate { get => replyDate; set => SetProperty(ref replyDate, value); }

        /// <summary>废弃汇率不再解析；新导入始终不提供汇率，不以默认值伪装有效输入。</summary>
        public decimal? ExchangeRate => null;
        /// <summary>提供单头必填和输入错误汇总。</summary>
        public string Error => Source.Error + (CustomerId == Guid.Empty ? "请选择客户。" : "")
            + (OrderDate.HasValue ? "" : "订单日期不能为空。");
        /// <summary>保留数据错误接口兼容；废弃汇率文本不产生字段错误。</summary>
        public string this[string name] => string.Empty;
    }
}
