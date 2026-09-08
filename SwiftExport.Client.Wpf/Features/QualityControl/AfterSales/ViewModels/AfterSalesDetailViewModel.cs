using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;

/// <summary>售后详情文档；维护投诉事实并执行实物确认、撤回和生命周期命令。</summary>
public sealed partial class AfterSalesDetailViewModel : ViewModelBaseEx, IDocumentTabViewModel, IDocumentTabInitializable, IDisposable
{
    public const string RouteKey = "quality-control-after-sales-detail";
    private readonly IAfterSalesApi _api;
    private readonly IAfterSalesFeatureService _feature;
    private readonly IApiCallRunner _runner;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IAfterSalesReasonDialogService _reasonDialog;
    private readonly IMessageService _messages;
    private readonly ICurrentUserService _currentUser;
    private readonly List<AfterSalesLineModel> _deletedLines = [];
    private readonly Dictionary<string, (string Fingerprint, Guid CommandId)> _pendingCommands = [];
    private CancellationTokenSource? _loadCancellation;
    private bool _hasServerIdentity;
    private bool _loading;
    private bool _disposed;

    /// <summary>创建售后详情文档并接入业务命令、对话框、导航和权限服务。</summary>
    public AfterSalesDetailViewModel(IAfterSalesApi api, IAfterSalesFeatureService feature, IApiCallRunner runner,
        INavigationService navigation, IDialogService dialogs, IAfterSalesReasonDialogService reasonDialog,
        IMessageService messages, ICurrentUserService currentUser)
    {
        _api = api; _feature = feature; _runner = runner; _navigation = navigation; _dialogs = dialogs;
        _reasonDialog = reasonDialog; _messages = messages; _currentUser = currentUser;
        Title = "售后服务单-新建";
        Header.PropertyChanged += OnEditChanged;
        Lines.CollectionChanged += (_, _) => UpdateDirtyState();
        RegisterBusyAwareCommand(SaveCommand); RegisterBusyAwareCommand(ConfirmDemandCommand);
        RegisterBusyAwareCommand(CancelDemandConfirmationCommand); RegisterBusyAwareCommand(CloseCommand);
        RegisterBusyAwareCommand(ReopenCommand); RegisterBusyAwareCommand(VoidCommand);
        RegisterBusyAwareCommand(AddLineCommand); RegisterBusyAwareCommand(DeleteLineCommand);
        RegisterBusyAwareCommand(ReloadCommand); RegisterBusyAwareCommand(RefreshLookupsCommand);
    }

    public AfterSalesHeaderEditModel Header { get; } = new();
    public ObservableCollection<AfterSalesLineModel> Lines { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> Customers { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> Owners { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> Suppliers { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> Products { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> SalesOrderLines { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> Colors { get; } = [];
    public ObservableCollection<AfterSalesLookupDto> QualityCategories { get; } = [];
    public string DocumentTitle => string.IsNullOrWhiteSpace(Header.AfterSalesNo) ? "售后服务单-新建" : Header.AfterSalesNo;
    public bool HasUnsavedChanges => IsDirty;
    public bool IsPersisted => _hasServerIdentity;
    public bool HasAccess => _currentUser.HasPermission(PermissionKeys.ModuleQualityControlAccess);
    public string DocumentStatusText => DocumentStatus switch { DocumentStatusEnum.Closed => "已结案", DocumentStatusEnum.Voided => "已作废", _ => "开放" };
    public string ConfirmationStatusText => ConfirmationStatus == ConfirmationStatusEnum.已确认 ? "实物需求已确认" : "实物需求待确认";
    public bool CanEditSupplement => Capabilities.CanSave && HasAccess;
    public bool CanEditFacts => Capabilities.CanEditFacts && HasAccess;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(DeleteLineCommand))] public partial AfterSalesLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial AfterSalesCapabilitiesDto Capabilities { get; set; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(DocumentStatusText))] public partial DocumentStatusEnum DocumentStatus { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ConfirmationStatusText))] public partial ConfirmationStatusEnum ConfirmationStatus { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "新建售后服务单。";

    partial void OnCapabilitiesChanged(AfterSalesCapabilitiesDto value)
    {
        OnPropertyChanged(nameof(CanEditFacts)); OnPropertyChanged(nameof(CanEditSupplement)); NotifyCommands();
    }

    /// <inheritdoc/>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (!HasAccess) { IsReadOnly = true; ResultSummary = "当前账号没有品质管理权限。"; return; }
        var request = parameter as AfterSalesDocumentOpenRequest;
        if (request?.AfterSalesId is not Guid uid || uid == Guid.Empty)
        {
            InitializeNew();
            await LoadLookupsAsync(cancellationToken);
            return;
        }
        await LoadAsync(uid, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        return !HasUnsavedChanges || await _dialogs.ConfirmAsync("关闭售后服务单", "当前服务单有未保存修改，确定放弃并关闭吗？", "放弃并关闭", "继续编辑", cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddLine))] private void AddLine()
    {
        var line = AfterSalesLineModel.CreateNew(Lines.Count == 0 ? 1 : Lines.Max(x => x.LineNumber) + 1);
        Attach(line); Lines.Add(line); SelectedLine = line; UpdateDirtyState();
    }
    [RelayCommand(CanExecute = nameof(CanDeleteLine))] private void DeleteLine()
    {
        var line = SelectedLine!;
        if (!line.IsNew) { line.IsDeleted = true; _deletedLines.Add(line); }
        Detach(line); Lines.Remove(line); SelectedLine = Lines.FirstOrDefault(); UpdateDirtyState();
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private async Task SaveAsync() => await RunWriteAsync(async () =>
    {
        if (!Validate(false)) return;
        var create = !IsPersisted;
        var command = CreateSaveCommand("save");
        var result = await _runner.RunAsync(() => create ? _api.CreateAsync(command) : _api.SaveAsync(Header.Uid, command), "保存售后服务单失败");
        CompleteCommand("save", result);
        if (!result.Success || result.Data is null) { HandleFailure("保存失败", result); return; }
        ApplyCommandResult(result.Data);
        await ReloadAfterWriteAsync(result.Data.Uid, "售后服务单已保存");
    }, "正在保存售后服务单...");

    [RelayCommand(CanExecute = nameof(CanConfirm))] private async Task ConfirmDemandAsync()
    {
        if (!Validate(true)) return;
        if (!Lines.Any(x => x.Quantity > 0)) { _messages.ShowInfo("无需确认", "当前没有正数量补发要求，投诉单可以直接保存并在处理完成后结案。"); return; }
        if (!await _dialogs.ConfirmAsync("确认实物需求", "将先保存当前修改，再把正数量补件交给来源需求。确认后相关事实会锁定。", "保存并确认")) return;
        await RunWriteAsync(async () =>
        {
            var create = !IsPersisted;
            var save = CreateSaveCommand("confirm-save");
            var confirmId = CommandId("confirm", JsonSerializer.Serialize(new { save.Uid, Header.Version }));
            var result = await _feature.SaveAndConfirmAsync(save, create, confirmId);
            CompleteCommand("confirm-save", result.Save);
            if (!result.Save.Success || result.Save.Data is null) { HandleFailure("确认前保存失败", result.Save); return; }
            ApplyCommandResult(result.Save.Data);
            _navigation.UpdateDocumentKey(this, $"after-sales:{Header.Uid:N}");
            if (result.Confirm is null || !result.Confirm.Success || result.Confirm.Data is null)
            {
                CompleteCommand("confirm", result.Confirm);
                await ReloadCoreAsync(Header.Uid, CancellationToken.None);
                HandleFailure("服务单已保存，但实物需求未确认", result.Confirm);
                return;
            }
            CompleteCommand("confirm", result.Confirm);
            ApplyCommandResult(result.Confirm.Data);
            await ReloadAfterWriteAsync(Header.Uid, "实物需求已确认");
        }, "正在保存并确认实物需求...");
    }

    [RelayCommand(CanExecute = nameof(CanCancelConfirmation))] private async Task CancelDemandConfirmationAsync()
    {
        var reason = await _reasonDialog.RequestAsync("取消实物需求确认", "来源需求会先检查是否能够安全撤回；失败时服务单仍保持确认和锁定状态。");
        if (reason is null) return;
        await ExecuteCommandAsync("cancel", id => _api.CancelDemandConfirmationAsync(Header.Uid,
            new CancelAfterSalesDemandConfirmationCommand { CommandId = id, Version = Header.Version, Reason = reason }), "已取消实物需求确认", reason);
    }
    [RelayCommand(CanExecute = nameof(CanCloseDocument))] private async Task CloseAsync()
    {
        var reason = await _reasonDialog.RequestAsync("结案", "正数量要求必须已正式交付完成或由下游合法关闭。请输入结案说明。");
        if (reason is null) return;
        await ExecuteCommandAsync("close", id => _api.CloseAsync(Header.Uid, new CloseAfterSalesCommand { CommandId = id, Version = Header.Version, Reason = reason }), "售后服务单已结案", reason);
    }
    [RelayCommand(CanExecute = nameof(CanReopen))] private async Task ReopenAsync()
    {
        var reason = await _reasonDialog.RequestAsync("重开售后服务单", "重开只恢复售后生命周期，不会重新发布来源需求。请输入原因。");
        if (reason is null) return;
        await ExecuteCommandAsync("reopen", id => _api.ReopenAsync(Header.Uid, new ReopenAfterSalesCommand { CommandId = id, Version = Header.Version, Reason = reason }), "售后服务单已重开", reason);
    }
    [RelayCommand(CanExecute = nameof(CanVoid))] private async Task VoidAsync()
    {
        var reason = await _reasonDialog.RequestAsync("作废售后服务单", "作废要求服务单开放、未确认且没有活动来源需求。请输入原因。");
        if (reason is null) return;
        await ExecuteCommandAsync("void", id => _api.VoidAsync(Header.Uid, new VoidAfterSalesCommand { CommandId = id, Version = Header.Version, Reason = reason }), "售后服务单已作废", reason);
    }
    [RelayCommand(CanExecute = nameof(CanReload))] private async Task ReloadAsync()
    {
        if (IsDirty && !await _dialogs.ConfirmAsync("重新加载售后服务单", "重新加载会放弃当前未保存修改，确定继续吗？", "放弃并重新加载", "继续编辑")) return;
        await LoadAsync(Header.Uid, CancellationToken.None);
    }
    [RelayCommand(CanExecute = nameof(CanRefreshLookups))] private Task RefreshLookupsAsync() => LoadLookupsAsync(CancellationToken.None);

    // 生命周期命令共享写入门禁，服务端结构化错误保留当前编辑状态。
    private async Task ExecuteCommandAsync(string operation, Func<Guid, Task<ServiceResult<AfterSalesCommandResult>>> call, string success, string reason)
    {
        await RunWriteAsync(async () =>
        {
            var id = CommandId(operation, JsonSerializer.Serialize(new { Header.Uid, Header.Version, reason }));
            var result = await _runner.RunAsync(() => call(id), success + "失败");
            CompleteCommand(operation, result);
            if (!result.Success || result.Data is null) { HandleFailure(success + "失败", result); return; }
            ApplyCommandResult(result.Data);
            await ReloadAfterWriteAsync(Header.Uid, success);
        }, "正在执行售后状态操作...");
    }

    // 本地验证只负责明显输入错误，状态及跨领域资格仍由后端决定。
    private bool Validate(bool forConfirmation)
    {
        if (Header.CustomerId == Guid.Empty) return ValidationFailed("请选择客户。");
        if (Header.AcceptedDate == default) return ValidationFailed("请选择受理日期。");
        if (Lines.Count == 0) return ValidationFailed("至少需要一条投诉明细。");
        foreach (var line in Lines)
        {
            var error = line.Validate(forConfirmation);
            if (error is null) continue;
            SelectedLine = line;
            return ValidationFailed($"第 {line.LineNumber} 行：{error}");
        }
        ValidationSummary = null; return true;
    }

    private bool ValidationFailed(string message) { ValidationSummary = message; _messages.ShowWarning("售后服务单未提交", message); return false; }

    private SaveAfterSalesCommand CreateSaveCommand(string operation)
    {
        var command = new SaveAfterSalesCommand
        {
            Uid = Header.Uid, Version = Header.Version,
            Header = Header.ToDto(), AddedLines = Lines.Where(x => x.IsNew).Select(x => x.ToDto()).ToList(),
            UpdatedLines = Lines.Where(x => !x.IsNew && x.IsDirty).Select(x => x.ToDto()).ToList(),
            DeletedLines = _deletedLines.Select(x => new AfterSalesDeletedLineDto { Uid = x.Uid, Version = x.Version }).ToList()
        };
        var fingerprint = JsonSerializer.Serialize(command);
        command.CommandId = CommandId(operation, fingerprint);
        return command;
    }

    private Guid CommandId(string operation, string fingerprint)
    {
        if (_pendingCommands.TryGetValue(operation, out var pending) && pending.Fingerprint == fingerprint) return pending.CommandId;
        var id = Guid.NewGuid(); _pendingCommands[operation] = (fingerprint, id); return id;
    }
    private void CompleteCommand<T>(string operation, ServiceResult<T>? result)
    {
        if (result?.Success == true || result?.Error is not null) _pendingCommands.Remove(operation);
    }
    private void HandleFailure<T>(string title, ServiceResult<T>? result)
    {
        var message = result?.Error?.Message ?? result?.Message ?? "请求没有返回结果。";
        ResultSummary = title + "：" + message;
        if (!string.IsNullOrWhiteSpace(result?.Error?.Field))
        {
            ValidationSummary = $"{result.Error.Field}：{message}";
            SelectErrorLine(result.Error.Field);
        }
        if (result?.Error?.Code == "VERSION_CONFLICT") _messages.ShowWarning("数据已变化", message + " 本地输入已保留，请决定是否刷新。");
        else _messages.ShowError(title, message);
    }

    private void InitializeNew()
    {
        _loading = true; _hasServerIdentity = false; Header.Uid = GuidSequentialGenerator.NewGuid(); Header.Version = 1; Header.AcceptedDate = DateTime.Today;
        DocumentStatus = DocumentStatusEnum.Open; ConfirmationStatus = ConfirmationStatusEnum.待确认;
        Capabilities = new AfterSalesCapabilitiesDto { CanSave = true, CanEditFacts = true, CanVoid = true };
        var line = AfterSalesLineModel.CreateNew(1); Attach(line); Lines.Add(line); SelectedLine = line;
        _loading = false; UpdateDirtyState();
    }

    private async Task LoadAsync(Guid uid, CancellationToken outerToken)
    {
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken); _loadCancellation = cts;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetAsync(uid, cts.Token), cts.Token, "加载售后服务单失败");
            if (cts.IsCancellationRequested || _disposed || !ReferenceEquals(_loadCancellation, cts)) return;
            if (!result.Success || result.Data is null) { HandleFailure("加载失败", result); return; }
            ApplyDetail(result.Data);
            await LoadLookupsAsync(cts.Token);
        }, "正在加载售后服务单...");
        if (ReferenceEquals(_loadCancellation, cts)) _loadCancellation = null;
        cts.Dispose();
    }

    private async Task ReloadAfterWriteAsync(Guid uid, string success)
    {
        await ReloadCoreAsync(uid, CancellationToken.None);
        ResultSummary = success; _messages.ShowSuccess(success, DocumentTitle);
    }
    private async Task ReloadCoreAsync(Guid uid, CancellationToken token)
    {
        var result = await _runner.RunAsync(() => _api.GetAsync(uid, token), token, "刷新售后服务单失败");
        if (result.Success && result.Data is not null) ApplyDetail(result.Data);
        else _messages.ShowWarning("操作已完成，刷新失败", result.Error?.Message ?? result.Message);
    }

    private void ApplyDetail(AfterSalesDetailDto detail)
    {
        _loading = true; _hasServerIdentity = true;
        foreach (var line in Lines) Detach(line);
        Lines.Clear(); _deletedLines.Clear(); Header.Load(detail.Header);
        foreach (var dto in detail.Lines) { var line = new AfterSalesLineModel(); line.Load(dto); Attach(line); Lines.Add(line); }
        SelectedLine = Lines.FirstOrDefault(); DocumentStatus = detail.Header.DocumentStatus;
        ConfirmationStatus = detail.Header.ConfirmationStatus; Capabilities = detail.Capabilities;
        Title = DocumentTitle; IsReadOnly = !HasAccess; _loading = false;
        IsDirty = false; OnPropertyChanged(nameof(DocumentTitle)); OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCommands();
        _navigation.UpdateDocumentKey(this, $"after-sales:{Header.Uid:N}");
    }

    // 写入成功后先接纳服务端返回的身份、版本和状态，避免随后刷新失败时误走重复创建。
    private void ApplyCommandResult(AfterSalesCommandResult result)
    {
        _loading = true;
        _hasServerIdentity = true;
        Header.Uid = result.Uid;
        Header.Version = result.Version;
        Header.AfterSalesNo = result.AfterSalesNo;
        DocumentStatus = result.DocumentStatus;
        ConfirmationStatus = result.ConfirmationStatus;
        Capabilities = result.Capabilities;
        Title = DocumentTitle;
        _loading = false;
        OnPropertyChanged(nameof(IsPersisted));
        OnPropertyChanged(nameof(DocumentTitle));
    }

    // 服务端行字段路径使用 Lines[Uid].Field；定位失败时仍保留整体错误摘要。
    private void SelectErrorLine(string field)
    {
        const string prefix = "Lines[";
        if (!field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var end = field.IndexOf(']', prefix.Length);
        if (end <= prefix.Length || !Guid.TryParse(field[prefix.Length..end], out var uid)) return;
        SelectedLine = Lines.FirstOrDefault(line => line.Uid == uid);
    }

    private async Task LoadLookupsAsync(CancellationToken token)
    {
        if (_disposed) return;
        var kinds = new[] { "customers", "owners", "suppliers", "products", "sales-order-lines", "colors", "quality-categories" };
        foreach (var kind in kinds)
        {
            var result = await _runner.RunAsync(() => _api.LookupAsync(kind, new AfterSalesLookupRequest
            { CustomerId = Header.CustomerId == Guid.Empty ? null : Header.CustomerId, PageIndex = 1, PageSize = 200 }, token), token, "加载售后候选失败");
            if (token.IsCancellationRequested || _disposed) return;
            var target = kind switch { "customers" => Customers, "owners" => Owners, "suppliers" => Suppliers,
                "products" => Products, "sales-order-lines" => SalesOrderLines, "colors" => Colors, _ => QualityCategories };
            if (!result.Success || result.Data is null) continue;
            target.Clear();
            foreach (var item in result.Data.Items) target.Add(item);
        }
    }

    private void Attach(AfterSalesLineModel line) => line.PropertyChanged += OnEditChanged;
    private void Detach(AfterSalesLineModel line) => line.PropertyChanged -= OnEditChanged;
    private void OnEditChanged(object? sender, PropertyChangedEventArgs args) { if (!_loading) UpdateDirtyState(); }
    private void UpdateDirtyState()
    {
        IsDirty = Header.IsDirty || Lines.Any(x => x.IsDirty || x.IsNew) || _deletedLines.Count > 0;
        OnPropertyChanged(nameof(HasUnsavedChanges)); NotifyCommands();
    }
    private bool CanAddLine() => CanEditFacts && IsNotBusy;
    private bool CanDeleteLine() => CanEditFacts && IsNotBusy && SelectedLine is not null && Lines.Count > 1;
    private bool CanReload() => HasAccess && IsNotBusy && IsPersisted;
    private bool CanRefreshLookups() => HasAccess && IsNotBusy;
    private bool CanSave() => HasAccess && IsNotBusy && Capabilities.CanSave && IsDirty;
    private bool CanConfirm() => HasAccess && IsNotBusy && DocumentStatus == DocumentStatusEnum.Open
        && ConfirmationStatus != ConfirmationStatusEnum.已确认 && (Capabilities.CanConfirm || IsDirty);
    private bool CanCancelConfirmation() => HasAccess && IsNotBusy && Capabilities.CanCancelConfirmation && !IsDirty;
    private bool CanCloseDocument() => HasAccess && IsNotBusy && Capabilities.CanClose && !IsDirty;
    private bool CanReopen() => HasAccess && IsNotBusy && Capabilities.CanReopen && !IsDirty;
    private bool CanVoid() => HasAccess && IsNotBusy && Capabilities.CanVoid && !IsDirty;
    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged(); ConfirmDemandCommand.NotifyCanExecuteChanged();
        CancelDemandConfirmationCommand.NotifyCanExecuteChanged(); CloseCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged(); VoidCommand.NotifyCanExecuteChanged();
        AddLineCommand.NotifyCanExecuteChanged(); DeleteLineCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged(); RefreshLookupsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>取消可替代读取并解除行订阅；重复释放安全。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null;
        Header.PropertyChanged -= OnEditChanged;
        foreach (var line in Lines) Detach(line);
    }
}
