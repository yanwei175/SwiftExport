using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>承载一条内存导入明细及其搜索编辑状态，不包含 CRUD 基线或持久化行为。</summary>
    public sealed partial class SalesOrderImportLine : ObservableObject, IDataErrorInfo
    {
        private string sku = "", customerSku = "", descriptionEn = "", colorNameEn = "", featuresEn = "", unitSnapshot = "", subNote = "", mainImage = "";
        private string qtyText = "1", usdPriceText = "", cnyPriceText = "";
        private Guid? productId, customerProductId;
        private DateTime? expectedDeliveryDate;
        private SalesOrderProductLookupDto selectedProduct;
        private bool isSearchBusy;

        /// <summary>创建稳定行身份，非标编号只生成一次。</summary>
        public SalesOrderImportLine(bool nonStandard, DateTime? deliveryDate)
        {
            isNonStandard = nonStandard;
            Source.PropertyChanged += (sender, args) => { InvalidateCheck(); OnPropertyChanged(nameof(Error)); };
            expectedDeliveryDate = deliveryDate;
            if (nonStandard) sku = NonStandardSkuGenerator.Create(Uid);
        }
        /// <summary>本地稳定行标识，检查请求作为 ClientLineId，保存作为明细 Uid。</summary>
        public Guid Uid { get; } = Guid.NewGuid();
        /// <summary>只能通过受控转换切换行类型，不能由字段配置直接写入。</summary>
        public bool IsNonStandard => isNonStandard;
        public string LineTypeText => IsNonStandard ? "非标" : "标准";
        public Guid? ProductId { get => productId; private set => SetProperty(ref productId, value); }
        public Guid? CustomerProductId { get => customerProductId; private set => SetProperty(ref customerProductId, value); }
        public string Sku { get => sku; private set => SetProperty(ref sku, value); }

        [ExcelImportField("Line.CustomerSku", "客户型号", DisplayOrder = 10)]
        public string CustomerSku { get => customerSku; set => SetProperty(ref customerSku, value); }

        [ExcelImportField("Line.DescriptionEn", "英文描述", DisplayOrder = 12)]
        public string DescriptionEn { get => descriptionEn; set => SetProperty(ref descriptionEn, value); }
        [ExcelImportField("Line.ColorNameEn", "英文颜色", DisplayOrder = 13)]
        public string ColorNameEn { get => colorNameEn; set => SetProperty(ref colorNameEn, value); }
        [ExcelImportField("Line.FeaturesEn", "英文特性", DisplayOrder = 14)]
        public string FeaturesEn { get => featuresEn; set => SetProperty(ref featuresEn, value); }

        [ExcelImportField("Line.UsdPrice", "美元单价", DisplayOrder = 15)]
        public string UsdPriceText { get => usdPriceText; set { if (SetProperty(ref usdPriceText, value)) NotifyAmounts(); } }
        [ExcelImportField("Line.CnyPrice", "人民币单价", DisplayOrder = 16)]
        public string CnyPriceText { get => cnyPriceText; set { if (SetProperty(ref cnyPriceText, value)) NotifyAmounts(); } }

        [ExcelImportField("Line.Qty", "数量", DisplayOrder = 17)]
        public string QtyText { get => qtyText; set { if (SetProperty(ref qtyText, value)) NotifyAmounts(); } }


        public string UnitSnapshot { get => unitSnapshot; set { if (value != null) SetProperty(ref unitSnapshot, value); } }
        [ExcelImportField("Line.SubNote", "行备注", DisplayOrder = 101)]
        public string SubNote { get => subNote; set => SetProperty(ref subNote, value); }
        public string MainImage { get => mainImage; private set => SetProperty(ref mainImage, value); }
        [ExcelImportField("Line.ExpectedDeliveryDate", "预计交货日期", DisplayOrder = 102)]
        public DateTime? ExpectedDeliveryDate { get => expectedDeliveryDate; set => SetProperty(ref expectedDeliveryDate, value); }

        public decimal? Qty => Parse(QtyText, true);
        public decimal? UsdPrice => Parse(UsdPriceText, false);
        public decimal? CnyPrice => Parse(CnyPriceText, false);
        public decimal? UsdAmount => Multiply(Qty, UsdPrice);
        public decimal? CnyAmount => Multiply(Qty, CnyPrice);
        public ObservableCollection<SalesOrderProductLookupDto> ProductCandidates { get; } = new ObservableCollection<SalesOrderProductLookupDto>();
        public SalesOrderProductLookupDto SelectedProduct { get => selectedProduct; set => SetProperty(ref selectedProduct, value); }
        public bool IsSearchBusy { get => isSearchBusy; set => SetProperty(ref isSearchBusy, value); }
        public IAsyncRelayCommand<string> SearchCommand { get; internal set; }
        public IRelayCommand CommitProductCommand { get; internal set; }
        public IRelayCommand ClearProductCommand { get; internal set; }

        /// <summary>仅检查本地输入完整性，不代替服务端保存校验。</summary>
        public string Error => Source.Error + PackagingError + CheckError + this[nameof(QtyText)] + this[nameof(UsdPriceText)] + this[nameof(CnyPriceText)]
            + (IsNonStandard ? (string.IsNullOrWhiteSpace(DescriptionEn) ? "非标行需要描述。" : "") + (string.IsNullOrWhiteSpace(UnitSnapshot) ? "非标行需要单位。" : "")
                : (!ProductId.HasValue ? "标准行需要选择产品。" : ""))
            + (Qty.HasValue && ((UsdPrice.HasValue && !UsdAmount.HasValue) || (CnyPrice.HasValue && !CnyAmount.HasValue)) ? "金额超过支持范围。" : "");
        /// <summary>保留非法输入文本并由 WPF 显示软校验。</summary>
        public string this[string name] => name == nameof(QtyText) && !Qty.HasValue ? "数量必须大于零。"
            : name == nameof(UsdPriceText) && !string.IsNullOrWhiteSpace(UsdPriceText) && !UsdPrice.HasValue ? "USD 单价必须为非负数字。"
            : name == nameof(CnyPriceText) && !string.IsNullOrWhiteSpace(CnyPriceText) && !CnyPrice.HasValue ? "CNY 单价必须为非负数字。"
            : name == nameof(PcsOfCartonText) ? PackagingIssue(PcsOfCartonText, true, "每箱数量")
            : name == nameof(NwOfCartonText) ? PackagingIssue(NwOfCartonText, false, "每箱净重")
            : name == nameof(GwOfCartonText) ? PackagingIssue(GwOfCartonText, false, "每箱毛重")
            : name == nameof(CbmOfCartonText) ? PackagingIssue(CbmOfCartonText, false, "每箱体积") : "";

        /// <summary>复制选择结果，保留用户输入的数量、交期和备注。</summary>
        public void ApplyProduct(SalesOrderProductLookupDto product)
        {
            if (IsNonStandard || product == null) return;
            ResetMismatchConfirmation(); InvalidateCheck();
            CustomerSku = product.CustomerSku ?? "";
            selectedMatchId = product.CustomerProductId;
            ProductId = product.ProductId; CustomerProductId = product.CustomerProductId;
            Sku = product.Sku ?? ""; LinkSku = product.LinkSku ?? "";
            DescriptionEn = product.DescriptionEn ?? ""; ColorNameEn = product.ColorNameEn ?? "";
            FeaturesEn = product.FeaturesEn ?? ""; MainImage = product.MainImage ?? "";
            UsdPriceText = product.UsdPrice?.ToString(CultureInfo.CurrentCulture) ?? "";
            CnyPriceText = product.CnyPrice?.ToString(CultureInfo.CurrentCulture) ?? "";
        }
        /// <summary>显式清除产品时清空关联快照与报价，避免旧客户资料残留。</summary>
        public void ClearProduct()
        {
            if (IsNonStandard) return;
            ResetMismatchConfirmation(); InvalidateCheck();
            ProductId = null; CustomerProductId = null; LinkSku = ""; Sku = ""; CustomerSku = "";
            DescriptionEn = ""; ColorNameEn = ""; FeaturesEn = ""; MainImage = "";
            UsdPriceText = ""; CnyPriceText = ""; SelectedProduct = null;
        }
        // 数值为空或无效时不沿用上一次合法数值，避免错误汇总看似有效。
        private static decimal? Parse(string text, bool positive) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
            && (positive ? value > 0 : value >= 0) ? value : (decimal?)null;
        private static decimal? Multiply(decimal? quantity, decimal? price)
        {
            try { return quantity * price; }
            catch (OverflowException) { return null; }
        }
        private void NotifyAmounts()
        {
            OnPropertyChanged(nameof(Qty)); OnPropertyChanged(nameof(UsdPrice)); OnPropertyChanged(nameof(CnyPrice)); OnPropertyChanged(nameof(UsdAmount)); OnPropertyChanged(nameof(CnyAmount)); OnPropertyChanged(nameof(Error));
        }
    }
}
