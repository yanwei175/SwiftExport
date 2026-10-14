using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;

/// <summary>维护一个字典类型的明细草稿、删除池及保存回执，由字典页面和固定类型页面分别拥有实例。</summary>
public sealed partial class DictionaryDataEditorViewModel : ViewModelBaseEx, IDisposable
{
    private readonly ISysDictDataApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IReferenceDataCacheService _cache;
    private readonly ICurrentUserService _user;
    private readonly string? _sessionUserId;
    private CancellationTokenSource? _queryCancellation;
    private long _queryVersion;
    private bool _disposed, _sessionEnded;

    /// <summary>复用现有字典 API、行状态和会话缓存，不维护字典类型本身。</summary>
    public DictionaryDataEditorViewModel(ISysDictDataApi api, IApiCallRunner runner,
        IMessageService messages, IDialogService dialogs, IReferenceDataCacheService cache, ICurrentUserService user)
    {
        _api = api; _runner = runner; _messages = messages; _dialogs = dialogs; _cache = cache; _user = user;
        _sessionUserId = user.CurrentUser?.UserId;
        Lines = new(_ => new DictionaryDataLineModel());
        Lines.PropertyChanged += OnLinesChanged;
        PropertyChanged += OnOwnStateChanged;
        user.CurrentUserChanged += OnUserChanged;
        foreach (var command in new IRelayCommand[] { AddRowCommand, DeleteSelectedCommand, SaveCommand, RejectCommand, RefreshCommand })
            RegisterBusyAwareCommand(command);
    }

    /// <summary>当前明细行与已持久化行的删除池。</summary>
    public BaseListModel<DictionaryDataLineModel, SysDictDataDto> Lines { get; }
    /// <summary>视觉层提交原生单元格和行编辑的窄回调。</summary>
    public Func<bool>? CommitPendingEdits { get; set; }
    /// <summary>撤销前取消无效转换缓冲，由控件桥接原生编辑。</summary>
    public Action? CancelPendingEdits { get; set; }
    /// <summary>所有字典明细未保存变更，包括删除池。</summary>
    public bool HasUnsavedChanges => Lines.HasChanges;
    /// <summary>读取和写入期间禁止宿主替换当前字典。</summary>
    public bool IsWorking => IsBusy || IsWriting;
    /// <summary>字典类型已保存且无类型草稿，并且有完整明细基线时可维护。</summary>
    public bool CanEditValues => ContextEditable && IsLoaded && !RequiresReload && !string.IsNullOrEmpty(DictCode);
    /// <summary>字典明细交互受当前会话、宿主运行状态及独立权限约束。</summary>
    public bool IsInteractionEnabled => IsCurrentSession && !HostBusy && IsNotBusy;
    /// <summary>只有新增或编辑权限时允许进入字段编辑。</summary>
    public bool IsValueEditorReadOnly => !IsInteractionEnabled || !CanEditValues
        || !(HasPermission("Create") || HasPermission("Edit"));
    /// <summary>成功读取的空清单提示，失败不冒充正常空数据。</summary>
    public bool IsEmpty => IsLoaded && IsNotBusy && Lines.Items.Count == 0;
    /// <summary>就地显示校验或服务端失败。</summary>
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationSummary);
    private bool IsCurrentSession => !_disposed && !_sessionEnded && _user.IsAuthenticated
        && _user.CurrentUser?.UserId == _sessionUserId && _user.HasPermission("System.Dictionary.View");

    [ObservableProperty] public partial string? DictCode { get; private set; }
    [ObservableProperty] public partial string DictionaryName { get; private set; } = "字典值";
    [ObservableProperty] public partial DictionaryDataLineModel? SelectedLine { get; set; }
    [ObservableProperty] public partial bool ContextEditable { get; set; }
    [ObservableProperty] public partial bool HostBusy { get; set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial bool RequiresReload { get; private set; }
    [ObservableProperty] public partial bool IsWriting { get; private set; }
    [ObservableProperty] public partial string StatusMessage { get; private set; } = "尚未加载字典值。";

    /// <summary>宿主已经处理旧草稿后，激活新的固定类型；重复激活同类型保留已加载草稿。</summary>
    public async Task ActivateAsync(string code, string name)
    {
        if (!IsCurrentSession || IsWriting) return;
        if (string.Equals(DictCode, code, StringComparison.OrdinalIgnoreCase) && IsLoaded) return;
        CancelQuery();
        DictCode = code; DictionaryName = name;
        Lines.Clear(); SelectedLine = null; IsLoaded = false; RequiresReload = false;
        await RunBusyAsync(() => LoadCoreAsync(), "正在读取字典值...");
    }

    /// <summary>在类型新增或无类型时解除明细上下文，调用方先保护旧草稿。</summary>
    public void ClearContext()
    {
        CancelQuery(); DictCode = null; Lines.Clear(); SelectedLine = null; IsLoaded = false;
        RequiresReload = false; SetFeedback(null); StatusMessage = "请先选择或保存字典类型。";
    }

    /// <summary>刷新或类型切换前提交原生编辑；用户取消时不改变任何草稿。</summary>
    public async Task<bool> CanDiscardAsync(string message, CancellationToken cancellationToken = default)
    {
        if (IsWorking || CommitPendingEdits?.Invoke() == false) return false;
        if (!HasUnsavedChanges) return true;
        return await _dialogs.ConfirmAsync("放弃字典值变更", message, "放弃变更", cancellationToken: cancellationToken);
    }

    /// <summary>页签关闭沿用字典明细草稿确认；写入发出后必须等待完成。</summary>
    public Task<bool> CanCloseAsync(CancellationToken cancellationToken) => CanDiscardAsync("字典值有未保存修改，确定放弃并关闭吗？", cancellationToken);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddRow()
    {
        if (!CanAdd() || CommitPendingEdits?.Invoke() == false) return;
        SelectedLine = Lines.AddNew(new SysDictDataDto { DictCode = DictCode!, DataNameEn = string.Empty,
            DataNameZh = string.Empty, SortOrder = Lines.Items.Select(item => item.SortOrder ?? 0).DefaultIfEmpty().Max() + 10,
            IsEnabled = true, IsSystem = false });
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        if (!CanDelete() || CommitPendingEdits?.Invoke() == false || SelectedLine is not { } line) return;
        var name = string.IsNullOrWhiteSpace(line.DataNameZh) ? line.DataNameEn : line.DataNameZh;
        if (!await _dialogs.ConfirmAsync("删除字典值", $"确定删除“{name}”吗？保存前可以撤销。", "删除")) return;
        if (!CanDelete() || !ReferenceEquals(SelectedLine, line)) return;
        Lines.Remove(line); SelectedLine = Lines.Items.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => TrySaveAsync();

    /// <summary>整批提交并核对回执，再接纳身份版本；缓存刷新失败不会恢复已提交的草稿。</summary>
    public async Task<bool> TrySaveAsync()
    {
        if (!CanSave() || CommitPendingEdits?.Invoke() == false) return false;
        var saved = false;
        IsWriting = true;
        try
        {
            await RunWriteAsync(async () =>
            {
                var validation = await Lines.ValidateAllAsync();
                if (!string.IsNullOrWhiteSpace(validation))
                { SetFeedback(validation); _messages.ShowWarning("字典值未保存", validation); return; }
                if (!IsCurrentSession || !HasSavePermissions()) return;
                var rows = new Dictionary<string, DictionaryDataLineModel>();
                var request = BuildRequest(rows);
                var result = await _runner.RunAsync(() => _api.BatchSaveAsync(request), "保存字典值失败");
                if (!IsCurrentSession) return;
                if (!result.Success)
                { SetFeedback(result.Message); _messages.ShowError("保存字典值失败", result.Message); return; }
                if (!ValidReceipts(request, result.Data))
                {
                    RequiresReload = true;
                    SetFeedback("服务器返回成功但保存回执不完整，草稿已保留；请刷新核对，不能重复提交。");
                    return;
                }
                foreach (var receipt in result.Data!.Items.Where(item => item.Operation != SaveOperation.Delete))
                {
                    var row = rows[receipt.ClientKey]; row.Uid = receipt.Uid!.Value; row.Version = receipt.NewVersion!.Value;
                }
                Lines.AcceptChanges(); SetFeedback(null); saved = true;
                StatusMessage = $"{DictionaryName}已保存，共提交 {request.TotalCount} 条变更。";
                _messages.ShowSuccess("字典值已保存", StatusMessage);
                // 保存身份已接纳；后续缓存失败只作为独立警告，不重新制造待提交变更。
                try
                {
                    var refresh = await _cache.RefreshDictionariesAsync();
                    if (IsCurrentSession && !refresh.Success) _messages.ShowWarning("字典值已保存，但缓存刷新失败", refresh.Message);
                }
                catch (Exception)
                {
                    if (IsCurrentSession) _messages.ShowWarning("字典值已保存，但缓存刷新失败", "请稍后刷新字典缓存。");
                }
            }, "正在保存字典值...");
        }
        finally { IsWriting = false; NotifyState(); }
        return saved;
    }

    /// <summary>冻结当前变更集并建立客户端键与私有行的映射，不改变原有字典批量协议。</summary>
    private SysDictDataBatchSaveRequest BuildRequest(Dictionary<string, DictionaryDataLineModel> rows)
    {
        var request = new SysDictDataBatchSaveRequest();
        foreach (var line in Lines.AddedItems)
        {
            var key = "new-" + Guid.NewGuid().ToString("N"); rows.Add(key, line);
            var dto = line.ToDto(); dto.DictCode = DictCode!;
            request.CreateItems.Add(new() { ClientKey = key, RowIndex = line.RowIndex, Data = dto });
        }
        foreach (var line in Lines.ModifiedItems)
        {
            var key = line.Uid.ToString("N"); rows.Add(key, line);
            var dto = line.ToDto(); dto.DictCode = DictCode!;
            request.UpdateItems.Add(new() { ClientKey = key, RowIndex = line.RowIndex, Uid = line.Uid, Version = line.Version, Data = dto });
        }
        foreach (var line in Lines.DeletedItems.Where(item => item.Uid != Guid.Empty))
            request.DeleteItems.Add(new() { ClientKey = line.Uid.ToString("N"), RowIndex = line.RowIndex, Uid = line.Uid, Version = line.Version, IsSoftDelete = true });
        return request;
    }

    /// <summary>每项客户端键、操作、身份及版本必须能对应冻结请求；内置零版本覆盖也是更新。</summary>
    private static bool ValidReceipts(SysDictDataBatchSaveRequest request, SwiftExport.Contracts.DTOs.SystemDtos.Responses.SysDictDataBatchSaveResponse? response)
    {
        if (response?.Items is null || response.Items.Count != request.TotalCount || response.Items.Any(item => item is null) || !response.AllSucceeded
            || response.Items.Any(item => item.Uid is null || item.Uid == Guid.Empty || item.NewVersion is null or <= 0)) return false;
        var written = response.Items.Where(item => item.Operation != SaveOperation.Delete).ToArray();
        if (written.Select(item => item.Uid).Distinct().Count() != written.Length) return false;
        foreach (var item in request.CreateItems)
            if (response.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Create && receipt.NewVersion == 1) != 1) return false;
        foreach (var item in request.UpdateItems)
            if (response.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Update && receipt.Uid == item.Uid
                && receipt.OldVersion == item.Version && receipt.NewVersion == item.Version + 1) != 1) return false;
        foreach (var item in request.DeleteItems)
            if (response.Items.Count(receipt => receipt.ClientKey == item.ClientKey && receipt.Operation == SaveOperation.Delete && receipt.Uid == item.Uid
                && receipt.OldVersion == item.Version && receipt.NewVersion >= item.Version) != 1) return false;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanReject))]
    private void Reject()
    {
        if (!CanReject()) return;
        CancelPendingEdits?.Invoke(); Lines.RejectChanges(); SelectedLine = Lines.Items.FirstOrDefault(); SetFeedback(null);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh() || !await CanDiscardAsync("刷新会放弃当前字典值的未保存修改。")) return;
        await RunBusyAsync(() => LoadCoreAsync(), "正在刷新字典值...");
    }

    /// <summary>宿主明确刷新已确认类型；失败保留旧完整明细和草稿，不能接纳晚到响应。</summary>
    public Task ReloadAsync() => !IsCurrentSession || IsWorking ? Task.CompletedTask
        : RunBusyAsync(() => LoadCoreAsync(), "正在读取字典值...");

    private async Task LoadCoreAsync()
    {
        if (DictCode is not { } code || !IsCurrentSession) return;
        CancelQuery(); var version = _queryVersion;
        var cancellation = _queryCancellation = new CancellationTokenSource();
        try
        {
            var result = await _runner.RunAsync(() => _api.GetByCodeAsync(code).WaitAsync(cancellation.Token), cancellation.Token, "加载字典值失败");
            if (!IsCurrentSession || version != _queryVersion || cancellation.IsCancellationRequested || DictCode != code) return;
            if (!result.Success || result.Data is null)
            { SetFeedback(result.Success ? "服务器未返回完整字典明细。" : result.Message); return; }
            Lines.LoadFromDtos(result.Data); SelectedLine = Lines.Items.FirstOrDefault(); IsLoaded = true; RequiresReload = false;
            SetFeedback(null); StatusMessage = $"{DictionaryName}共 {Lines.Items.Count} 个字典值。";
        }
        finally
        {
            if (ReferenceEquals(_queryCancellation, cancellation)) _queryCancellation = null;
            cancellation.Dispose(); NotifyState();
        }
    }

    private bool HasPermission(string operation) => _user.HasPermission($"System.Dictionary.{operation}");
    /// <summary>原生行编辑按新增与修改分别授权，不能以其中一种权限编辑另一类行。</summary>
    public bool CanEditRow(string operation) => CanOperate() && HasPermission(operation);
    private bool CanOperate() => IsInteractionEnabled && CanEditValues;
    private bool CanAdd() => CanOperate() && HasPermission("Create");
    private bool CanDelete() => CanOperate() && HasPermission("Delete") && SelectedLine is { IsBuiltIn: false, IsSystem: false };
    private bool HasSavePermissions() => (!Lines.AddedItems.Any() || HasPermission("Create"))
        && (!Lines.ModifiedItems.Any() || HasPermission("Edit"))
        && (!Lines.DeletedItems.Any(item => item.Uid != Guid.Empty) || HasPermission("Delete"));
    private bool CanSave() => CanOperate() && HasUnsavedChanges && HasSavePermissions();
    private bool CanReject() => IsInteractionEnabled && HasUnsavedChanges;
    private bool CanRefresh() => IsInteractionEnabled && !string.IsNullOrWhiteSpace(DictCode);
    private void SetFeedback(string? message)
    { ValidationSummary = message; if (!string.IsNullOrWhiteSpace(message)) StatusMessage = message; NotifyState(); }
    private void OnLinesChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(Lines.HasChanges)) NotifyState(); }
    private void OnOwnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsBusy) or nameof(ContextEditable) or nameof(HostBusy) or nameof(IsLoaded)
            or nameof(RequiresReload) or nameof(IsWriting) or nameof(SelectedLine)) NotifyState();
    }
    private void OnUserChanged(object? sender, EventArgs args)
    {
        if (!_user.IsAuthenticated || _user.CurrentUser?.UserId != _sessionUserId) _sessionEnded = true;
        if (!IsCurrentSession) CancelQuery();
        NotifyState();
    }
    private void NotifyState()
    {
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(IsWorking)); OnPropertyChanged(nameof(IsInteractionEnabled));
        OnPropertyChanged(nameof(CanEditValues)); OnPropertyChanged(nameof(IsValueEditorReadOnly));
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasValidationError));
        AddRowCommand.NotifyCanExecuteChanged(); DeleteSelectedCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
    }
    private void CancelQuery() { _queryVersion++; _queryCancellation?.Cancel(); }
    /// <summary>未知异常只提供稳定业务反馈，不向页面泄露堆栈。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    { if (IsCurrentSession) SetFeedback("字典操作未完成，输入已保留，请稍后重试。"); return Task.CompletedTask; }
    /// <summary>解绑行状态和会话订阅，取消读取；旧响应不得回写下一会话。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelQuery(); _user.CurrentUserChanged -= OnUserChanged;
        Lines.PropertyChanged -= OnLinesChanged; PropertyChanged -= OnOwnStateChanged;
        CommitPendingEdits = null; CancelPendingEdits = null;
    }
}
