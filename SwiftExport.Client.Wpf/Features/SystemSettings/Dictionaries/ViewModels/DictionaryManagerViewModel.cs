using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;

/// <summary>管理字典类型和值的编辑状态，并为页签或独立窗口提供统一的加载、保存和关闭门禁。</summary>
public sealed partial class DictionaryManagerViewModel
    : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private readonly ISysDictTypeApi _dictTypeApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private readonly ICurrentUserService _currentUser;
    private bool _isInitialized;
    private bool _isDisposed;
    private long _queryVersion;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _queryCancellation;
    private string? _selectionBeforeNew;
    private readonly string? _sessionUserId;
    private bool _sessionEnded;

    /// <summary>类型编辑由父页面负责，明细编辑交给独立子工作区。</summary>
    public DictionaryManagerViewModel(
        ISysDictTypeApi dictTypeApi,
        DictionaryDataEditorViewModel values,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache,
        ICurrentUserService currentUser)
    {
        _dictTypeApi = dictTypeApi;
        Values = values;
        Values.PropertyChanged += OnValuesChanged;
        PropertyChanged += OnOwnStateChanged;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        _currentUser = currentUser;
        _sessionUserId = currentUser.CurrentUser?.UserId;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;

        Title = "字典管理";


        RegisterBusyAwareCommand(SelectDictionaryCommand);
        RegisterBusyAwareCommand(AddDictionaryTypeCommand);
        RegisterBusyAwareCommand(SaveDictionaryTypeCommand);
        RegisterBusyAwareCommand(DeleteDictionaryTypeCommand);
        RegisterBusyAwareCommand(RejectDictionaryTypeChangesCommand);
        RegisterBusyAwareCommand(RefreshCommand);
    }

    /// <summary>获取可选择的字典类型集合。</summary>
    public ObservableCollection<DictionaryTypeItemModel> Dictionaries { get; } = [];

    /// <summary>获取当前字典的可编辑值列表。</summary>
    public BaseListModel<DictionaryDataLineModel, SysDictDataDto> Lines => Values.Lines;

    /// <summary>独立明细编辑工作区，固定类型页面也使用同一实现。</summary>
    public DictionaryDataEditorViewModel Values { get; }
    /// <summary>明细新增交由子工作区维护。</summary>
    public IRelayCommand AddRowCommand => Values.AddRowCommand;
    /// <summary>明细删除交由子工作区维护删除池。</summary>
    public IAsyncRelayCommand DeleteSelectedCommand => Values.DeleteSelectedCommand;
    /// <summary>明细保存交由子工作区核对回执。</summary>
    public IAsyncRelayCommand SaveCommand => Values.SaveCommand;
    /// <summary>明细撤销不影响类型编辑。</summary>
    public IRelayCommand RejectAllChangesCommand => Values.RejectCommand;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial DictionaryTypeItemModel? SelectedDictionary { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDictionaryTypeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDictionaryTypeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectDictionaryTypeChangesCommand))]
    [NotifyPropertyChangedFor(nameof(HasTypeEditor))]
    [NotifyPropertyChangedFor(nameof(IsTypeEditorReadOnly))]
    public partial DictionaryTypeEditModel? TypeEditor { get; private set; }

    /// <summary>保留宿主当前明细选择入口，编辑状态由子工作区持有。</summary>
    public DictionaryDataLineModel? SelectedLine { get => Values.SelectedLine; set => Values.SelectedLine = value; }

    [ObservableProperty]
    public partial string ResultSummary { get; set; } = "尚未加载字典。";

    public string DocumentTitle => Title;

    public bool HasUnsavedChanges => Lines.HasChanges || TypeEditor?.IsDirty == true;

    /// <summary>明细反馈在共享组件内展示，父页面仅显示类型反馈。</summary>
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationSummary) && !Values.HasValidationError;

    public bool HasTypeEditor => TypeEditor is not null;

    public bool IsTypeEditorReadOnly => TypeEditor?.IsProtected != false;

    /// <summary>服务端有效类型无需先落库，已有干净类型快照即可维护其明细。</summary>
    public bool CanEditValues => SelectedDictionary is not null
        && TypeEditor is { IsNew: false, IsDirty: false };

    public bool IsValueEditorReadOnly => !CanEditValues
        || !(_currentUser.HasPermission("System.Dictionary.Create") || _currentUser.HasPermission("System.Dictionary.Edit"));

    public bool IsEmpty => CanEditValues && !IsBusy && Lines.Items.Count == 0;

    /// <summary>指示字典写入是否正在执行；执行期间独立窗口不能关闭。</summary>
    [ObservableProperty]
    public partial bool IsTypeWriting { get; private set; }

    /// <summary>类型和明细写入任一执行时，宿主均不得关闭。</summary>
    public bool IsWriting => IsTypeWriting || Values.IsWriting;

    /// <summary>首次显示时加载字典类型和默认字典值；重复调用不会重复加载。</summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _isInitialized = true;
        await RunBusyAsync(() => LoadDictionaryTypesAsync(), "正在加载字典...");
    }

    /// <summary>检查是否可以关闭宿主；存在未保存变更时向用户确认放弃。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || Values.IsWorking || Values.CommitPendingEdits?.Invoke() == false) return false;
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "放弃未保存变更",
            "字典类型或字典值有未保存的修改，确定关闭页面吗？",
            "放弃并关闭",
            cancellationToken: cancellationToken);
    }

    /// <summary>离开缓存字典页时，确认后恢复已加载快照，避免返回页面再保存已放弃的修改。</summary>
    public async Task<bool> CanLeavePageAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || Values.IsWorking || Values.CommitPendingEdits?.Invoke() == false) return false;
        if (!HasUnsavedChanges) return true;
        if (!await _dialogService.ConfirmAsync("放弃未保存变更",
                "字典类型或字典值有未保存的修改，确定离开页面吗？", "放弃并离开",
                cancellationToken: cancellationToken)) return false;

        if (TypeEditor?.IsNew == true)
        {
            SetTypeEditor(null);
            Values.ClearContext();
            SelectedLine = null;
            _isInitialized = false;
        }
        else
        {
            TypeEditor?.RejectChanges();
            Lines.RejectChanges();
            SelectedLine = Lines.Items.FirstOrDefault();
        }

        SetValidationFeedback(null);
        UpdateChangeState();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanSelectDictionary))]
    private async Task SelectDictionaryAsync(DictionaryTypeItemModel? item)
    {
        if (item is null || ReferenceEquals(item, SelectedDictionary))
        {
            return;
        }

        if (!await EnsureCanDiscardAllChangesAsync("切换字典会放弃当前未保存的修改。"))
        {
            return;
        }

        await RunBusyAsync(() => ActivateDictionaryAsync(item), "正在加载字典...");
    }

    [RelayCommand(CanExecute = nameof(CanAddDictionaryType))]
    private async Task AddDictionaryTypeAsync()
    {
        if (!await EnsureCanDiscardAllChangesAsync("新增字典类型会放弃当前未保存的修改。"))
        {
            return;
        }

        _selectionBeforeNew = SelectedDictionary?.Code;
        SetSelectedDictionary(null);
        Values.ClearContext();
        SelectedLine = null;

        var editor = new DictionaryTypeEditModel();
        editor.Load(new SysDictTypeDto
        {
            Uid = Guid.Empty,
            DictCode = string.Empty,
            DictName = string.Empty,
            SortOrder = Dictionaries
                .Where(item => item.Data is not null)
                .Select(item => item.Data!.SortOrder)
                .DefaultIfEmpty()
                .Max() + 10,
            IsSystem = false
        });
        editor.MarkAsAdded();
        SetTypeEditor(editor);
        ResultSummary = "请先保存新字典类型，再维护字典值。";
        SetValidationFeedback(null);
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSaveDictionaryType))]
    private async Task SaveDictionaryTypeAsync()
    {
        if (!CanSaveDictionaryType() || Values.CommitPendingEdits?.Invoke() == false) return;
        var editor = TypeEditor;
        if (editor is null || editor.IsProtected)
        {
            return;
        }

        await RunDictionaryWriteAsync(async () =>
        {
            var validationError = editor.ValidateRow();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SetValidationFeedback(validationError);
                _messageService.ShowWarning("字典类型未保存", validationError);
                return;
            }

            if (DictCodes.DictCodeList.Contains(editor.DictCode, StringComparer.OrdinalIgnoreCase))
            {
                const string reservedCodeError = "系统字典编码为保留编码，不能用于自定义字典类型。";
                SetValidationFeedback(reservedCodeError);
                _messageService.ShowWarning("字典类型未保存", reservedCodeError);
                return;
            }

            var dto = editor.ToDto();
            var request = new SysDictTypeBatchSaveRequest();
            if (editor.IsNew)
            {
                request.CreateItems.Add(new CreateItemRequest<SysDictTypeDto>
                {
                    ClientKey = $"new-type-{Guid.NewGuid():N}",
                    RowIndex = 1,
                    Data = dto
                });
            }
            else
            {
                request.UpdateItems.Add(new UpdateItemRequest<SysDictTypeDto>
                {
                    ClientKey = editor.Uid.ToString("N"),
                    RowIndex = 1,
                    Uid = editor.Uid,
                    Version = editor.Version,
                    Data = dto
                });
            }

            var result = await _apiCallRunner.RunAsync(
                () => _dictTypeApi.BatchSaveAsync(request),
                "保存字典类型失败");
            if (!IsCurrentSession) return;
            if (!result.Success)
            {
                SetValidationFeedback(result.Message);
                _messageService.ShowError("保存字典类型失败", result.Message);
                return;
            }

            _messageService.ShowSuccess("字典类型已保存", $"{dto.DictName} 已保存。");
            _selectionBeforeNew = null;
            await LoadDictionaryTypesAsync(dto.DictCode);
            await RefreshDictionaryCacheAfterSaveAsync("字典类型已保存，但缓存刷新失败");
        }, "正在保存字典类型...");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDictionaryType))]
    private async Task DeleteDictionaryTypeAsync()
    {
        if (!CanDeleteDictionaryType() || Values.CommitPendingEdits?.Invoke() == false) return;
        var editor = TypeEditor;
        if (editor is null || editor.IsProtected)
        {
            return;
        }

        if (editor.IsNew)
        {
            await CancelNewDictionaryTypeAsync();
            return;
        }

        if (Lines.HasChanges
            && !await EnsureCanDiscardValueChangesAsync("删除字典类型会放弃当前字典值的未保存修改。"))
        {
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "删除字典类型",
            $"确定删除“{editor.DictName}”吗？存在字典值或子类型时服务端会拒绝删除。",
            "删除");
        if (!confirmed)
        {
            return;
        }

        await RunDictionaryWriteAsync(async () =>
        {
            var request = new SysDictTypeBatchSaveRequest
            {
                DeleteItems =
                [
                    new DeleteItemRequest
                    {
                        ClientKey = editor.Uid.ToString("N"),
                        RowIndex = 1,
                        Uid = editor.Uid,
                        Version = editor.Version,
                        IsSoftDelete = true
                    }
                ]
            };
            var result = await _apiCallRunner.RunAsync(
                () => _dictTypeApi.BatchSaveAsync(request),
                "删除字典类型失败");
            if (!IsCurrentSession) return;
            if (!result.Success)
            {
                SetValidationFeedback(result.Message);
                _messageService.ShowError("删除字典类型失败", result.Message);
                return;
            }

            _messageService.ShowSuccess("字典类型已删除", $"{editor.DictName} 已删除。");
            await LoadDictionaryTypesAsync();
            await RefreshDictionaryCacheAfterSaveAsync("字典类型已删除，但缓存刷新失败");
        }, "正在删除字典类型...");
    }

    [RelayCommand(CanExecute = nameof(CanRejectDictionaryTypeChanges))]
    private async Task RejectDictionaryTypeChangesAsync()
    {
        var editor = TypeEditor;
        if (editor is null)
        {
            return;
        }

        if (editor.IsNew)
        {
            await CancelNewDictionaryTypeAsync();
            return;
        }

        editor.RejectChanges();
        SetValidationFeedback(null);
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!await EnsureCanDiscardAllChangesAsync("刷新会放弃当前未保存的修改。"))
        {
            return;
        }

        var selectedCode = SelectedDictionary?.Code;
        await RunBusyAsync(() => LoadDictionaryTypesAsync(selectedCode), "正在刷新字典...");
    }

    /// <summary>使用服务端完整类型清单；内置类型的名称、身份和缺失回退均由服务端提供。</summary>
    private async Task LoadDictionaryTypesAsync(string? preferredCode = null)
    {
        var (version, cancellationToken) = BeginQuery();
        var result = await _apiCallRunner.RunAsync(
            () => _dictTypeApi.GetAllAsync().WaitAsync(cancellationToken),
            cancellationToken,
            "加载字典类型失败");
        if (!CanAcceptQuery(version, cancellationToken))
        {
            return;
        }

        if (!result.Success)
        {
            SetValidationFeedback(result.Message);
            ResultSummary = "字典类型加载失败。";
            _messageService.ShowError("加载字典类型失败", result.Message);
            return;
        }

        var allTypes = result.Data ?? [];
        Dictionaries.Clear();

        var visibleTypes = allTypes
            // 旧品质分类数据保留供迁移追溯，维护入口已由独立分类树接管，不能从附加类型重新出现。
            .Where(type => !string.Equals(type.DictCode?.TrimEnd(' '), DictCodes.AfterSalesQualityCategory,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(type => type.IsBuiltIn)
            .ThenBy(type => type.SortOrder)
            .ThenBy(type => type.DictCode, StringComparer.OrdinalIgnoreCase);
        foreach (var type in visibleTypes)
        {
            Dictionaries.Add(new DictionaryTypeItemModel(type));
        }

        var selected = Dictionaries.FirstOrDefault(item =>
                string.Equals(item.Code, preferredCode, StringComparison.OrdinalIgnoreCase))
            ?? Dictionaries.FirstOrDefault();
        if (selected is not null)
        {
            await ActivateDictionaryAsync(selected);
        }
        else
        {
            SetSelectedDictionary(null);
            SetTypeEditor(null);
            Values.ClearContext();
            ResultSummary = "暂无字典类型。";
            UpdateChangeState();
        }
    }

    /// <summary>选择有效类型并加载明细；不因类型尚无数据库记录而阻断维护。</summary>
    private async Task ActivateDictionaryAsync(DictionaryTypeItemModel item)
    {
        SetSelectedDictionary(item);
        Values.ClearContext();
        SelectedLine = null;

        var editor = new DictionaryTypeEditModel();
        editor.Load(item.Data);
        SetTypeEditor(editor);
        await LoadDictionaryDataAsync(item);
    }

    /// <summary>激活独立明细工作区，不复制 API、保存或删除池逻辑。</summary>
    private async Task LoadDictionaryDataAsync(DictionaryTypeItemModel item)
    {
        await Values.ActivateAsync(item.Code, item.Name);
        ResultSummary = Values.StatusMessage;
        UpdateChangeState();
    }

    private async Task CancelNewDictionaryTypeAsync()
    {
        var preferred = _selectionBeforeNew;
        _selectionBeforeNew = null;
        await RunBusyAsync(() => LoadDictionaryTypesAsync(preferred), "正在恢复字典列表...");
    }

    private async Task RefreshDictionaryCacheAfterSaveAsync(string warningTitle)
    {
        var cacheResult = await _referenceDataCache.RefreshDictionariesAsync();
        if (!cacheResult.Success)
        {
            _messageService.ShowWarning(warningTitle, cacheResult.Message);
        }
    }

    private async Task<bool> EnsureCanDiscardAllChangesAsync(string message)
    {
        if (IsBusy || Values.IsWorking || Values.CommitPendingEdits?.Invoke() == false) return false;
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "放弃未保存变更",
            message,
            "放弃变更");
    }

    private async Task<bool> EnsureCanDiscardValueChangesAsync(string message)
    {
        if (IsBusy || Values.IsWorking || Values.CommitPendingEdits?.Invoke() == false) return false;
        if (!Lines.HasChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "放弃字典值变更",
            message,
            "放弃变更");
    }

    private void SetSelectedDictionary(DictionaryTypeItemModel? selected)
    {
        foreach (var dictionary in Dictionaries)
        {
            dictionary.IsSelected = ReferenceEquals(dictionary, selected);
        }

        SelectedDictionary = selected;
    }

    private void SetTypeEditor(DictionaryTypeEditModel? editor)
    {
        if (TypeEditor is not null)
        {
            TypeEditor.PropertyChanged -= OnTypeEditorPropertyChanged;
        }

        TypeEditor = editor;
        if (TypeEditor is not null)
        {
            TypeEditor.PropertyChanged += OnTypeEditorPropertyChanged;
        }
    }

    private void SetValidationFeedback(string? message)
    {
        ValidationSummary = string.IsNullOrWhiteSpace(message) ? null : message;
        OnPropertyChanged(nameof(HasValidationError));
    }

    private void UpdateChangeState()
    {
        Values.ContextEditable = CanEditValues;
        Values.HostBusy = IsBusy || IsTypeWriting;
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanEditValues));
        OnPropertyChanged(nameof(IsValueEditorReadOnly));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsWriting));
        AddDictionaryTypeCommand.NotifyCanExecuteChanged();
        SelectDictionaryCommand.NotifyCanExecuteChanged();
        SaveDictionaryTypeCommand.NotifyCanExecuteChanged();
        DeleteDictionaryTypeCommand.NotifyCanExecuteChanged();
        RejectDictionaryTypeChangesCommand.NotifyCanExecuteChanged();
        AddRowCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RejectAllChangesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void OnValuesChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DictionaryDataEditorViewModel.ValidationSummary))
            SetValidationFeedback(Values.ValidationSummary);
        if (args.PropertyName == nameof(DictionaryDataEditorViewModel.StatusMessage)) ResultSummary = Values.StatusMessage;
        if (args.PropertyName is nameof(DictionaryDataEditorViewModel.HasUnsavedChanges) or nameof(DictionaryDataEditorViewModel.IsWorking)
            or nameof(DictionaryDataEditorViewModel.IsWriting)) UpdateChangeState();
    }

    private void OnOwnStateChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(IsBusy) or nameof(IsTypeWriting)) UpdateChangeState(); }

    private void OnTypeEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DictionaryTypeEditModel.State)
            or nameof(DictionaryTypeEditModel.IsDirty)
            or nameof(DictionaryTypeEditModel.IsProtected)
            or nameof(DictionaryTypeEditModel.DictCode)
            or nameof(DictionaryTypeEditModel.DictName)
            or nameof(DictionaryTypeEditModel.SortOrder)
            or nameof(DictionaryTypeEditModel.Remark))
        {
            UpdateChangeState();
        }
    }

    private bool CanSelectDictionary(DictionaryTypeItemModel? item) => item is not null && IsNotBusy && !Values.IsWorking && IsCurrentSession;

    private bool CanAddDictionaryType() => CanWrite && IsNotBusy && !Values.IsWorking && HasDictionaryPermission("Create");

    private bool CanSaveDictionaryType() => CanWrite
        && IsNotBusy && !Values.IsWorking
        && TypeEditor is { IsDirty: true, IsProtected: false } editor
        && HasDictionaryPermission(editor.IsNew ? "Create" : "Edit");

    private bool CanDeleteDictionaryType() => CanWrite
        && IsNotBusy && !Values.IsWorking
        && TypeEditor is { IsProtected: false } editor
        && (editor.IsNew || HasDictionaryPermission("Delete"));

    private bool CanRejectDictionaryTypeChanges() => IsNotBusy && !Values.IsWorking && TypeEditor?.IsDirty == true;

    /// <summary>以当前 me 快照控制字典写按钮；后端仍逐操作重验授权。</summary>
    private bool HasDictionaryPermission(string operation) => IsCurrentSession && _currentUser.HasPermission($"System.Dictionary.{operation}");

    private bool IsCurrentSession => !_isDisposed && !_sessionEnded && _currentUser.IsAuthenticated
        && _currentUser.CurrentUser?.UserId == _sessionUserId && _currentUser.HasPermission("System.Dictionary.View");

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.CurrentUser?.UserId != _sessionUserId) _sessionEnded = true;
        if (!IsCurrentSession) { _queryCancellation?.Cancel(); Interlocked.Increment(ref _queryVersion); }
        UpdateChangeState();
    }

    private bool CanRefresh() => IsNotBusy && !Values.IsWorking && IsCurrentSession;

    private async Task RunDictionaryWriteAsync(Func<Task> action, string busyMessage)
    {
        if (IsWriting)
        {
            return;
        }

        IsTypeWriting = true;
        try
        {
            await RunWriteAsync(action, busyMessage);
        }
        finally
        {
            IsTypeWriting = false;
        }
    }

    private (long Version, CancellationToken Token) BeginQuery()
    {
        var version = Interlocked.Increment(ref _queryVersion);
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var previous = Interlocked.Exchange(ref _queryCancellation, next);
        previous?.Cancel();
        previous?.Dispose();
        return (version, next.Token);
    }

    private bool CanAcceptQuery(long version, CancellationToken token)
    {
        return IsCurrentSession
            && !token.IsCancellationRequested
            && version == Volatile.Read(ref _queryVersion);
    }

    /// <summary>取消字典读取并解除模型订阅，防止关闭后迟到结果回写窗口或新会话。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        _lifetimeCancellation.Cancel();
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = null;
        _lifetimeCancellation.Dispose();
        Values.PropertyChanged -= OnValuesChanged;
        Values.Dispose();
        PropertyChanged -= OnOwnStateChanged;
        if (TypeEditor is not null)
        {
            TypeEditor.PropertyChanged -= OnTypeEditorPropertyChanged;
        }
    }

}
