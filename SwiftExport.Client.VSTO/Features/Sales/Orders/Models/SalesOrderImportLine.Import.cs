using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    public sealed partial class SalesOrderImportLine
    {
        private bool isNonStandard, applyingCheck, acceptLinkSkuMismatch;
        private long editRevision;
        private Guid? selectedMatchId;
        private SalesOrderImportMatchCandidate selectedMatchCandidate;
        private string originalLinkSku = "", linkSku = "", matchStatus = "NotChecked";
        private string pcsOfCartonText = "", nwOfCartonText = "", gwOfCartonText = "", cbmOfCartonText = "";
        private string originalAmountText = "", originalCartonsText = "", originalCbmText = "";
        private IReadOnlyList<SalesOrderImportMatchCandidate> matchCandidates = Array.AsReadOnly(new SalesOrderImportMatchCandidate[0]);
        private IReadOnlyList<SalesOrderImportCheckIssue> checkIssues = Array.AsReadOnly(new SalesOrderImportCheckIssue[0]);
        /// <summary>原始来源与解析问题，不参与产品身份赋值。</summary>
        public SalesOrderImportSource Source { get; } = new SalesOrderImportSource();
        /// <summary>请求开始时捕获的编辑版本；页面还需核对客户、会话和生命周期。</summary>
        public long EditRevision => editRevision;
        /// <summary>原表 Aquavit Code；检查请求使用此值，不能用系统匹配值替换。</summary>
        [ExcelImportField("Line.LinkSku", "Excel本司型号", DisplayOrder = 11)]
        public string OriginalLinkSku { get => originalLinkSku; set => SetProperty(ref originalLinkSku, value); }
        /// <summary>检查返回的系统中性型号，和真实 SKU、原表型号分开显示。</summary>
        public string LinkSku { get => linkSku; private set => SetProperty(ref linkSku, value); }
        /// <summary>后端逐行状态；编辑后回到 NotChecked，不能据此单独决定整单可保存。</summary>
        public string MatchStatus => matchStatus;
        /// <summary>行头只显示通过或未通过；产品身份通过后仍须满足本行输入和差异确认要求。</summary>
        public bool IsCheckPassed => (MatchStatus == "Matched" || MatchStatus == "NonStandard") && string.IsNullOrEmpty(Error);
        /// <summary>红绿状态的文字替代，供提示及辅助阅读使用；不代表整单可以保存。</summary>
        public string CheckStatusText => IsCheckPassed ? "校验通过" : "未校验通过";
        /// <summary>本次检查的不可变候选快照；多候选禁止自动取第一条。</summary>
        public IReadOnlyList<SalesOrderImportMatchCandidate> MatchCandidates => matchCandidates;
        /// <summary>本次检查的问题快照；确认提示应重新请求后端验证。</summary>
        public IReadOnlyList<SalesOrderImportCheckIssue> CheckIssues => checkIssues;
        /// <summary>用户对本行中性型号差异的明确确认；再次改变型号或产品时复位。</summary>
        public bool AcceptLinkSkuMismatch { get => acceptLinkSkuMismatch; set => SetProperty(ref acceptLinkSkuMismatch, value); }
        /// <summary>每箱数量输入文字，保留未完成或非法输入。</summary>
        [ExcelImportField("Line.PcsOfCarton", "每箱数量", DisplayOrder = 18)]
        public string PcsOfCartonText { get => pcsOfCartonText; set { if (SetProperty(ref pcsOfCartonText, value)) { OnPropertyChanged(nameof(PcsOfCarton)); OnPropertyChanged(nameof(Error)); } } }
        /// <summary>每箱净重输入文字，保留未完成或非法输入。</summary>
        [ExcelImportField("Line.NwOfCarton", "每箱净重", DisplayOrder = 19)]
        public string NwOfCartonText { get => nwOfCartonText; set { if (SetProperty(ref nwOfCartonText, value)) { OnPropertyChanged(nameof(NwOfCarton)); OnPropertyChanged(nameof(Error)); } } }
        /// <summary>每箱毛重输入文字，保留未完成或非法输入。</summary>
        [ExcelImportField("Line.GwOfCarton", "每箱毛重", DisplayOrder = 20)]
        public string GwOfCartonText { get => gwOfCartonText; set { if (SetProperty(ref gwOfCartonText, value)) { OnPropertyChanged(nameof(GwOfCarton)); OnPropertyChanged(nameof(Error)); } } }
        /// <summary>每箱体积输入文字，保留未完成或非法输入。</summary>
        [ExcelImportField("Line.CbmOfCarton", "每箱体积", DisplayOrder = 21)]
        public string CbmOfCartonText { get => cbmOfCartonText; set { if (SetProperty(ref cbmOfCartonText, value)) { OnPropertyChanged(nameof(CbmOfCarton)); OnPropertyChanged(nameof(Error)); } } }
        /// <summary>已验证的PcsOfCarton值；空或非法输入返回空值，原文字仍保留。</summary>
        public decimal? PcsOfCarton => Parse(PcsOfCartonText, true);
        /// <summary>已验证的NwOfCarton值；空或非法输入返回空值，原文字仍保留。</summary>
        public decimal? NwOfCarton => Parse(NwOfCartonText, false);
        /// <summary>已验证的GwOfCarton值；空或非法输入返回空值，原文字仍保留。</summary>
        public decimal? GwOfCarton => Parse(GwOfCartonText, false);
        /// <summary>已验证的CbmOfCarton值；空或非法输入返回空值，原文字仍保留。</summary>
        public decimal? CbmOfCarton => Parse(CbmOfCartonText, false);
        /// <summary>原表 Amount，仅供后续对账，不能代替计算值或落库。</summary>
        public string OriginalAmountText { get => originalAmountText; set => SetProperty(ref originalAmountText, value); }
        /// <summary>原表 CTNS，仅供后续对账，不能代替计算值或落库。</summary>
        public string OriginalCartonsText { get => originalCartonsText; set => SetProperty(ref originalCartonsText, value); }
        /// <summary>原表 CBMS，仅供后续对账，不能代替计算值或落库。</summary>
        public string OriginalCbmText { get => originalCbmText; set => SetProperty(ref originalCbmText, value); }

        /// <summary>多候选列表的待选项，仅为 UI 状态；使用候选命令后才进入检查请求。</summary>
        public SalesOrderImportMatchCandidate SelectedMatchCandidate { get => selectedMatchCandidate; set => SetProperty(ref selectedMatchCandidate, value); }
        /// <summary>用户明确选定的客户产品关联；修改客户型号或转换行类型后清除。</summary>
        public Guid? SelectedMatchId => selectedMatchId;
        /// <summary>显示本行的匹配反馈及需确认问题。</summary>
        public string MatchMessage => CheckStatusText + "：" + string.Join("；", CheckIssues.Select(v => v.Message));
        /// <summary>仅接纳当前候选集中的可用关联，不取第一项，也不直接赋产品身份。</summary>
        public bool ChooseMatchCandidate()
        {
            var candidate = SelectedMatchCandidate;
            if (candidate == null || !MatchCandidates.Contains(candidate) || !candidate.IsUsable) return false;
            selectedMatchId = candidate.CustomerProductId;
            ResetMismatchConfirmation(); InvalidateCheck();
            OnPropertyChanged(nameof(SelectedMatchId)); return true;
        }
        /// <summary>独立校验输入文字，避免非法可空单价被序列化成未填写；产品匹配由后端执行。</summary>
        public string InputError => Source.Error + PackagingError + this[nameof(QtyText)] + this[nameof(UsdPriceText)] + this[nameof(CnyPriceText)]
            + (Qty.HasValue && ((UsdPrice.HasValue && !UsdAmount.HasValue) || (CnyPrice.HasValue && !CnyAmount.HasValue)) ? "金额超过支持范围。" : "");

        // 未确认警告与后端错误进入本地汇总，最终整单保存仍由页面和服务端控制。
        private string CheckError => string.Join("；", CheckIssues.Where(v => !v.RequiresConfirmation || !v.Acknowledged).Select(v => v.Message));

        // 包装数允许空；非法文字必须阻断本地完整性，不能混同于未提供。
        private string PackagingError => string.Join("", new[] { PackagingIssue(PcsOfCartonText, true, "每箱数量"),
            PackagingIssue(NwOfCartonText, false, "每箱净重"), PackagingIssue(GwOfCartonText, false, "每箱毛重"), PackagingIssue(CbmOfCartonText, false, "每箱体积") });
        private static string PackagingIssue(string text, bool positive, string label) =>
            !string.IsNullOrWhiteSpace(text) && !Parse(text, positive).HasValue ? label + (positive ? "必须大于零。" : "必须为非负数字。") : "";

        /// <summary>创建来自 Excel 的标准行，缺失数量不沿用手工行默认的 1。</summary>
        public static SalesOrderImportLine CreateForImport() => new SalesOrderImportLine(false, null) { QtyText = "" };

        /// <summary>将未匹配产品明确改为非标，保留商业资料并清空身份；反向转换必须重新匹配。</summary>
        public void SetNonStandard(bool value)
        {
            if (isNonStandard == value) return;
            isNonStandard = value; selectedMatchId = null;
            ProductId = null; CustomerProductId = null; LinkSku = ""; MainImage = "";
            Sku = value ? NonStandardSkuGenerator.Create(Uid) : "";
            SelectedProduct = null; ProductCandidates.Clear();
            ResetMismatchConfirmation(); InvalidateCheck();
            OnPropertyChanged(nameof(IsNonStandard));
            OnPropertyChanged(nameof(LineTypeText));
            OnPropertyChanged(nameof(Error));
            SearchCommand?.NotifyCanExecuteChanged();
            CommitProductCommand?.NotifyCanExecuteChanged();
            ClearProductCommand?.NotifyCanExecuteChanged();
        }

        /// <summary>接纳同一行同一编辑版本的检查结果，仅回填身份和系统型号／单位／图片，保留 Excel 的成交快照。</summary>
        public bool TryApplyCheck(SalesOrderImportLineCheckDto result, long expectedRevision)
        {
            if (result == null || result.ClientLineId != Uid || expectedRevision != EditRevision) return false;
            var status = result.MatchStatus;
            if (IsNonStandard ? status != "NonStandard" : !new[] { "Matched", "NotMatched", "Ambiguous", "Unavailable" }.Contains(status)) return false;
            var product = result.Product;
            if (status == "Matched" && (product == null || !product.IsUsable || !product.ProductId.HasValue || product.ProductId == Guid.Empty
                || product.CustomerProductId == Guid.Empty || string.IsNullOrWhiteSpace(product.Sku) || string.IsNullOrWhiteSpace(product.Unit))) return false;
            applyingCheck = true;
            try
            {
                // 转为不可变副本，后台 DTO 或候选集合后续改变不影响页面已接纳结果。
                matchCandidates = Array.AsReadOnly((result.Candidates ?? new List<SalesOrderImportProductCandidateDto>())
                    .Where(v => v != null).Select(v => new SalesOrderImportMatchCandidate(v)).ToArray());
                checkIssues = Array.AsReadOnly((result.Issues ?? new List<SalesOrderImportIssueDto>())
                    .Where(v => v != null).Select(v => new SalesOrderImportCheckIssue(v)).ToArray());
                ProductId = status == "Matched" ? product.ProductId : null;
                CustomerProductId = status == "Matched" ? (Guid?)product.CustomerProductId : null;
                Sku = status == "Matched" ? product.Sku : IsNonStandard ? NonStandardSkuGenerator.Create(Uid) : "";
                LinkSku = status == "Matched" ? product.LinkSku ?? "" : "";
                MainImage = status == "Matched" ? product.MainImage ?? "" : "";
                if (status == "Matched") UnitSnapshot = product.Unit;
                matchStatus = status; selectedMatchCandidate = null;
                OnPropertyChanged(nameof(SelectedMatchCandidate)); OnPropertyChanged(nameof(MatchMessage));
                OnPropertyChanged(nameof(MatchStatus)); OnPropertyChanged(nameof(MatchCandidates)); OnPropertyChanged(nameof(CheckIssues)); OnPropertyChanged(nameof(Error));
                return true;
            }
            finally { applyingCheck = false; }
        }

        /// <summary>客户、会话或页面条件变化时由页面显式调用，使此前检查结果失效。</summary>
        public void InvalidateCheck()
        {
            editRevision++;
            matchStatus = "NotChecked"; selectedMatchCandidate = null;
            OnPropertyChanged(nameof(SelectedMatchCandidate));
            matchCandidates = Array.AsReadOnly(new SalesOrderImportMatchCandidate[0]);
            checkIssues = Array.AsReadOnly(new SalesOrderImportCheckIssue[0]);
            OnPropertyChanged(nameof(EditRevision)); OnPropertyChanged(nameof(MatchStatus));
            OnPropertyChanged(nameof(MatchCandidates)); OnPropertyChanged(nameof(CheckIssues)); OnPropertyChanged(nameof(MatchMessage)); OnPropertyChanged(nameof(Error));
        }

        // 人工输入引起检查失效；派生值、搜索 UI 状态和检查结果通知不能反过来递增编辑版本。
        /// <summary>业务输入变化通知 UI 前使检查版本失效，拒绝迟到回填。</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!applyingCheck && IsInput(e.PropertyName))
            {
                if (e.PropertyName == nameof(CustomerSku) || e.PropertyName == nameof(OriginalLinkSku)) ResetMismatchConfirmation();
                if (e.PropertyName == nameof(CustomerSku))
                {
                    selectedMatchId = null;
                    ProductId = null; CustomerProductId = null; LinkSku = ""; MainImage = "";
                    if (!IsNonStandard) Sku = "";
                }
                InvalidateCheck();
            }
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(Error) || e.PropertyName == nameof(MatchStatus))
            {
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCheckPassed)));
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(CheckStatusText)));
            }
        }
        // 型号差异确认仅对当时的原表型号与匹配条件有效。
        private void ResetMismatchConfirmation()
        {
            if (!acceptLinkSkuMismatch) return;
            acceptLinkSkuMismatch = false; OnPropertyChanged(nameof(AcceptLinkSkuMismatch));
        }
        private static bool IsInput(string name) => new[] { nameof(CustomerSku), nameof(OriginalLinkSku), nameof(DescriptionEn),
            nameof(ColorNameEn), nameof(FeaturesEn), nameof(UnitSnapshot), nameof(SubNote), nameof(ExpectedDeliveryDate), nameof(QtyText),
            nameof(UsdPriceText), nameof(CnyPriceText), nameof(PcsOfCartonText), nameof(NwOfCartonText), nameof(GwOfCartonText),
            nameof(CbmOfCartonText), nameof(AcceptLinkSkuMismatch) }.Contains(name);
    }
}
