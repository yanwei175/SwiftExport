using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using System;
namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models
{
    /// <summary>装箱单单头内存编辑模型；系统身份不进入配置目录。</summary>
    public sealed class PackingListImportHeader : ObservableObject
    {
        private string customerName = "";
        /// <summary>收货名称的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Header.CustomerName", "收货名称", DisplayOrder = 10)]
        public string CustomerName { get => customerName; set => SetProperty(ref customerName, value); }
        private string customerAddress = "";
        /// <summary>收货地址的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Header.CustomerAddress", "收货地址", DisplayOrder = 20)]
        public string CustomerAddress { get => customerAddress; set => SetProperty(ref customerAddress, value); }
        private string attention = "";
        /// <summary>联系人的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Header.Attention", "联系人", DisplayOrder = 30)]
        public string Attention { get => attention; set => SetProperty(ref attention, value); }
        private string excelDocumentNo = "";
        /// <summary>单头发票号原文及其配置地址；仅留在前端，不进入草稿请求或替代后端明细发票号汇总。</summary>
        [ExcelImportField("Header.ExcelDocumentNo", "单头发票号", DisplayOrder = 40)]
        public string ExcelDocumentNo { get => excelDocumentNo; set => SetProperty(ref excelDocumentNo, value); }
        private DateTime? invoiceDate;
        /// <summary>单据日期，无法解析时为空，由用户补齐。</summary>
        [ExcelImportField("Header.InvoiceDate", "单据日期", DisplayOrder = 50)]
        public DateTime? InvoiceDate { get => invoiceDate; set => SetProperty(ref invoiceDate, value); }

        private string containerNo = "";
        /// <summary>集装箱号的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Header.ContainerNo", "集装箱号", DisplayOrder = 60)]
        public string ContainerNo { get => containerNo; set => SetProperty(ref containerNo, value); }

        private string sealNo = "";
        /// <summary>封条号的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Header.SealNo", "封条号", DisplayOrder = 70)]
        public string SealNo { get => sealNo; set => SetProperty(ref sealNo, value); }

        private DateTime? etd;
        /// <summary>预计离港，无法解析时为空，由用户补齐。</summary>
        [ExcelImportField("Header.Etd", "预计离港", DisplayOrder = 80)]
        public DateTime? Etd { get => etd; set => SetProperty(ref etd, value); }

        private DateTime? eta;
        /// <summary>预计到达，无法解析时为空，由用户补齐。</summary>
        [ExcelImportField("Header.Eta", "预计到达", DisplayOrder = 90)]
        public DateTime? Eta { get => eta; set => SetProperty(ref eta, value); }

        private string shippingLocation = "";
        /// <summary>出货地点的可编辑原文；修改会使检查失效。</summary>
        public string ShippingLocation { get => shippingLocation; set => SetProperty(ref shippingLocation, value); }

        private string note = "";
        /// <summary>备注的可编辑原文；修改会使检查失效。</summary>
        public string Note { get => note; set => SetProperty(ref note, value); }
    }
}
