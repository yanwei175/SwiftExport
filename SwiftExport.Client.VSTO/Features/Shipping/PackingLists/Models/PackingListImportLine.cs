using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models
{
    /// <summary>装箱单独立导入行，保留原文；产品身份仅由检查接纳，不暴露为映射字段。</summary>
    public sealed class PackingListImportLine : ObservableObject
    {
        /// <summary>跨检查及重试稳定的行身份。</summary>
        public Guid Uid { get; } = Guid.NewGuid();
        private int lineNumber;
        /// <summary>本地导入顺序；删除或视图排序不重排，不作为地址配置字段。</summary>
        public int LineNumber => lineNumber;
        /// <summary>行输入版本，用于拒绝旧匹配结果。</summary>
        public long EditRevision { get; private set; }
        /// <summary>集合接纳新行后赋号，仅通知显示，不算业务编辑。</summary>
        internal void AssignLineNumber(int value) { lineNumber = value; OnPropertyChanged(nameof(LineNumber)); }
        private string marks = "";
        /// <summary>箱唛的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.Marks", "箱唛", DisplayOrder = 8)]
        public string Marks { get => marks; set => SetProperty(ref marks, value); }

        private string customerSku = "";
        /// <summary>客户型号的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.CustomerSku", "客户型号", DisplayOrder = 10)]
        public string CustomerSku { get => customerSku; set => SetProperty(ref customerSku, value); }
        private string originalLinkSku = "";
        /// <summary>Excel中性型号的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.OriginalLinkSku", "中性型号", DisplayOrder = 20)]
        public string OriginalLinkSku { get => originalLinkSku; set => SetProperty(ref originalLinkSku, value); }
        private string descriptionEn = "";
        /// <summary>英文描述的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.DescriptionEn", "描述", DisplayOrder = 30)]
        public string DescriptionEn { get => descriptionEn; set => SetProperty(ref descriptionEn, value); }
        private string colorNameEn = "";
        /// <summary>颜色的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.ColorNameEn", "颜色", DisplayOrder = 40)]
        public string ColorNameEn { get => colorNameEn; set => SetProperty(ref colorNameEn, value); }
        private string qtyText = "";
        /// <summary>数量的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.Qty", "数量", DisplayOrder = 50)]
        public string QtyText { get => qtyText; set => SetProperty(ref qtyText, value); }
        private string pcsPerPackageText = "";
        /// <summary>每箱数量的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.PcsPerPackage", "每箱数量", DisplayOrder = 60)]
        public string PcsPerPackageText { get => pcsPerPackageText; set => SetProperty(ref pcsPerPackageText, value); }
        private string nwPerPackageText = "";
        /// <summary>每箱净重的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.NwPerPackage", "每箱净重", DisplayOrder = 70)]
        public string NwPerPackageText { get => nwPerPackageText; set => SetProperty(ref nwPerPackageText, value); }
        private string gwPerPackageText = "";
        /// <summary>每箱毛重的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.GwPerPackage", "每箱毛重", DisplayOrder = 80)]
        public string GwPerPackageText { get => gwPerPackageText; set => SetProperty(ref gwPerPackageText, value); }
        private string cbmPerPackageText = "";
        /// <summary>每箱体积的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.CbmPerPackage", "每箱体积", DisplayOrder = 90)]
        public string CbmPerPackageText { get => cbmPerPackageText; set => SetProperty(ref cbmPerPackageText, value); }
        private string customerPoNo = "";
        /// <summary>PO原文的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.CustomerPoNo", "PoNo.", DisplayOrder = 100)]
        public string CustomerPoNo { get => customerPoNo; set => SetProperty(ref customerPoNo, value); }
        private string invoiceNo = "";
        /// <summary>Invoice原文的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.InvoiceNo", "行发票号列", DisplayOrder = 110)]
        public string InvoiceNo { get => invoiceNo; set => SetProperty(ref invoiceNo, value); }

        private string unit = "";
        /// <summary>单位的可编辑原文；修改会使检查失效。</summary>
        public string Unit { get => unit; set => SetProperty(ref unit, value); }


        private string note = "";
        /// <summary>备注的可编辑原文；修改会使检查失效。</summary>
        public string Note { get => note; set => SetProperty(ref note, value); }

        private bool isNonStandard, acceptLinkSkuMismatch;
        /// <summary>用户明确标记非标，不能因匹配失败自动启用。</summary>
        public bool IsNonStandard { get => isNonStandard; set => SetProperty(ref isNonStandard, value); }
        /// <summary>用户确认原始中性型号与系统型号的差异。</summary>
        public bool AcceptLinkSkuMismatch { get => acceptLinkSkuMismatch; set => SetProperty(ref acceptLinkSkuMismatch, value); }
        private PackingListImportProductCandidateDto selectedCandidate;
        /// <summary>多候选时由用户明确选择关系，再次检查后才能保存。</summary>
        public PackingListImportProductCandidateDto SelectedCandidate { get => selectedCandidate; set => SetProperty(ref selectedCandidate, value); }
        private IReadOnlyList<PackingListImportProductCandidateDto> candidates = new List<PackingListImportProductCandidateDto>();
        /// <summary>后端返回的客户产品关系候选。</summary>
        public IReadOnlyList<PackingListImportProductCandidateDto> Candidates { get => candidates; private set => SetProperty(ref candidates, value); }
        private string matchMessage = "尚未检查", sku = "", linkSku = "", mainImage = "";
        private bool isCheckPassed;
        /// <summary>当前行检查通过标记；独立行检查不代表整单可以保存。</summary>
        public bool IsCheckPassed { get => isCheckPassed; private set { if (SetProperty(ref isCheckPassed, value)) OnPropertyChanged(nameof(CheckLabel)); } }
        /// <summary>可排序状态列的显示文字，完整问题在处理弹层中展示。</summary>
        public string CheckLabel => IsCheckPassed ? "通过" : "未通过";
        /// <summary>产品图片地址；控件统一负责 HTTPS／本地读取及内存缓存。</summary>
        public string MainImage { get => mainImage; private set => SetProperty(ref mainImage, value); }
        /// <summary>检查与字段问题反馈。</summary>
        public string MatchMessage { get => matchMessage; private set => SetProperty(ref matchMessage, value); }
        /// <summary>真实系统 SKU，只读展示。</summary>
        public string Sku { get => sku; private set => SetProperty(ref sku, value); }
        /// <summary>真实系统中性型号，只读展示。</summary>
        public string LinkSku { get => linkSku; private set => SetProperty(ref linkSku, value); }
        private Guid? productId;
        /// <summary>从文本解析的数量，非法输入返回空，不变成零。</summary>
        public decimal? Qty => Number(QtyText);
        /// <summary>非正数量用红字提醒；无法解析的原文仍由检验阻断。</summary>
        public bool IsQuantityNonPositive => Qty.HasValue && Qty <= 0;
        /// <summary>通知数量视觉，不改变输入或检查版本。</summary>
        internal void NotifyInputState() { OnPropertyChanged(nameof(Qty)); OnPropertyChanged(nameof(IsQuantityNonPositive)); }
        /// <summary>撤销检查；客户或产品定位变化才清身份，普通商业输入不抹去已匹配产品。</summary>
        public void Invalidate(bool clearProduct = false)
        {
            EditRevision++; IsCheckPassed = false; MatchMessage = "已修改，请重新检查";
            if (!clearProduct) return;
            productId = null; Sku = LinkSku = MainImage = ""; Candidates = new List<PackingListImportProductCandidateDto>();
            SelectedCandidate = null; AcceptLinkSkuMismatch = false;
        }
        /// <summary>接纳同版本检查结果，按关系身份恢复显式选择；不覆盖描述、颜色和备注。</summary>
        public void Apply(PackingListImportLineCheckDto result)
        {
            var selectedId = SelectedCandidate?.CustomerProductId;
            Candidates = result.Candidates ?? new List<PackingListImportProductCandidateDto>();
            // HTTP 每次生成新候选对象，ItemsSource 更新会清空 TwoWay 选择；只恢复同一关系，失效关系不选替代项。
            SelectedCandidate = selectedId.HasValue ? Candidates.FirstOrDefault(x => x.CustomerProductId == selectedId.Value) : null;
            var product = result.Product;
            var usable = !IsNonStandard && result.MatchStatus == "Matched" && product != null && product.IsUsable
                && product.ProductId.HasValue && product.ProductId != Guid.Empty && !string.IsNullOrWhiteSpace(product.Sku)
                && !string.IsNullOrWhiteSpace(product.Unit);
            productId = usable ? product.ProductId : null;
            Sku = usable ? product.Sku : ""; LinkSku = usable ? product.LinkSku : ""; MainImage = usable ? product.MainImage : "";
            if (usable) Unit = product.Unit;
            var issues = result.Issues ?? new List<PackingListImportIssueDto>();
            IsCheckPassed = (usable || IsNonStandard && result.MatchStatus == "NonStandard"
                && !string.IsNullOrWhiteSpace(DescriptionEn) && !string.IsNullOrWhiteSpace(Unit))
                && !HasInvalidNumber && !issues.Any(i => !i.RequiresConfirmation || !i.Acknowledged);
            MatchMessage = string.Join("；", issues.Select(i => i.Message));
            if (HasInvalidNumber) MatchMessage = (MatchMessage + "；数量或包装输入无效，请修正原文。").TrimStart('；');
            if (!IsCheckPassed && string.IsNullOrWhiteSpace(MatchMessage)) MatchMessage = "未取得有效产品或非标资料，请重新匹配。";
        }
        /// <summary>构造纯 DTO，必填数字由页面及检查服务阻断。</summary>
        public PackingListDraftLineInput ToInput(int number) => new PackingListDraftLineInput
        {
            Uid = Uid,
            LineNumber = number,
            ProductId = productId,
            IsNonStandardSnapshot = IsNonStandard,
            SelectedCustomerProductId = SelectedCandidate?.CustomerProductId,
            OriginalLinkSku = OriginalLinkSku,
            AcceptLinkSkuMismatch = AcceptLinkSkuMismatch,
            SkuSnapshot = Sku,
            LinkSkuSnapshot = IsNonStandard ? OriginalLinkSku : LinkSku,
            CustomerSkuSnapshot = CustomerSku,
            DescriptionEnSnapshot = DescriptionEn,
            ColorNameEnSnapshot = ColorNameEn,
            UnitSnapshot = Unit,
            Qty = Qty ?? 0,
            PcsPerPackage = Number(PcsPerPackageText),
            NwPerPackage = Number(NwPerPackageText),
            GwPerPackage = Number(GwPerPackageText),
            CbmPerPackage = Number(CbmPerPackageText),
            CustomerPoNo = CustomerPoNo,
            InvoiceNo = InvoiceNo,
            Marks = Marks,
            Note = Note
        };
        /// <summary>输入期间保留文字；空值与非法值都由调用方分别判断。</summary>
        public static decimal? Number(string text) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : (decimal?)null;
        /// <summary>检查可选包装数字，防止非法文字被当作空值。</summary>
        public bool HasInvalidNumber => !Qty.HasValue || Qty <= 0 || new[] { PcsPerPackageText, NwPerPackageText, GwPerPackageText, CbmPerPackageText }.AnyInvalid();
    }
    /// <summary>可选数字的输入检查，仅用于导入模型。</summary>
    internal static class PackingListNumberInputs
    {
        internal static bool AnyInvalid(this string[] values) { foreach (var value in values) if (!string.IsNullOrWhiteSpace(value) && !PackingListImportLine.Number(value).HasValue) return true; return false; }
    }
}
