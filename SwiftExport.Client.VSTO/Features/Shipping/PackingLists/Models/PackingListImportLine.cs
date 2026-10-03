using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models
{
    /// <summary>装箱单独立导入行，保留原文；产品身份仅由检查接纳，不暴露为映射字段。</summary>
    public sealed class PackingListImportLine : ObservableObject
    {
        /// <summary>跨检查及重试稳定的行身份。</summary>
        public Guid Uid { get; } = Guid.NewGuid();
        private string customerSku = "";
        /// <summary>客户型号的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.CustomerSku", "客户型号")]
        public string CustomerSku { get => customerSku; set => SetProperty(ref customerSku, value); }
        private string originalLinkSku = "";
        /// <summary>Excel中性型号的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.OriginalLinkSku", "Excel中性型号")]
        public string OriginalLinkSku { get => originalLinkSku; set => SetProperty(ref originalLinkSku, value); }
        private string descriptionEn = "";
        /// <summary>英文描述的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.DescriptionEn", "英文描述")]
        public string DescriptionEn { get => descriptionEn; set => SetProperty(ref descriptionEn, value); }
        private string colorNameEn = "";
        /// <summary>颜色的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.ColorNameEn", "颜色")]
        public string ColorNameEn { get => colorNameEn; set => SetProperty(ref colorNameEn, value); }
        private string unit = "";
        /// <summary>单位的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.Unit", "单位")]
        public string Unit { get => unit; set => SetProperty(ref unit, value); }
        private string qtyText = "";
        /// <summary>数量的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.Qty", "数量")]
        public string QtyText { get => qtyText; set => SetProperty(ref qtyText, value); }
        private string pcsPerPackageText = "";
        /// <summary>每箱数量的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.PcsPerPackage", "每箱数量")]
        public string PcsPerPackageText { get => pcsPerPackageText; set => SetProperty(ref pcsPerPackageText, value); }
        private string nwPerPackageText = "";
        /// <summary>每箱净重的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.NwPerPackage", "每箱净重")]
        public string NwPerPackageText { get => nwPerPackageText; set => SetProperty(ref nwPerPackageText, value); }
        private string gwPerPackageText = "";
        /// <summary>每箱毛重的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.GwPerPackage", "每箱毛重")]
        public string GwPerPackageText { get => gwPerPackageText; set => SetProperty(ref gwPerPackageText, value); }
        private string cbmPerPackageText = "";
        /// <summary>每箱体积的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.CbmPerPackage", "每箱体积")]
        public string CbmPerPackageText { get => cbmPerPackageText; set => SetProperty(ref cbmPerPackageText, value); }
        private string customerPoNo = "";
        /// <summary>PO原文的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.CustomerPoNo", "PO原文")]
        public string CustomerPoNo { get => customerPoNo; set => SetProperty(ref customerPoNo, value); }
        private string invoiceNo = "";
        /// <summary>Invoice原文的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.InvoiceNo", "Invoice原文")]
        public string InvoiceNo { get => invoiceNo; set => SetProperty(ref invoiceNo, value); }
        private string marks = "";
        /// <summary>箱唛的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.Marks", "箱唛")]
        public string Marks { get => marks; set => SetProperty(ref marks, value); }
        private string note = "";
        /// <summary>备注的可编辑原文；修改会使检查失效。</summary>
        [ExcelImportField("Line.Note", "备注")]
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
        private string matchMessage = "尚未检查", sku = "", linkSku = "";
        /// <summary>检查与字段问题反馈。</summary>
        public string MatchMessage { get => matchMessage; private set => SetProperty(ref matchMessage, value); }
        /// <summary>真实系统 SKU，只读展示。</summary>
        public string Sku { get => sku; private set => SetProperty(ref sku, value); }
        /// <summary>真实系统中性型号，只读展示。</summary>
        public string LinkSku { get => linkSku; private set => SetProperty(ref linkSku, value); }
        private Guid? productId;
        /// <summary>从文本解析的数量，非法输入返回空，不变成零。</summary>
        public decimal? Qty => Number(QtyText);
        /// <summary>失效标记不清除原文及手工候选。</summary>
        public void Invalidate() { MatchMessage = "已修改，请重新检查"; productId = null; Sku = LinkSku = ""; }
        /// <summary>接纳同版本检查结果，按关系身份恢复显式选择；不覆盖描述、颜色和备注。</summary>
        public void Apply(PackingListImportLineCheckDto result)
        {
            var selectedId = SelectedCandidate?.CustomerProductId;
            Candidates = result.Candidates ?? new List<PackingListImportProductCandidateDto>();
            // HTTP 每次生成新候选对象，ItemsSource 更新会清空 TwoWay 选择；只恢复同一关系，失效关系不选替代项。
            SelectedCandidate = selectedId.HasValue ? Candidates.FirstOrDefault(x => x.CustomerProductId == selectedId.Value) : null;
            productId = result.Product?.ProductId; Sku = result.Product?.Sku ?? ""; LinkSku = result.Product?.LinkSku ?? "";
            if (!IsNonStandard && result.Product != null) Unit = result.Product.Unit;
            MatchMessage = result.MatchStatus + " " + string.Join("；", result.Issues.ConvertAll(i => i.Message));
        }
        /// <summary>构造纯 DTO，必填数字由页面及检查服务阻断。</summary>
        public PackingListDraftLineInput ToInput(int number) => new PackingListDraftLineInput {
            Uid = Uid, LineNumber = number, ProductId = productId, IsNonStandardSnapshot = IsNonStandard,
            SelectedCustomerProductId = SelectedCandidate?.CustomerProductId, OriginalLinkSku = OriginalLinkSku,
            AcceptLinkSkuMismatch = AcceptLinkSkuMismatch, SkuSnapshot = Sku, LinkSkuSnapshot = IsNonStandard ? OriginalLinkSku : LinkSku,
            CustomerSkuSnapshot = CustomerSku, DescriptionEnSnapshot = DescriptionEn, ColorNameEnSnapshot = ColorNameEn,
            UnitSnapshot = Unit, Qty = Qty ?? 0, PcsPerPackage = Number(PcsPerPackageText),
            NwPerPackage = Number(NwPerPackageText), GwPerPackage = Number(GwPerPackageText), CbmPerPackage = Number(CbmPerPackageText),
            CustomerPoNo = CustomerPoNo, InvoiceNo = InvoiceNo, Marks = Marks, Note = Note };
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
