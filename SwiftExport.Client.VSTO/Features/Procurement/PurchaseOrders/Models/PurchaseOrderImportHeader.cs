using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using System;
using System.ComponentModel;
using System.Linq;
namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models
{
    /// <summary>采购导入单头，仅承载内存编辑；身份通过后端检验接纳，特殊要求保存为 Remark。</summary>
    public sealed class PurchaseOrderImportHeader : ObservableObject, IDataErrorInfo
    {
        private string CustomerCodeValue = "";
        /// <summary>客户代码的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.CustomerCode", "客户代码")]
        public string CustomerCode { get => CustomerCodeValue; set => SetProperty(ref CustomerCodeValue, value); }
        private string BoNoValue = "";
        /// <summary>Excel采购单号的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.BoNo", "自定义采购单号")]
        public string BoNo { get => BoNoValue; set => SetProperty(ref BoNoValue, value); }
        private string CustomerPoNoValue = "";
        /// <summary>客户PO的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.CustomerPoNo", "客户PO号")]
        public string CustomerPoNo { get => CustomerPoNoValue; set => SetProperty(ref CustomerPoNoValue, value); }
        private string SalesPersonValue = "";
        /// <summary>业务员的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.SalesPerson", "业务员")]
        public string SalesPerson { get => SalesPersonValue; set => SetProperty(ref SalesPersonValue, value); }
        private string PurchasePersonValue = "";
        /// <summary>跟单员的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.PurchasePerson", "跟单员")]
        public string PurchasePerson { get => PurchasePersonValue; set => SetProperty(ref PurchasePersonValue, value); }
        private string InletHoseTypeValue = "";
        /// <summary>进水管的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.InletHoseType", "进水管")]
        public string InletHoseType { get => InletHoseTypeValue; set => SetProperty(ref InletHoseTypeValue, value); }
        private string AeratorTypeValue = "";
        /// <summary>起泡器的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.AeratorType", "起泡器")]
        public string AeratorType { get => AeratorTypeValue; set => SetProperty(ref AeratorTypeValue, value); }
        private string PalletTypeValue = "";
        /// <summary>托盘的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.PalletType", "托盘")]
        public string PalletType { get => PalletTypeValue; set => SetProperty(ref PalletTypeValue, value); }
        private string BoxTypeValue = "";
        /// <summary>内盒的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.BoxType", "内盒")]
        public string BoxType { get => BoxTypeValue; set => SetProperty(ref BoxTypeValue, value); }
        private string LabelTypeValue = "";
        /// <summary>标贴的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.LabelType", "标贴")]
        public string LabelType { get => LabelTypeValue; set => SetProperty(ref LabelTypeValue, value); }
        private string InstructionManualTypeValue = "";
        /// <summary>说明书的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.InstructionManualType", "说明书")]
        public string InstructionManualType { get => InstructionManualTypeValue; set => SetProperty(ref InstructionManualTypeValue, value); }
        private string CartonTypeValue = "";
        /// <summary>外箱的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.CartonType", "外箱")]
        public string CartonType { get => CartonTypeValue; set => SetProperty(ref CartonTypeValue, value); }
        private string LogoTypeValue = "";
        /// <summary>商标的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.LogoType", "商标")]
        public string LogoType { get => LogoTypeValue; set => SetProperty(ref LogoTypeValue, value); }
        private string RemarkValue = "";
        /// <summary>特殊要求的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.Remark", "特殊要求")]
        public string Remark { get => RemarkValue; set => SetProperty(ref RemarkValue, value); }
        private string DiscountRemarkValue = "";
        /// <summary>优惠备注的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.DiscountRemark", "优惠备注")]
        public string DiscountRemark { get => DiscountRemarkValue; set => SetProperty(ref DiscountRemarkValue, value); }
        private string DiscountRateTextValue = "";
        /// <summary>优惠倍率的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.DiscountRate", "优惠倍率")]
        public string DiscountRateText { get => DiscountRateTextValue; set => SetProperty(ref DiscountRateTextValue, value); }
        private string DiscountAmountTextValue = "";
        /// <summary>整单优惠金额的编辑值，保留原始输入。</summary>
        [ExcelImportField("Header.DiscountAmount", "整单优惠金额")]
        public string DiscountAmountText { get => DiscountAmountTextValue; set => SetProperty(ref DiscountAmountTextValue, value); }

        private DateTime? orderDate = DateTime.Today, replyDate, deliveryDate;
        /// <summary>订单日期；解析时缺失保持为空。</summary>
        [ExcelImportField("Header.OrderDate", "订单日期")]
        public DateTime? OrderDate { get => orderDate; set => SetProperty(ref orderDate, value); }
        /// <summary>供应商回复日期。</summary>
        [ExcelImportField("Header.ReplyDate", "回复交期")]
        public DateTime? ReplyDate { get => replyDate; set => SetProperty(ref replyDate, value); }

        /// <summary>新手工行采用此日期；之后不自动覆盖行交期。</summary>
        [ExcelImportField("Header.DeliveryDate", "预计交期")]
        public DateTime? DeliveryDate { get => deliveryDate; set => SetProperty(ref deliveryDate, value); }

        /// <summary>记录性倍率，空值不伪造为零。</summary>
        public decimal? DiscountRate => PurchaseImportNumbers.Parse(DiscountRateText);
        /// <summary>记录性金额调整，允许负数，不参与行汇总。</summary>
        public decimal? DiscountAmount => PurchaseImportNumbers.Parse(DiscountAmountText);
        /// <summary>本地输入反馈，不替代后端采购资格校验。</summary>
        public string Error => string.Join("；", new[] {
            string.IsNullOrWhiteSpace(CustomerCode) ? "请填写客户代码。" : "",
            !OrderDate.HasValue ? "请填写订单日期。" : "",
            this[nameof(DiscountRateText)], this[nameof(DiscountAmountText)] }.Where(s => s.Length > 0));
        /// <summary>可空优惠数值的软校验，非法文字仍保留。</summary>
        public string this[string property] => property == nameof(DiscountRateText)
            ? PurchaseImportNumbers.Error(DiscountRateText, "优惠倍率", false, true, 6, 999.999999m)
            : property == nameof(DiscountAmountText)
            ? PurchaseImportNumbers.Error(DiscountAmountText, "整单优惠金额", false, false, 4, 99999999999999.9999m) : "";
        /// <summary>读取缺失日期时不采用今天，要求人工补齐。</summary>
        public static PurchaseOrderImportHeader CreateForImport() => new PurchaseOrderImportHeader { OrderDate = null };
        /// <summary>输入变化通知校验；不在模型中请求网络。</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);
            if (args.PropertyName != nameof(Error)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Error)));
        }
    }
}

