using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    /// <summary>编排销售导入、产品匹配和新建草稿；不访问 Excel COM，不维护既有单据。</summary>
    public sealed partial class SalesOrderImportViewModel : ViewModelBaseEx
    {
        private readonly SessionService session;
        private readonly IUiDispatcher dispatcher;
        private readonly ISalesOrderDetailApi api;
        private readonly ApiCallRunner errors;
        private readonly ISalesOrderImportPrompt prompt;
        private readonly HashSet<SalesOrderImportLine> trackedLines = new HashSet<SalesOrderImportLine>();
        private readonly List<SalesOrderImportLine> selectedLines = new List<SalesOrderImportLine>();
        private SalesOrderImportHeader header;
        private CustomerDto selectedCustomer, acceptedCustomer;
        private bool ready, initializing, dirty, resetting;
        private long pageVersion;

        /// <summary>注入共享缓存与当前会话；构造期间不发送网络请求。</summary>
        public SalesOrderImportViewModel(IReferenceDataCacheService referenceData, SessionService session,
            IUiDispatcher dispatcher, ISalesOrderDetailApi api, ApiCallRunner errors, ISalesOrderImportPrompt prompt,
            ExcelReadProfileService profileService = null,
            IExcelAddressReader addressReader = null, ExcelImportRegistry imports = null, SwiftExport.Contracts.Apis.ForExcel.IForExcelSalesOrderApi checkApi = null)
        {
            ReferenceData = referenceData; this.session = session; this.dispatcher = dispatcher;
            this.api = api; this.errors = errors; this.prompt = prompt;
            pageVersion = session.Capture().Version;
            header = new SalesOrderImportHeader(); header.PropertyChanged += HeaderChanged;
            Title = "销售订单导入";
            InitializeCommand = new AsyncRelayCommand(InitializeAsync, () => !IsDisposed && !initializing && !parsing && !SubmissionLocked && session.Capture().User != null);
            AddStandardLineCommand = new RelayCommand(() => AddLine(false), () => IsEditable && Header.CustomerId != Guid.Empty);
            AddNonStandardLineCommand = new RelayCommand(() => AddLine(true), () => IsEditable && Header.CustomerId != Guid.Empty);
            DeleteSelectedLineCommand = new RelayCommand(DeleteLines, () => IsEditable && selectedLines.Any(Lines.Contains));
            CustomerSelectedCommand = new RelayCommand(() => ChangeCustomer(SelectedCustomer), () => IsEditable);
            CustomerClearedCommand = new RelayCommand(() => ChangeCustomer(null), () => IsEditable);
            foreach (var command in new IRelayCommand[] { InitializeCommand, AddStandardLineCommand, AddNonStandardLineCommand, DeleteSelectedLineCommand, CustomerSelectedCommand, CustomerClearedCommand }) RegisterCommand(command);
            Lines.CollectionChanged += LinesChanged;
            ((INotifyCollectionChanged)ReferenceData.Currencies).CollectionChanged += ReferenceChanged;
            ((INotifyCollectionChanged)ReferenceData.DictionaryValues).CollectionChanged += ReferenceChanged;
            ((INotifyCollectionChanged)ReferenceData.Customers).CollectionChanged += ReferenceChanged;
            session.Changed += SessionChanged;
            InitializeAddresses(profileService, addressReader, imports);
            InitializeSubmission(checkApi);
            RefreshOptions();
        }
        public IReferenceDataCacheService ReferenceData { get; }
        public SalesOrderImportHeader Header => header;
        public ObservableCollection<SalesOrderImportLine> Lines { get; } = new ObservableCollection<SalesOrderImportLine>();
        public CustomerDto SelectedCustomer { get => selectedCustomer; set => SetProperty(ref selectedCustomer, value); }
        /// <summary>仅当前已加载、已认证的会话可以编辑。</summary>
        public bool IsEditable => !IsDisposed && ready && !initializing && !parsing && !SubmissionLocked && session.Capture().User != null && session.Capture().Version == pageVersion;
        /// <summary>提供 DataGrid 的只读状态。</summary>
        public bool IsReadOnly => !IsEditable;
        /// <summary>记录本地草稿是否发生编辑，用于关闭确认。</summary>
        public bool HasEdits => dirty;
        /// <summary>提供基础资料进度或脱敏错误。</summary>
        public string BusyMessage => parsing ? "正在按配置读取 Excel…" : initializing ? "正在加载基础资料…" : ErrorMessage;
        /// <summary>输入数量统计，不代表不同单位的履约总量。</summary>
        public decimal? TotalQty => Sum(line => line.Qty);
        /// <summary>合计有效 USD 金额；超出 decimal 范围返回空值。</summary>
        public decimal? TotalUsdAmount => Sum(line => line.UsdAmount);
        /// <summary>合计有效 CNY 金额，不使用汇率推算。</summary>
        public decimal? TotalCnyAmount => Sum(line => line.CnyAmount);
        /// <summary>汇总缺失资料、行校验及不完整统计提示。</summary>
        public string ValidationSummary => string.Join("\n", new[] { Header.Error, MissingOptionMessage }
            .Concat(Lines.Select((line, index) => string.IsNullOrEmpty(line.Error) ? "" : "第 " + (index + 1) + " 行：" + line.Error))
            .Concat(new[] { Lines.Any(l => !l.Qty.HasValue || !l.UsdAmount.HasValue || !l.CnyAmount.HasValue) || !TotalQty.HasValue || !TotalUsdAmount.HasValue || !TotalCnyAmount.HasValue ? "汇总不完整：空白、非法数值或超出范围的金额未计入。" : "" })
            .Where(message => !string.IsNullOrEmpty(message)));
        /// <summary>控制页面校验提示的可见性。</summary>
        public bool HasValidationError => !string.IsNullOrEmpty(ValidationSummary);
        public IAsyncRelayCommand InitializeCommand { get; }
        public IRelayCommand AddStandardLineCommand { get; }
        public IRelayCommand AddNonStandardLineCommand { get; }
        public IRelayCommand DeleteSelectedLineCommand { get; }
        public IRelayCommand CustomerSelectedCommand { get; }
        public IRelayCommand CustomerClearedCommand { get; }

        /// <summary>成功后重复 Loaded 不覆盖编辑内容；并发加载直接忽略，失败可再次重试。</summary>
        public async Task InitializeAsync()
        {
            if (IsDisposed || session.Capture().User == null || ready || initializing || parsing) return;
            var version = pageVersion;
            initializing = true; NotifyState();
            try
            {
                var result = await ReferenceData.EnsureLoadedAsync();
                await dispatcher.InvokeAsync(() =>
                {
                    if (!Accept(version)) return;
                    ready = result.Success; ErrorMessage = result.Success ? "" : result.Message;
                    RefreshOptions();
                });
            }
            catch (Exception ex)
            {
                if (Accept(version)) await dispatcher.InvokeAsync(() => { if (Accept(version)) ErrorMessage = errors.Describe(ex); });
            }
            finally
            {
                if (Accept(version)) await dispatcher.InvokeAsync(() =>
                {
                    if (!Accept(version)) return;
                    initializing = false; NotifyState();
                });
            }
        }
        /// <summary>视图只转交选中对象快照，不把 DataGrid 放入 ViewModel。</summary>
        public void SetSelectedLines(IEnumerable<SalesOrderImportLine> values)
        {
            if (IsDisposed) return;
            selectedLines.Clear(); selectedLines.AddRange(values.Where(Lines.Contains)); RefreshCommands();
        }
        /// <summary>读取、检验和保存期间拒绝普通关闭；未保存或结果未知须确认丢弃，宿主停止直接跳过。</summary>
        public bool CanClose() => !parsing && !checking && !saving && (!HasEdits || prompt.ConfirmDiscard(HasPendingSave
            ? "保存结果尚未确定，关闭将丢失原请求，不能安全重试。CommandId: " + PendingCommandId + "。仍要关闭吗？"
            : "编辑内容尚未保存，关闭将丢失。是否关闭？"));
        private bool Accept(long version) => !IsDisposed && pageVersion == version && session.Capture().Version == version && session.Capture().User != null;
        // 确认框存在嵌套消息循环，返回后再次核对会话，避免确认期间失效后仍回填。
        private void ChangeCustomer(CustomerDto candidate)
        {
            if (!IsEditable) return;
            if (candidate?.Uid == acceptedCustomer?.Uid) return;
            var version = pageVersion;
            if (Lines.Count > 0 && !prompt.ConfirmDiscard("更换或清空客户会清空全部明细，并重新填写客户默认资料。是否继续？"))
            {
                if (Accept(version)) SelectedCustomer = acceptedCustomer;
                return;
            }
            if (!Accept(version)) return;
            CancelSearches(); Lines.Clear(); acceptedCustomer = candidate; SelectedCustomer = candidate;
            Header.CustomerId = candidate?.Uid ?? Guid.Empty; Header.CustomerCode = candidate?.CustomerCode ?? "";
            Header.Currency = string.IsNullOrWhiteSpace(candidate?.Currency) ? "USD" : candidate.Currency;
            Header.PaymentMethod = candidate?.PaymentMethod ?? ""; Header.Incoterm = candidate?.Incoterm ?? "";
            Header.IncotermSortRemark = candidate?.IncotermSortRemark ?? ""; Header.IncotermLocation = candidate?.IncotermLocation ?? "";
            Header.DepositRate = candidate?.DepositRate ?? 0m; Header.LeadTimeDays = candidate?.LeadTimeDays ?? 0;
            Header.ShippedBy = candidate?.ShippedBy ?? ""; Header.ShipToName = candidate?.CustomerName ?? "";
            Header.ShipToAddress = candidate?.Address ?? ""; Header.ShipToPhone = candidate?.PhoneNumber ?? "";
            dirty = true; RefreshOptions(); NotifyState();
        }
        // 新行复制当前交期，之后修改单头不会覆盖行编辑。
        private void AddLine(bool nonStandard)
        {
            if (!IsEditable || Header.CustomerId == Guid.Empty) return;
            var line = new SalesOrderImportLine(nonStandard, Header.DeliveryDate);
            ConfigureSearch(line); Lines.Add(line);
        }
        private void DeleteLines()
        {
            if (!IsEditable) return;
            foreach (var line in selectedLines.ToArray()) Lines.Remove(line);
            selectedLines.Clear(); RefreshCommands();
        }
        // 统一管理行通知和查询清理，Clear 与批量删除也必须解绑。
        private void LinesChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (applyingParsedPart) return;
            foreach (var line in trackedLines.Where(line => !Lines.Contains(line)).ToArray())
            {
                line.PropertyChanged -= LineChanged; CancelSearch(line); trackedLines.Remove(line);
                line.SearchCommand?.NotifyCanExecuteChanged(); line.CommitProductCommand?.NotifyCanExecuteChanged(); line.ClearProductCommand?.NotifyCanExecuteChanged();
            }
            foreach (var line in Lines.Where(line => !trackedLines.Contains(line))) { trackedLines.Add(line); line.PropertyChanged += LineChanged; }
            if (!resetting) { dirty = true; InvalidateSubmission(); }
            RefreshOptions(); NotifyState();
        }
        // 只改变当前草稿，候选集重建不写回共享资料。
        private void HeaderChanged(object sender, PropertyChangedEventArgs args)
        {
            if (resetting || applyingSubmission) return;
            InvalidateSubmission(); dirty = true;
            if (new[] { "Currency", "PaymentMethod", "Incoterm", "ShippedBy" }.Contains(args.PropertyName)) RefreshOptions();
            NotifyState();
        }
        // 搜索忙碌和候选选择不是业务编辑，避免仅查询就标记草稿。
        private void LineChanged(object sender, PropertyChangedEventArgs args)
        {
            if (applyingParsedPart || applyingSubmission || resetting) return;
            if (args.PropertyName == nameof(SalesOrderImportLine.SelectedProduct) || args.PropertyName == nameof(SalesOrderImportLine.IsSearchBusy) || args.PropertyName == nameof(SalesOrderImportLine.SelectedMatchCandidate)) return;
            InvalidateSubmission(); dirty = true;
            if (args.PropertyName == nameof(SalesOrderImportLine.UnitSnapshot)) RefreshOptions();
            NotifyState();
        }
        private void ReferenceChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (!IsDisposed && Accept(pageVersion)) { RefreshOptions(); NotifyState(); }
        }
        // 身份通知可来自网络线程，清空与 UI 通知均回到 Excel STA。
        private async void SessionChanged(object sender, EventArgs args)
        {
            try { await dispatcher.InvokeAsync(ResetSession); }
            catch (OperationCanceledException) { /* 宿主已停止调度，不再恢复页面。 */ }
        }
        private void ResetSession()
        {
            if (IsDisposed || pageVersion == session.Capture().Version) return;
            resetting = true;
            try
            {
                CancelSearches(); ResetSubmission(); ResetAddressSession(); pageVersion = session.Capture().Version;
                ready = false; initializing = false;
                ParseMessage = "";
                Lines.Clear(); selectedLines.Clear(); acceptedCustomer = null; SelectedCustomer = null;
                header.PropertyChanged -= HeaderChanged; header = new SalesOrderImportHeader(); header.PropertyChanged += HeaderChanged;
                OnPropertyChanged(nameof(Header)); dirty = false;
                ErrorMessage = session.Capture().User == null ? "请先通过 Ribbon 登录。" : "账号已变更，请点击加载基础资料重新开始。";
                RefreshOptions(); NotifyState();
            }
            finally { resetting = false; }
        }
        // 非法或空输入不贡献金额，外层提示统计不完整；溢出不崩溃。
        private decimal? Sum(Func<SalesOrderImportLine, decimal?> selector)
        {
            try { return Lines.Sum(line => selector(line) ?? 0m); }
            catch (OverflowException) { return null; }
        }
        // 所有派生属性和命令统一通知，避免增删或换号后按钮状态残留。
        private void NotifyState()
        {
            foreach (var name in new[] { nameof(CanSaveDraft), nameof(HasPendingSave), nameof(PendingCommandId), nameof(CanSelectReadProfile), nameof(IsEditable), nameof(IsReadOnly), nameof(HasEdits), nameof(BusyMessage), nameof(TotalQty), nameof(TotalUsdAmount), nameof(TotalCnyAmount), nameof(ValidationSummary), nameof(HasValidationError) }) OnPropertyChanged(name);
            RefreshCommands();
            foreach (var line in Lines) { line.SearchCommand?.NotifyCanExecuteChanged(); line.CommitProductCommand?.NotifyCanExecuteChanged(); line.ClearProductCommand?.NotifyCanExecuteChanged(); }
        }
        /// <summary>关闭窗口时解绑缓存和身份通知，取消所有行查询。</summary>
        protected override void DisposeCore()
        {
            submissionLifetime.Cancel(); submissionLifetime.Dispose();
            DisposeAddresses();
            session.Changed -= SessionChanged;
            ((INotifyCollectionChanged)ReferenceData.Currencies).CollectionChanged -= ReferenceChanged;
            ((INotifyCollectionChanged)ReferenceData.DictionaryValues).CollectionChanged -= ReferenceChanged;
            ((INotifyCollectionChanged)ReferenceData.Customers).CollectionChanged -= ReferenceChanged;
            header.PropertyChanged -= HeaderChanged; Lines.CollectionChanged -= LinesChanged;
            foreach (var line in trackedLines) line.PropertyChanged -= LineChanged;
            CancelSearches(); NotifyState();
        }
    }
}
