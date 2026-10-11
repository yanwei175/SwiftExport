using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models
{
    /// <summary>一条采购导入明细，系统身份只由检验结果填写，不允许配置反射赋值。</summary>
    public sealed class PurchaseOrderImportLine : ObservableObject, IDataErrorInfo
    {
        private string CustomerSkuValue = "";
        /// <summary>客户型号的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.CustomerSku", "客户型号", DisplayOrder = 10)]
        public string CustomerSku { get => CustomerSkuValue; set => SetProperty(ref CustomerSkuValue, value); }
        private string OriginalLinkSkuValue = "";
        /// <summary>Excel本司型号的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.LinkSku", "Excel本司型号", DisplayOrder = 11)]
        public string OriginalLinkSku { get => OriginalLinkSkuValue; set => SetProperty(ref OriginalLinkSkuValue, value); }
        private string SupplierSkuValue = "";
        /// <summary>工厂型号的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.SupplierSku", "工厂型号", DisplayOrder = 12)]
        public string SupplierSku { get => SupplierSkuValue; set => SetProperty(ref SupplierSkuValue, value); }
        private string DescriptionZhValue = "";
        /// <summary>中文描述的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.DescriptionZh", "描述", DisplayOrder = 13)]
        public string DescriptionZh { get => DescriptionZhValue; set => SetProperty(ref DescriptionZhValue, value); }
        private string FinishZhValue = "";
        /// <summary>表面处理的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.FinishZh", "表面处理", DisplayOrder = 14)]
        public string FinishZh { get => FinishZhValue; set => SetProperty(ref FinishZhValue, value); }
        private string FeaturesZhValue = "";
        /// <summary>配置的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.FeaturesZh", "配置", DisplayOrder = 15)]
        public string FeaturesZh { get => FeaturesZhValue; set => SetProperty(ref FeaturesZhValue, value); }
        private string UnitPriceTextValue = "";
        /// <summary>未税单价的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.UnitPrice", "未税单价", DisplayOrder = 16)]
        public string UnitPriceText { get => UnitPriceTextValue; set => SetProperty(ref UnitPriceTextValue, value); }
        private string QuantityTextValue = "";
        /// <summary>数量的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.Quantity", "数量", DisplayOrder = 17)]
        public string QuantityText { get => QuantityTextValue; set => SetProperty(ref QuantityTextValue, value); }

        private string SubNoteValue = "";
        /// <summary>行备注的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.SubNote", "行备注", DisplayOrder = 18)]
        public string SubNote { get => SubNoteValue; set => SetProperty(ref SubNoteValue, value); }

        private string UnitValue = "";
        /// <summary>非标单位的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.Unit", "非标单位", DisplayOrder = 19)]
        public string Unit { get => UnitValue; set => SetProperty(ref UnitValue, value); }

        private string TaxPriceTextValue = "";
        /// <summary>含税单价的编辑值，保留原始输入。</summary>
        [ExcelImportField("Line.TaxPrice", "含税单价", DisplayOrder = 20)]
        public string TaxPriceText { get => TaxPriceTextValue; set => SetProperty(ref TaxPriceTextValue, value); }

        private DateTime? plannedDeliveryDate;
        /// <summary>行交期独立编辑，不随单头后续修改。</summary>
        [ExcelImportField("Line.PlannedDeliveryDate", "计划交期", DisplayOrder = 21)]
        public DateTime? PlannedDeliveryDate { get => plannedDeliveryDate; set => SetProperty(ref plannedDeliveryDate, value); }
        private Guid? deliverySupplierId;
        private bool nonStandard, acceptMismatch, applying;
        private long revision;
        private Guid? chosenId;
        private PurchaseOrderImportProductCandidateDto selectedCandidate;
        private string status = "NotChecked", sku = "", linkSku = "", image = "", materialType = "", matchMessage = "";
        private string checkError = "";
        private IReadOnlyList<PurchaseOrderImportProductCandidateDto> candidates = Array.Empty<PurchaseOrderImportProductCandidateDto>();
        /// <summary>稳定的检查行标识，服务器创建明细身份另行生成。</summary>
        public Guid Uid { get; } = Guid.NewGuid();
        /// <summary>每次业务编辑递增，迟到检查不能回填。</summary>
        public long EditRevision => revision;
        /// <summary>明确转换为非标；失败匹配不自动转换。</summary>
        public bool IsNonStandard => nonStandard;
        /// <summary>仅非标行可手工填写单位。</summary>
        public bool CanEditUnit => IsNonStandard;
        /// <summary>非标采购不关联送达供应商；标准行资格由后端按物料类型校验。</summary>
        public bool CanChooseDeliverySupplier => !IsNonStandard;
        /// <summary>真实产品身份，字段目录不能写入。</summary>
        public Guid? ProductId { get; private set; }
        /// <summary>选定的客户产品关联，供重新检查。</summary>
        public Guid? SelectedCustomerProductId => chosenId;
        /// <summary>后端返回的真实 SKU。</summary>
        public string Sku => sku;
        /// <summary>后端返回的中性型号，与 Excel 原值分开。</summary>
        public string LinkSku => linkSku;
        /// <summary>系统产品图片地址；控件支持 HTTPS 与本地路径，仅使用内存缓存。</summary>
        public string MainImage => image;
        /// <summary>标准产品的物料类型。</summary>
        public string MaterialType => materialType;
        /// <summary>导入标准产品采购资格通过后显示采购供货方式。</summary>
        public string SupplyType => ProductId.HasValue ? "采购" : "";
        /// <summary>当前检查状态，任意业务编辑使整单保存资格失效。</summary>
        public string MatchStatus => status;
        /// <summary>匹配和字段校验反馈。</summary>
        public string MatchMessage => matchMessage;
        /// <summary>可排序的当前行检验资格，型号差异及非法输入也阻断。</summary>
        public bool IsCheckPassed => status == (IsNonStandard ? "NonStandard" : "Matched") && string.IsNullOrEmpty(checkError) && string.IsNullOrEmpty(InputError);
        /// <summary>紧凑状态文本；完整问题在行内处理弹层展示。</summary>
        public string CheckStatusText => IsCheckPassed ? "校验通过" : "未通过";
        /// <summary>数量小于等于零的视觉提示，非法原文仍由输入验证提示。</summary>
        public bool IsQuantityNonPositive => Quantity.HasValue && Quantity <= 0;
        /// <summary>当前客户型号下的候选。</summary>
        public IReadOnlyList<PurchaseOrderImportProductCandidateDto> MatchCandidates => candidates;
        /// <summary>待确认候选；选择后仍须执行使用候选并重新检查。</summary>
        public PurchaseOrderImportProductCandidateDto SelectedMatchCandidate { get => selectedCandidate; set => SetProperty(ref selectedCandidate, value); }
        /// <summary>确认 Excel 与系统中性型号差异，条件变化后复位。</summary>
        public bool AcceptLinkSkuMismatch { get => acceptMismatch; set => SetProperty(ref acceptMismatch, value); }
        /// <summary>客供件实际送达供应商，非标转换时清空。</summary>
        public Guid? DeliverySupplierId { get => deliverySupplierId; set => SetProperty(ref deliverySupplierId, value); }

        /// <summary>数量解析结果，空与非法输入不会变成零。</summary>
        public decimal? Quantity => PurchaseImportNumbers.Parse(QuantityText);
        /// <summary>未税单价解析结果。</summary>
        public decimal? UnitPrice => PurchaseImportNumbers.Parse(UnitPriceText);
        /// <summary>可选含税单价解析结果。</summary>
        public decimal? TaxPrice => PurchaseImportNumbers.Parse(TaxPriceText);
        /// <summary>原未税行金额，不应用单头优惠。</summary>
        public decimal? Amount => PurchaseImportNumbers.Amount(Quantity, UnitPrice);
        /// <summary>校验输入，不要求先匹配身份。</summary>
        public string InputError => this[nameof(QuantityText)] + this[nameof(UnitPriceText)] + this[nameof(TaxPriceText)]
            + (Quantity.HasValue && UnitPrice.HasValue && (!Amount.HasValue || Amount > 99999999999999.9999m) ? "金额超出支持范围。" : "")
            + (IsNonStandard && (string.IsNullOrWhiteSpace(DescriptionZh) || string.IsNullOrWhiteSpace(Unit)) ? "非标行需要描述及单位。" : "");
        /// <summary>原文字保留，校验提示不覆盖用户输入。</summary>
        public string Error => InputError;
        /// <summary>数量大于零，未税单价必填且非负，可选含税价非负，最多四位小数。</summary>
        public string this[string property] => property == nameof(QuantityText)
            ? PurchaseImportNumbers.Error(QuantityText, "数量", true, true, 4, 99999999999999.9999m) + (Quantity == 0 ? "数量必须大于零。" : "")
            : property == nameof(UnitPriceText) ? PurchaseImportNumbers.Error(UnitPriceText, "未税单价", true, true, 4, 99999999999999.9999m)
            : property == nameof(TaxPriceText) ? PurchaseImportNumbers.Error(TaxPriceText, "含税单价", false, true, 4, 99999999999999.9999m) : "";
        /// <summary>用户选择可用候选，不直接把候选当保存授权。</summary>
        public bool ChooseMatchCandidate()
        {
            if (selectedCandidate == null || !candidates.Contains(selectedCandidate) || !selectedCandidate.IsUsable) return false;
            chosenId = selectedCandidate.CustomerProductId; acceptMismatch = false; InvalidateCheck();
            OnPropertyChanged(nameof(SelectedCustomerProductId)); return true;
        }
        /// <summary>显式改变行类型，保留商业内容，清除旧身份和送达信息。</summary>
        public void SetNonStandard(bool value)
        {
            if (nonStandard == value) return;
            nonStandard = value; chosenId = null; acceptMismatch = false; deliverySupplierId = null; ClearIdentity(); InvalidateCheck();
            OnPropertyChanged(nameof(IsNonStandard)); OnPropertyChanged(nameof(CanEditUnit)); OnPropertyChanged(nameof(CanChooseDeliverySupplier)); OnPropertyChanged(nameof(DeliverySupplierId));
        }
        /// <summary>客户或身份边界变化时清空系统身份与旧候选。</summary>
        public void ResetMatch() { chosenId = null; acceptMismatch = false; ClearIdentity(); InvalidateCheck(); OnPropertyChanged(nameof(AcceptLinkSkuMismatch)); }
        /// <summary>匹配接纳同时核对行身份和编辑版本；复制候选避免外部集合后续变动。</summary>
        public bool TryApplyCheck(PurchaseOrderImportLineCheckDto result, long expectedRevision)
        {
            if (result == null || result.ClientLineId != Uid || revision != expectedRevision) return false;
            var product = result.Product;
            if (result.MatchStatus == "Matched" && (IsNonStandard || product == null || !product.IsUsable || !product.ProductId.HasValue || product.ProductId == Guid.Empty || product.CustomerProductId == Guid.Empty || string.IsNullOrWhiteSpace(product.Sku))) return false;
            if (result.MatchStatus == "NonStandard" && !IsNonStandard) return false;
            applying = true;
            try
            {
                ClearIdentity();
                candidates = Array.AsReadOnly((result.Candidates ?? new List<PurchaseOrderImportProductCandidateDto>()).Where(p => p != null).Select(Copy).ToArray());
                status = result.MatchStatus;
                if (status == "Matched") { ProductId = product.ProductId; chosenId = product.CustomerProductId; sku = product.Sku; linkSku = product.LinkSku; image = product.MainImage; materialType = product.MaterialType; Unit = product.Unit; }
                checkError = string.Join("；", (result.Issues ?? new List<PurchaseOrderImportIssueDto>()).Where(i => !i.RequiresConfirmation || !i.Acknowledged).Select(i => i.Message));
                matchMessage = !string.IsNullOrEmpty(checkError) ? checkError
                    : status == (IsNonStandard ? "NonStandard" : "Matched")
                        ? (string.IsNullOrEmpty(InputError) ? "校验通过。" : InputError) : "产品未匹配，请选择候选或修正客户型号。";
                NotifyMatch(); return true;
            }
            finally { applying = false; }
        }
        // 候选仅复制所需展示和身份字段，不保留响应 DTO 的可变实例。
        private static PurchaseOrderImportProductCandidateDto Copy(PurchaseOrderImportProductCandidateDto p) => new PurchaseOrderImportProductCandidateDto { CustomerProductId = p.CustomerProductId, ProductId = p.ProductId, CustomerSku = p.CustomerSku, Sku = p.Sku, LinkSku = p.LinkSku, Unit = p.Unit, MainImage = p.MainImage, MaterialType = p.MaterialType, IsUsable = p.IsUsable };
        /// <summary>使当前匹配失效，保留显示身份供核对，整单不能再保存。</summary>
        public void InvalidateCheck() { revision++; status = "NotChecked"; candidates = Array.Empty<PurchaseOrderImportProductCandidateDto>(); selectedCandidate = null; matchMessage = "资料已变化，请重新检验。"; NotifyMatch(); }
        private void ClearIdentity() { ProductId = null; sku = linkSku = image = materialType = ""; }
        private void NotifyMatch() { foreach (var key in new[] { nameof(ProductId), nameof(Sku), nameof(LinkSku), nameof(MainImage), nameof(MaterialType), nameof(SupplyType), nameof(MatchStatus), nameof(MatchMessage), nameof(MatchCandidates), nameof(SelectedMatchCandidate), nameof(IsCheckPassed), nameof(CheckStatusText), nameof(IsQuantityNonPositive), nameof(Error) }) base.OnPropertyChanged(new PropertyChangedEventArgs(key)); }
        /// <summary>仅真实业务属性使行版本失效，派生通知和候选临时选择不算编辑。</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SelectedMatchCandidate)) { base.OnPropertyChanged(args); return; }
            if (!applying && args.PropertyName != nameof(SelectedMatchCandidate) && args.PropertyName != nameof(SelectedCustomerProductId))
            {
                if (args.PropertyName == nameof(CustomerSku) || args.PropertyName == nameof(OriginalLinkSku)) { acceptMismatch = false; chosenId = null; ClearIdentity(); base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(AcceptLinkSkuMismatch))); }
                InvalidateCheck();
            }
            base.OnPropertyChanged(args); base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Amount))); base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(InputError)));
        }
    }
}

