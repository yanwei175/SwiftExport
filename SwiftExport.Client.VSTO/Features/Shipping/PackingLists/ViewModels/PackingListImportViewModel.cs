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
using SwiftExport.Contracts.SysEnums;
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
        private string savedInvoiceNosSummary;
        private readonly HashSet<PackingListImportLine> observed = new HashSet<PackingListImportLine>();
        private readonly List<PackingListImportLine> selectedLines = new List<PackingListImportLine>();
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
            CheckCommand = new AsyncRelayCommand(CheckAsync, () => CanMatchSkus && Lines.Count > 0);
            MatchAllSkusCommand = new AsyncRelayCommand(() => MatchSkusAsync(), () => CanMatchSkus && Lines.Count > 0);
            MatchLineSkuCommand = new AsyncRelayCommand<PackingListImportLine>(line => MatchSkusAsync(line), line => CanMatchSkus && line != null && Lines.Contains(line));
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsDisposed && !IsBusy && HasPermission && (pending != null || CanEdit && checkedRevision == revision && Lines.Count > 0 && Lines.All(line => line.IsCheckPassed)));
            AddStandardLineCommand = new RelayCommand(() => AddLine(false), () => CanEdit && Lines.Count < 500);
            AddNonStandardLineCommand = new RelayCommand(() => AddLine(true), () => CanEdit && Lines.Count < 500);
            DeleteLineCommand = new RelayCommand(DeleteLines, () => CanEdit && selectedLines.Any(Lines.Contains));
            ToggleNonStandardCommand = new RelayCommand<PackingListImportLine>(line => line.IsNonStandard = !line.IsNonStandard, line => CanEdit && line != null && Lines.Contains(line));
            CustomerSelectedCommand = new RelayCommand<CustomerDto>(selected => SelectedCustomer = selected, _ => CanEdit);
            CustomerClearedCommand = new RelayCommand(() => SelectedCustomer = null, () => CanEdit);
            NewCommand = new RelayCommand(NewDraft, () => !IsDisposed && !IsBusy && pending == null);
            DesignCommand = new RelayCommand(() => DesignReadProfileRequested?.Invoke(SelectedReadProfile?.Uid), () => !IsDisposed && !IsBusy && pending == null);
            foreach (var command in new IRelayCommand[] { InitializeCommand, ReadHeaderCommand, ReadLinesCommand, CheckCommand, MatchAllSkusCommand, MatchLineSkuCommand, SaveCommand, AddStandardLineCommand, AddNonStandardLineCommand, DeleteLineCommand, ToggleNonStandardCommand, CustomerSelectedCommand, CustomerClearedCommand, NewCommand, DesignCommand }) RegisterCommand(command);
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
        /// <summary>只接纳明确的缓存客户；换客户保留商业输入，撤销旧客户下的产品身份。</summary>
        public CustomerDto SelectedCustomer
        {
            get => customer;
            set
            {
                if (!CanEdit) { OnPropertyChanged(); return; }
                var valid = value == null ? null : ReferenceData.Customers.SingleOrDefault(item => item.Uid == value.Uid && item.Uid != Guid.Empty && !item.IsDeleted);
                if (customer?.Uid == valid?.Uid) { OnPropertyChanged(); return; }
                customer = valid;
                applying = true;
                try { foreach (var line in Lines) line.Invalidate(true); }
                finally { applying = false; }
                OnPropertyChanged(); Touch(); Message = "客户已变化，明细已保留，请重新匹配并检验。";
            }
        }
        private PackingListImportLine selectedLine;
        /// <summary>原生 DataGrid 当前行，不用可视行序号作为业务身份。</summary>
        public PackingListImportLine SelectedLine { get => selectedLine; set { SetProperty(ref selectedLine, value); RefreshCommands(); } }
        /// <summary>当前账号创建装箱单的内存权限，不替代服务器授权。</summary>
        public bool HasPermission => session.Capture().User?.Permissions?.Contains("Shipping.PackingList.Create") == true;
        /// <summary>检查和写入期间冻结编辑；写入结果不明时保持同命令重试。</summary>
        public bool CanEdit => ready && HasPermission && !IsDisposed && !IsBusy && pending == null && savedNumber == null;
        /// <summary>独立 SKU 匹配只要求当前明确客户，不要求完整单头。</summary>
        public bool CanMatchSkus => CanEdit && customer != null && customer.Uid != Guid.Empty && !customer.IsDeleted;
        /// <summary>保存后回显服务端箱单号，未保存时明确由系统生成。</summary>
        public string PackingListNo => savedNumber ?? "保存后生成";
        /// <summary>保存前按集合顺序预览行发票号去重汇总；保存后显示服务器返回的只读汇总，不进入地址配置或保存输入。</summary>
        public string InvoiceNosSummary => savedInvoiceNosSummary ?? string.Join(", ", Lines.Select(line => (line.InvoiceNo ?? "").Trim(' '))
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal));
        /// <summary>当前启用的单位候选，非标允许填写字典外原文。</summary>
        public IReadOnlyList<string> Units => ReferenceData.GetDictionaryValues(DictCodes.Unit).Select(item => item.DataNameEn).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
        /// <summary>显示读取及检查反馈；可选包装问题不阻断型号读取。</summary>
        public string Message { get => message; private set => SetProperty(ref message, value); }
        /// <summary>行数和逐行包装暂计，不推断拼箱关系，不参与保存门禁。</summary>
        public string Summary => BuildSummary();
        /// <summary>手工新增完成集合赋号后交给视图定位，读取不逐行抢焦点。</summary>
        public event Action<PackingListImportLine> LineAdded;
        /// <summary>打开共享配置侧栏的受控回调。</summary>
        public event Action<Guid?> DesignReadProfileRequested;
        /// <summary>按需加载参考资料与读取配置。</summary>
        public IAsyncRelayCommand InitializeCommand { get; }
        /// <summary>读取单头，确认后只替换已读取字段，保留未配置值。</summary>
        public IAsyncRelayCommand ReadHeaderCommand { get; }
        /// <summary>读取明细，确认后仅替换明细。</summary>
        public IAsyncRelayCommand ReadLinesCommand { get; }
        /// <summary>通过后端只读检查获取真实产品身份。</summary>
        public IAsyncRelayCommand CheckCommand { get; }
        /// <summary>独立匹配全部 SKU，仍须整单检验后保存。</summary>
        public IAsyncRelayCommand MatchAllSkusCommand { get; }
        /// <summary>只匹配命令参数指定的行，不覆盖其他行。</summary>
        public IAsyncRelayCommand<PackingListImportLine> MatchLineSkuCommand { get; }
        /// <summary>创建未关联草稿；未知结果重试冻结命令。</summary>
        public IAsyncRelayCommand SaveCommand { get; }
        /// <summary>添加标准行，客户型号填写后使用现有检查接口定位产品。</summary>
        public IRelayCommand AddStandardLineCommand { get; }
        /// <summary>添加用户明确标记的非标行。</summary>
        public IRelayCommand AddNonStandardLineCommand { get; }
        /// <summary>删除选中行，使检查结果失效。</summary>
        public IRelayCommand DeleteLineCommand { get; }
        /// <summary>显式切换行类型，不把失败匹配自动转成非标。</summary>
        public IRelayCommand<PackingListImportLine> ToggleNonStandardCommand { get; }
        /// <summary>客户控件明确提交后接纳参数中的客户身份。</summary>
        public IRelayCommand<CustomerDto> CustomerSelectedCommand { get; }
        /// <summary>客户控件清除后撤销身份与全部匹配资格。</summary>
        public IRelayCommand CustomerClearedCommand { get; }
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
                if (dirty && !prompt.ConfirmDiscard(detail ? "替换全部明细，保留单头，是否继续？" : "替换已配置单头字段，保留未配置值、客户和明细，是否继续？")) return;
                if (!Accept(version) || edit != revision) return;
                if (detail) { Lines.Clear(); foreach (var line in parsed.Lines) Lines.Add(line); }
                else if (parsed.Header != null)
                {
                    // 空单元格也是明确的读取结果；未配置字段不从临时模型的默认空值覆盖原输入。
                    var keys = snapshot.Rows.SelectMany(row => row).Select(cell => cell.FieldKey).ToArray();
                    applying = true;
                    try
                    {
                        foreach (var field in definition.Fields.Where(field => field.ModelType == definition.HeaderType && keys.Contains(field.FieldKey)))
                            field.Assign(Header, field.ModelType.GetProperty(field.PropertyName).GetValue(parsed.Header));
                    }
                    finally { applying = false; }
                }
                Touch(); Message = parsed.Message;
            });
        });
        /// <summary>以当前集合或指定匹配行组装请求，不按 DataGrid 排序后的可视位置保存。</summary>
        private CheckPackingListImportRequest Request(IEnumerable<PackingListImportLine> rows = null) => new CheckPackingListImportRequest
        {
            Header = new PackingListDraftHeaderInput { CustomerId = customer?.Uid ?? Guid.Empty,
                CustomerNameSnapshot = Header.CustomerName, CustomerAddressSnapshot = Header.CustomerAddress,
                AttentionSnapshot = Header.Attention, InvoiceDate = Header.InvoiceDate ?? default(DateTime), ContainerNo = Header.ContainerNo,
                SealNo = Header.SealNo, ShippingLocationSnapshot = Header.ShippingLocation, Etd = Header.Etd, Eta = Header.Eta,
                Note = Header.Note },
            Lines = (rows ?? Lines).Select((line, i) => line.ToInput(i + 1)).ToList()
        };
        private void RequireInput()
        {
            if (customer == null || !Header.InvoiceDate.HasValue || Lines.Count == 0 || Lines.Count > 500 || Lines.Any(x => x.HasInvalidNumber))
                throw new InvalidOperationException("请选择客户、填写日期及有效数量；包装数字不能是非法文字。");
        }
        /// <summary>整单检验接纳完整同行结果，保存资格同时要求本地数值有效与后端通过。</summary>
        private Task CheckAsync() => RunAsync(async (version, token) =>
        {
            checkedRevision = -1; RequireInput(); var edit = revision;
            var rows = Lines.ToArray(); var rowVersions = rows.Select(line => line.EditRevision).ToArray(); var customerId = customer.Uid;
            var result = await checker.CheckAsync(Request(), token);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            await dispatcher.InvokeAsync(() =>
            {
                if (!CanAcceptRows(version, edit, customerId, rows, rowVersions)) return;
                RequireCompleteResult(result.Data, rows);
                applying = true;
                try { foreach (var row in result.Data.Lines) rows.Single(x => x.Uid == row.ClientLineId).Apply(row); }
                finally { applying = false; }
                var issues = result.Data.Issues ?? new List<PackingListImportIssueDto>();
                var canSave = result.Data.CanSave && rows.All(line => line.IsCheckPassed) && !issues.Any(i => !i.RequiresConfirmation || !i.Acknowledged);
                checkedRevision = canSave ? revision : -1;
                Message = canSave ? "检查通过，可保存未关联草稿。" : "检查未通过，请处理行反馈后重新检查。";
                ErrorMessage = string.Join("；", issues.Select(x => x.Message)); Notify();
            });
        });
        /// <summary>仅匹配产品结果；单头未填写或数值输入未完成不阻止定位 SKU，但不能授予保存资格。</summary>
        private Task MatchSkusAsync(PackingListImportLine target = null)
        {
            if (!CanMatchSkus || target != null && !Lines.Contains(target)) return Task.CompletedTask;
            var rows = target == null ? Lines.ToArray() : new[] { target };
            if (rows.Length == 0 || rows.Length > 500) return Task.CompletedTask;
            return RunAsync(async (version, token) =>
            {
                checkedRevision = -1;
                var edit = revision; var customerId = customer.Uid; var rowVersions = rows.Select(line => line.EditRevision).ToArray();
                Message = "正在匹配 SKU…";
                var result = await checker.CheckAsync(Request(rows), token);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                await dispatcher.InvokeAsync(() =>
                {
                    if (!CanAcceptRows(version, edit, customerId, rows, rowVersions)) return;
                    RequireCompleteResult(result.Data, rows);
                    applying = true;
                    try { foreach (var row in result.Data.Lines) rows.Single(line => line.Uid == row.ClientLineId).Apply(row); }
                    finally { applying = false; }
                    Message = "SKU 匹配完成：" + rows.Count(line => line.IsCheckPassed) + "/" + rows.Length + " 行校验通过。保存前仍须整单检验。";
                    Notify();
                });
            });
        }
        /// <summary>整单修订和逐行身份同时核对，删行、换客户、旧会话及释放后不回填。</summary>
        private bool CanAcceptRows(long version, long edit, Guid customerId, PackingListImportLine[] rows, long[] rowVersions)
            => Accept(version) && edit == revision && customer?.Uid == customerId
                && rows.Where((line, i) => !Lines.Contains(line) || line.EditRevision != rowVersions[i]).Any() == false;
        /// <summary>拒绝缺行、重复行或混入其他请求的响应，避免部分成功误授予检查资格。</summary>
        private static void RequireCompleteResult(CheckPackingListImportResponse result, PackingListImportLine[] rows)
        {
            if (result?.Lines == null || result.Lines.Count != rows.Length || result.Lines.Any(line => line == null)
                || result.Lines.Select(line => line.ClientLineId).Distinct().Count() != rows.Length
                || result.Lines.Any(line => !rows.Any(row => row.Uid == line.ClientLineId)))
                throw new InvalidOperationException("检查返回行身份不完整。");
        }
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
                savedNumber = result.Data.Detail.Header.PackingListNo; savedInvoiceNosSummary = result.Data.Detail.Header.InvoiceNosSummary;
                pending = null; dirty = false;
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
        /// <summary>同步编辑门禁、服务端编号和只读汇总，并刷新页面命令资格。</summary>
        private void Notify() { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanMatchSkus)); OnPropertyChanged(nameof(PackingListNo)); OnPropertyChanged(nameof(InvoiceNosSummary)); OnPropertyChanged(nameof(Units)); OnPropertyChanged(nameof(Summary)); RefreshCommands(); }
        /// <summary>区分身份变化与商业输入修订；接纳服务端结果时不把通知当成新编辑。</summary>
        private void Edited(object sender, PropertyChangedEventArgs args)
        {
            if (applying || args.PropertyName == nameof(PackingListImportLine.MatchMessage) || args.PropertyName == nameof(PackingListImportLine.Sku)
                || args.PropertyName == nameof(PackingListImportLine.LinkSku) || args.PropertyName == nameof(PackingListImportLine.Candidates)
                || args.PropertyName == nameof(PackingListImportLine.LineNumber)) return;
            if (sender is PackingListImportLine line)
            {
                applying = true;
                try
                {
                    if (args.PropertyName == nameof(PackingListImportLine.CustomerSku) || args.PropertyName == nameof(PackingListImportLine.OriginalLinkSku)
                        || args.PropertyName == nameof(PackingListImportLine.IsNonStandard)) line.AcceptLinkSkuMismatch = false;
                    line.Invalidate(args.PropertyName == nameof(PackingListImportLine.CustomerSku) || args.PropertyName == nameof(PackingListImportLine.IsNonStandard));
                    line.NotifyInputState();
                }
                finally { applying = false; }
            }
            Touch();
        }
        private void Touch() { revision++; checkedRevision = -1; dirty = true; Notify(); }
        /// <summary>新行按当前最大行号赋号，解除删除行订阅，并使整单检验失效。</summary>
        private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            var number = Lines.Select(line => line.LineNumber).DefaultIfEmpty(0).Max();
            foreach (var line in Lines.Where(line => line.LineNumber == 0)) line.AssignLineNumber(++number);
            foreach (var line in observed.Where(x => !Lines.Contains(x)).ToArray()) { line.PropertyChanged -= Edited; observed.Remove(line); }
            foreach (var line in Lines.Where(x => !observed.Contains(x))) { observed.Add(line); line.PropertyChanged += Edited; }
            selectedLines.RemoveAll(line => !Lines.Contains(line));
            Touch();
        }
        /// <summary>视图仅桥接多选对象，不把 DataGrid 或可视索引传入业务模型。</summary>
        public void SetSelectedLines(IEnumerable<PackingListImportLine> rows)
        {
            selectedLines.Clear(); selectedLines.AddRange(rows.Where(Lines.Contains).Distinct()); RefreshCommands();
        }
        /// <summary>手工新增独立导入行，并在集合绑定后通知视图定位。</summary>
        private void AddLine(bool nonStandard)
        {
            var line = new PackingListImportLine { IsNonStandard = nonStandard };
            Lines.Add(line); LineAdded?.Invoke(line);
        }
        /// <summary>按对象身份删除多选行，排序不改变删除目标。</summary>
        private void DeleteLines() { foreach (var line in selectedLines.ToArray()) Lines.Remove(line); selectedLines.Clear(); RefreshCommands(); }
        /// <summary>逐行暂计包装，缺失不推算、不分摊，单行或总计溢出都不会使页面崩溃。</summary>
        private string BuildSummary()
        {
            decimal qty = 0, cartons = 0, gross = 0, volume = 0;
            bool quantityIncomplete = false, packingIncomplete = false;
            foreach (var line in Lines)
            {
                var quantity = line.Qty;
                if (!quantity.HasValue || quantity <= 0) { quantityIncomplete = packingIncomplete = true; continue; }
                if (!TryAdd(ref qty, quantity.Value)) quantityIncomplete = true;
                var pcs = PackingListImportLine.Number(line.PcsPerPackageText);
                if (!pcs.HasValue || pcs <= 0) { packingIncomplete = true; continue; }
                decimal boxes;
                try { boxes = quantity.Value / pcs.Value; }
                catch (OverflowException) { packingIncomplete = true; continue; }
                if (!TryAdd(ref cartons, boxes)) packingIncomplete = true;
                if (!TryAddPackaging(ref gross, boxes, line.GwPerPackageText)) packingIncomplete = true;
                if (!TryAddPackaging(ref volume, boxes, line.CbmPerPackageText)) packingIncomplete = true;
            }
            return Lines.Count + " 行  总数量：" + qty.ToString("N2") + (quantityIncomplete ? "（输入不完整）" : "")
                + "  总箱数：" + cartons.ToString("N2") + "  总毛重：" + gross.ToString("N2") + "  总体积：" + volume.ToString("N3")
                + "（按当前行资料暂计）" + (packingIncomplete ? " 包装汇总不完整" : "");
        }
        /// <summary>保留此前有效合计，超出 decimal 范围的贡献不计入。</summary>
        private static bool TryAdd(ref decimal total, decimal value)
        {
            try { total += value; return true; } catch (OverflowException) { return false; }
        }
        /// <summary>可选毛重／体积未知或非法时不计入，相应汇总标记不完整。</summary>
        private static bool TryAddPackaging(ref decimal total, decimal boxes, string text)
        {
            var value = PackingListImportLine.Number(text);
            if (!value.HasValue || value < 0) return false;
            try { return TryAdd(ref total, boxes * value.Value); } catch (OverflowException) { return false; }
        }
        /// <summary>确认后清空当前输入及上次服务器汇总，恢复新导入的本地预览。</summary>
        private void NewDraft()
        {
            if (dirty && !prompt.ConfirmDiscard("清空内存编辑并开始新导入，是否继续？")) return;
            Lines.Clear(); Header = new PackingListImportHeader { InvoiceDate = DateTime.Today }; savedNumber = null; savedInvoiceNosSummary = null; dirty = false; checkedRevision = -1; Message = "新导入，请重新读取或手工填写。"; Notify();
        }
        /// <summary>普通关闭保护未保存或结果不明的输入，插件退出由宿主跳过。</summary>
        public bool CanClose() => !IsBusy && pending == null && (!dirty || prompt.ConfirmDiscard("未保存编辑仅存在内存，确认关闭？"));
        /// <summary>会话变化取消旧任务并清空输入、系统编号和服务器汇总，不向新账号展示旧数据。</summary>
        private async void SessionChanged(object sender, EventArgs args)
        {
            var changedVersion = session.Capture().Version;
            try { await dispatcher.InvokeAsync(() => { if (IsDisposed || session.Capture().Version != changedVersion) return; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource(); ready = false; pending = null; savedNumber = null; savedInvoiceNosSummary = null; customer = null; Lines.Clear(); Header = new PackingListImportHeader(); ReadProfiles.Clear(); SelectedReadProfile = null; IsBusy = false; dirty = false; OnPropertyChanged(nameof(SelectedCustomer)); Message = "账号变化，原导入数据已清空；请重新加载。"; Notify(); }); }
            catch (OperationCanceledException) { }
        }
        /// <summary>取消页面任务并解绑会话及模型事件。</summary>
        protected override void DisposeCore()
        {
            lifetime.Cancel(); lifetime.Dispose(); session.Changed -= SessionChanged; Lines.CollectionChanged -= CollectionChanged; header.PropertyChanged -= Edited;
            foreach (var line in observed) line.PropertyChanged -= Edited; observed.Clear(); selectedLines.Clear(); DesignReadProfileRequested = null; LineAdded = null;
        }
    }
}
