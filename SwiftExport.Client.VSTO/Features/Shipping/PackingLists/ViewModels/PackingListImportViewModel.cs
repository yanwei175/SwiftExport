using SwiftExport.Contracts.DTOs.Errors;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Features.Sales.Orders;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Services;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels
{
    /// <summary>按地址读取、检查和创建未关联草稿；不解析拼箱，不在 Excel 关联来源。</summary>
    public sealed class PackingListImportViewModel : ViewModelBaseEx
    {
        private readonly SessionService session;
        private readonly IUiDispatcher dispatcher;
        private readonly IExcelAddressReader reader;
        private readonly ExcelReadProfileService profiles;
        private readonly ExcelImportDefinition definition;
        private readonly PackingListAddressParser parser;
        private readonly IForExcelPackingListApi checker;
        private readonly IPackingListApi api;
        private readonly ISalesOrderImportPrompt prompt;
        private readonly ApiCallRunner errors;
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private long revision, checkedRevision = -1;
        private bool applying, ready, dirty;
        private CreatePackingListDraftCommand pending;
        private PackingListImportHeader header;
        private CustomerDto customer;
        private string message = "先选择客户和读取配置；保存后到客户端关联来源。";
        private string savedNumber;
        private readonly HashSet<PackingListImportLine> observed = new HashSet<PackingListImportLine>();
        /// <summary>借用应用服务，页面仅拥有取消源及自己的订阅。</summary>
        public PackingListImportViewModel(SessionService session, IUiDispatcher dispatcher, IExcelAddressReader reader,
            ExcelReadProfileService profiles, ExcelImportRegistry registry, IReferenceDataCacheService cache,
            IForExcelPackingListApi checker, IPackingListApi api, ISalesOrderImportPrompt prompt, ApiCallRunner errors)
        {
            this.session = session; this.dispatcher = dispatcher; this.reader = reader; this.profiles = profiles;
            this.checker = checker; this.api = api; this.prompt = prompt; this.errors = errors;
            ReferenceData = cache; definition = registry.Find("PackingList"); parser = new PackingListAddressParser(registry);
            Header = new PackingListImportHeader { InvoiceDate = DateTime.Today };
            Lines.CollectionChanged += CollectionChanged; session.Changed += SessionChanged;
            InitializeCommand = new AsyncRelayCommand(LoadProfilesAsync, () => !IsDisposed && !IsBusy && pending == null);
            ReadHeaderCommand = new AsyncRelayCommand(() => ReadAsync(false), () => CanEdit && SelectedReadProfile != null);
            ReadLinesCommand = new AsyncRelayCommand(() => ReadAsync(true), () => CanEdit && SelectedReadProfile != null);
            CheckCommand = new AsyncRelayCommand(CheckAsync, () => CanEdit && SelectedCustomer != null && Lines.Count > 0);
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsDisposed && !IsBusy && HasPermission && (pending != null || CanEdit && checkedRevision == revision));
            AddLineCommand = new RelayCommand(() => Lines.Add(new PackingListImportLine()), () => CanEdit);
            DeleteLineCommand = new RelayCommand(() => { if (SelectedLine != null) Lines.Remove(SelectedLine); }, () => CanEdit && SelectedLine != null);
            NewCommand = new RelayCommand(NewDraft, () => !IsDisposed && !IsBusy && pending == null);
            DesignCommand = new RelayCommand(() => DesignReadProfileRequested?.Invoke(SelectedReadProfile?.Uid), () => !IsDisposed && !IsBusy && pending == null);
            foreach (var command in new IRelayCommand[] { InitializeCommand, ReadHeaderCommand, ReadLinesCommand, CheckCommand, SaveCommand, AddLineCommand, DeleteLineCommand, NewCommand, DesignCommand }) RegisterCommand(command);
        }
        /// <summary>按需缓存，只借用 DTO，不直接编辑缓存。</summary>
        public IReferenceDataCacheService ReferenceData { get; }
        /// <summary>当前内存单头，替换时同步通知及订阅。</summary>
        public PackingListImportHeader Header { get => header; private set { if (header != null) header.PropertyChanged -= Edited; header = value; header.PropertyChanged += Edited; OnPropertyChanged(); } }
        /// <summary>导入行，直接通知集合，不使用单据状态机。</summary>
        public ObservableCollection<PackingListImportLine> Lines { get; } = new ObservableCollection<PackingListImportLine>();
        /// <summary>从服务器按需读取的配置列表，不作为应用级缓存。</summary>
        public ObservableCollection<ExcelReadProfileListItemDto> ReadProfiles { get; } = new ObservableCollection<ExcelReadProfileListItemDto>();
        private ExcelReadProfileListItemDto selectedReadProfile;
        /// <summary>本次读取配置，每次读取重新获取完整详情。</summary>
        public ExcelReadProfileListItemDto SelectedReadProfile { get => selectedReadProfile; set { if (SetProperty(ref selectedReadProfile, value)) RefreshCommands(); } }
        /// <summary>客户变化使全部产品检查失效；取消时保留旧客户。</summary>
        public CustomerDto SelectedCustomer
        {
            get => customer;
            set
            {
                if (customer?.Uid == value?.Uid) return;
                if (!CanEdit) { OnPropertyChanged(); return; }
                var changingSession = session.Capture().Version;
                if (Lines.Count > 0 && !prompt.ConfirmDiscard("更换客户将清空全部明细，是否继续？")) { OnPropertyChanged(); return; }
                if (!Accept(changingSession)) return;
                customer = value; Lines.Clear(); OnPropertyChanged(); Touch();
            }
        }
        private PackingListImportLine selectedLine;
        /// <summary>原生 DataGrid 当前行，用于删除。</summary>
        public PackingListImportLine SelectedLine { get => selectedLine; set { SetProperty(ref selectedLine, value); RefreshCommands(); } }
        /// <summary>当前账号创建装箱单的内存权限，不替代服务器授权。</summary>
        public bool HasPermission => session.Capture().User?.Permissions?.Contains("Shipping.PackingList.Create") == true;
        /// <summary>检查和写入期间冻结编辑；写入结果不明时保持同命令重试。</summary>
        public bool CanEdit => ready && HasPermission && !IsDisposed && !IsBusy && pending == null && savedNumber == null;
        /// <summary>显示读取及检查反馈；可选包装问题不阻断型号读取。</summary>
        public string Message { get => message; private set => SetProperty(ref message, value); }
        /// <summary>数量仅统计输入；存在非法数量时明确提示。</summary>
        public string Summary => Lines.Count + " 行，数量 " + Lines.Sum(x => x.Qty ?? 0) + (Lines.Any(x => !x.Qty.HasValue || x.Qty <= 0) ? "（输入不完整）" : "");
        /// <summary>打开共享配置侧栏的受控回调。</summary>
        public event Action<Guid?> DesignReadProfileRequested;
        /// <summary>按需加载参考资料与读取配置。</summary>
        public IAsyncRelayCommand InitializeCommand { get; }
        /// <summary>读取单头，确认后仅替换单头。</summary>
        public IAsyncRelayCommand ReadHeaderCommand { get; }
        /// <summary>读取明细，确认后仅替换明细。</summary>
        public IAsyncRelayCommand ReadLinesCommand { get; }
        /// <summary>通过后端只读检查获取真实产品身份。</summary>
        public IAsyncRelayCommand CheckCommand { get; }
        /// <summary>创建未关联草稿；未知结果重试冻结命令。</summary>
        public IAsyncRelayCommand SaveCommand { get; }
        /// <summary>添加可手工修正的导入行。</summary>
        public IRelayCommand AddLineCommand { get; }
        /// <summary>删除当前选中行，使检查结果失效。</summary>
        public IRelayCommand DeleteLineCommand { get; }
        /// <summary>确认后开始新的内存导入。</summary>
        public IRelayCommand NewCommand { get; }
        /// <summary>打开共享地址配置侧栏。</summary>
        public IRelayCommand DesignCommand { get; }
        /// <summary>首次使用或失败后重试；重复 Loaded 不重置编辑。</summary>
        public Task InitializeAsync() => ready || IsBusy || IsDisposed ? Task.CompletedTask : LoadProfilesAsync();
        // 用户显式刷新配置不会重置当前单头和明细。
        private Task LoadProfilesAsync() => RunAsync(async (version, token) =>
        {
            var cached = await ReferenceData.EnsureLoadedAsync();
            if (!cached.Success) throw new InvalidOperationException(cached.Message);
            var list = await profiles.ListAsync("PackingList", token);
            if (!list.Success) throw new InvalidOperationException(list.Message);
            await dispatcher.InvokeAsync(() => { if (!Accept(version)) return; ReadProfiles.Clear(); foreach (var item in list.Data) ReadProfiles.Add(item); ready = true; Notify(); });
        });
        // 捕获会话及读取前修订，避免旧账号、关闭页面或读取期间的新编辑被覆盖。
        private Task ReadAsync(bool detail) => RunAsync(async (version, token) =>
        {
            var edit = revision; var chosen = SelectedReadProfile;
            var result = await profiles.GetAsync(chosen.Uid, token);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            var issues = ExcelAddressRules.Validate(result.Data, definition);
            if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
            var snapshot = await reader.ReadAsync(result.Data, detail, token);
            var parsed = await Task.Run(() => parser.Parse(snapshot), token);
            if (detail && parsed.Lines.Count > 500) throw new InvalidOperationException("装箱单最多 500 行，请缩小读取范围。");
            await dispatcher.InvokeAsync(() =>
            {
                if (!Accept(version) || edit != revision) return;
                if (dirty && !prompt.ConfirmDiscard(detail ? "替换全部明细，保留单头，是否继续？" : "替换单头，保留客户和明细，是否继续？")) return;
                if (!Accept(version) || edit != revision) return;
                if (detail) { Lines.Clear(); foreach (var line in parsed.Lines) Lines.Add(line); }
                else if (parsed.Header != null) Header = parsed.Header;
                Touch(); Message = parsed.Message;
            });
        });
        private CheckPackingListImportRequest Request() => new CheckPackingListImportRequest
        {
            Header = new PackingListDraftHeaderInput { CustomerId = customer?.Uid ?? Guid.Empty,
                CustomerNameSnapshot = Header.CustomerName, CustomerAddressSnapshot = Header.CustomerAddress,
                AttentionSnapshot = Header.Attention, InvoiceDate = Header.InvoiceDate ?? default(DateTime), ContainerNo = Header.ContainerNo,
                SealNo = Header.SealNo, ShippingLocationSnapshot = Header.ShippingLocation, Etd = Header.Etd, Eta = Header.Eta,
                Note = string.IsNullOrWhiteSpace(Header.ExcelDocumentNo) ? Header.Note : (Header.Note + "\nExcel 装箱单号：" + Header.ExcelDocumentNo).Trim() },
            Lines = Lines.Select((line, i) => line.ToInput(i + 1)).ToList()
        };
        private void RequireInput()
        {
            if (customer == null || !Header.InvoiceDate.HasValue || Lines.Count == 0 || Lines.Count > 500 || Lines.Any(x => x.HasInvalidNumber))
                throw new InvalidOperationException("请选择客户、填写日期及有效数量；包装数字不能是非法文字。");
        }
        private Task CheckAsync() => RunAsync(async (version, token) =>
        {
            checkedRevision = -1; RequireInput(); var edit = revision;
            var result = await checker.CheckAsync(Request(), token);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            await dispatcher.InvokeAsync(() =>
            {
                if (!Accept(version) || edit != revision) return;
                if (result.Data == null || result.Data.Lines.Count != Lines.Count || result.Data.Lines.Select(x => x.ClientLineId).Distinct().Count() != Lines.Count
                    || result.Data.Lines.Any(x => !Lines.Any(line => line.Uid == x.ClientLineId))) throw new InvalidOperationException("检查返回行身份不完整。");
                applying = true;
                try { foreach (var row in result.Data.Lines) Lines.Single(x => x.Uid == row.ClientLineId).Apply(row); }
                finally { applying = false; }
                checkedRevision = result.Data.CanSave ? revision : -1;
                Message = result.Data.CanSave ? "检查通过，可保存未关联草稿。" : "检查未通过，请处理行反馈后重新检查。";
                ErrorMessage = string.Join("；", result.Data.Issues.Select(x => x.Message)); Notify();
            });
        });
        /// <summary>创建箱单草稿并保留未决命令；只有明确业务拒绝或有效成功才解除原重试状态。</summary>
        private Task SaveAsync() => RunAsync(async (version, token) =>
        {
            if (pending == null)
            {
                RequireInput(); if (checkedRevision != revision) throw new InvalidOperationException("编辑后必须重新检查。");
                var request = Request(); pending = new CreatePackingListDraftCommand { CommandId = Guid.NewGuid(), Uid = Guid.NewGuid(), Header = request.Header, Lines = request.Lines };
            }
            var result = await api.CreateAsync(pending, token);
            await dispatcher.InvokeAsync(() =>
            {
                if (!Accept(version)) return;
                if (!result.Success)
                {
                    // 明确失败可修改；结果不明只能重试原命令，避免再次建单。
                    if (result.Error?.Code != null && !new[] { ServiceErrorCodes.OutcomeUnknown, ServiceErrorCodes.Unexpected, ServiceErrorCodes.ServiceUnavailable, ServiceErrorCodes.CommandBusy, ServiceErrorCodes.CommandConflict }.Contains(result.Error.Code)) { pending = null; checkedRevision = -1; }
                    ErrorMessage = result.Message; Notify(); return;
                }
                if (result.Data?.Detail?.Header == null || result.Data.Uid != pending.Uid) { ErrorMessage = "保存结果不完整，请重试原命令。"; return; }
                savedNumber = result.Data.Detail.Header.PackingListNo; pending = null; dirty = false;
                Message = "已保存 " + savedNumber + "。请到 .NET 10 客户端关联来源；包装资料随后整理。"; Notify();
            });
        });
        // 每个异步边界统一接纳到 Excel STA，异常不丢失原始输入。
        private async Task RunAsync(Func<long, CancellationToken, Task> action)
        {
            if (IsBusy || IsDisposed || session.Capture().User == null) return;
            var version = session.Capture().Version; var token = lifetime.Token; IsBusy = true; ErrorMessage = ""; Notify();
            try { await action(version, token); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await dispatcher.InvokeAsync(() => { if (Accept(version)) { if (ex is Refit.ApiException apiError && ((int)apiError.StatusCode == 401 || (int)apiError.StatusCode == 403)) { pending = null; checkedRevision = -1; } ErrorMessage = ex is InvalidOperationException ? ex.Message : errors.Describe(ex); if (pending != null) ErrorMessage += " 保存结果未确认，请用保存按钮重试原命令。"; } });
            }
            finally { await dispatcher.InvokeAsync(() => { if (Accept(version)) { IsBusy = false; Notify(); } }); }
        }
        private bool Accept(long version) => !IsDisposed && session.Capture().Version == version;
        private void Notify() { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(Summary)); RefreshCommands(); }
        private void Edited(object sender, PropertyChangedEventArgs args)
        {
            if (applying || args.PropertyName == nameof(PackingListImportLine.MatchMessage) || args.PropertyName == nameof(PackingListImportLine.Sku)
                || args.PropertyName == nameof(PackingListImportLine.LinkSku) || args.PropertyName == nameof(PackingListImportLine.Candidates)) return;
            if (sender is PackingListImportLine line)
            {
                applying = true;
                try
                {
                    if (args.PropertyName == nameof(PackingListImportLine.CustomerSku) || args.PropertyName == nameof(PackingListImportLine.OriginalLinkSku)
                        || args.PropertyName == nameof(PackingListImportLine.IsNonStandard)) line.AcceptLinkSkuMismatch = false;
                    if (args.PropertyName == nameof(PackingListImportLine.CustomerSku) || args.PropertyName == nameof(PackingListImportLine.IsNonStandard)) line.SelectedCandidate = null;
                    line.Invalidate();
                }
                finally { applying = false; }
            }
            Touch();
        }
        private void Touch() { revision++; checkedRevision = -1; dirty = true; Notify(); }
        private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            foreach (var line in observed.Where(x => !Lines.Contains(x)).ToArray()) { line.PropertyChanged -= Edited; observed.Remove(line); }
            foreach (var line in Lines.Where(x => !observed.Contains(x))) { observed.Add(line); line.PropertyChanged += Edited; }
            Touch();
        }
        private void NewDraft()
        {
            if (dirty && !prompt.ConfirmDiscard("清空内存编辑并开始新导入，是否继续？")) return;
            Lines.Clear(); Header = new PackingListImportHeader { InvoiceDate = DateTime.Today }; savedNumber = null; dirty = false; checkedRevision = -1; Message = "新导入，请重新读取或手工填写。"; Notify();
        }
        /// <summary>普通关闭保护未保存或结果不明的输入，插件退出由宿主跳过。</summary>
        public bool CanClose() => !IsBusy && pending == null && (!dirty || prompt.ConfirmDiscard("未保存编辑仅存在内存，确认关闭？"));
        private async void SessionChanged(object sender, EventArgs args)
        {
            var changedVersion = session.Capture().Version;
            try { await dispatcher.InvokeAsync(() => { if (IsDisposed || session.Capture().Version != changedVersion) return; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource(); ready = false; pending = null; savedNumber = null; customer = null; Lines.Clear(); Header = new PackingListImportHeader(); ReadProfiles.Clear(); SelectedReadProfile = null; IsBusy = false; dirty = false; OnPropertyChanged(nameof(SelectedCustomer)); Message = "账号变化，原导入数据已清空；请重新加载。"; Notify(); }); }
            catch (OperationCanceledException) { }
        }
        /// <summary>取消页面任务并解绑会话及模型事件。</summary>
        protected override void DisposeCore()
        {
            lifetime.Cancel(); lifetime.Dispose(); session.Changed -= SessionChanged; Lines.CollectionChanged -= CollectionChanged; header.PropertyChanged -= Edited;
            foreach (var line in observed) line.PropertyChanged -= Edited; observed.Clear(); DesignReadProfileRequested = null;
        }
    }
}
