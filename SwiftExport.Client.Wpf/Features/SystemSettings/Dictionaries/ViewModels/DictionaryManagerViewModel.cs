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
    private readonly ISysDictDataApi _dictDataApi;
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

    public DictionaryManagerViewModel(
        ISysDictTypeApi dictTypeApi,
        ISysDictDataApi dictDataApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService,
        IDialogService dialogService,
        IReferenceDataCacheService referenceDataCache,
        ICurrentUserService currentUser)
    {
        _dictTypeApi = dictTypeApi;
        _dictDataApi = dictDataApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        _dialogService = dialogService;
        _referenceDataCache = referenceDataCache;
        _currentUser = currentUser;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;

        Title = "字典管理";
        Lines = new BaseListModel<DictionaryDataLineModel, SysDictDataDto>(
            _ => new DictionaryDataLineModel());
        Lines.PropertyChanged += OnLinesPropertyChanged;

        RegisterBusyAwareCommand(SelectDictionaryCommand);
        RegisterBusyAwareCommand(AddDictionaryTypeCommand);
        RegisterBusyAwareCommand(SaveDictionaryTypeCommand);
        RegisterBusyAwareCommand(DeleteDictionaryTypeCommand);
        RegisterBusyAwareCommand(RejectDictionaryTypeChangesCommand);
        RegisterBusyAwareCommand(AddRowCommand);
        RegisterBusyAwareCommand(DeleteSelectedCommand);
        RegisterBusyAwareCommand(SaveCommand);
        RegisterBusyAwareCommand(RejectAllChangesCommand);
        RegisterBusyAwareCommand(RefreshCommand);
    }

    /// <summary>获取可选择的字典类型集合。</summary>
    public ObservableCollection<DictionaryTypeItemModel> Dictionaries { get; } = [];

    /// <summary>获取当前字典的可编辑值列表。</summary>
    public BaseListModel<DictionaryDataLineModel, SysDictDataDto> Lines { get; }

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial DictionaryDataLineModel? SelectedLine { get; set; }

    [ObservableProperty]
    public partial string ResultSummary { get; set; } = "尚未加载字典。";

    public string DocumentTitle => Title;

    public bool HasUnsavedChanges => Lines.HasChanges || TypeEditor?.IsDirty == true;

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationSummary);

    public bool HasTypeEditor => TypeEditor is not null;

    public bool IsTypeEditorReadOnly => TypeEditor?.IsProtected != false;

    public bool CanEditValues => SelectedDictionary?.IsConfigured == true
        && TypeEditor is { IsNew: false, IsDirty: false };

    public bool IsValueEditorReadOnly => !CanEditValues
        || !(_currentUser.HasPermission("System.Dictionary.Create") || _currentUser.HasPermission("System.Dictionary.Edit"));

    public bool IsEmpty => CanEditValues && !IsBusy && Lines.Items.Count == 0;

    /// <summary>指示字典写入是否正在执行；执行期间独立窗口不能关闭。</summary>
    [ObservableProperty]
    public partial bool IsWriting { get; private set; }

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
        if (!HasUnsavedChanges) return true;
        if (!await _dialogService.ConfirmAsync("放弃未保存变更",
                "字典类型或字典值有未保存的修改，确定离开页面吗？", "放弃并离开",
                cancellationToken: cancellationToken)) return false;

        if (TypeEditor?.IsNew == true)
        {
            SetTypeEditor(null);
            Lines.Clear();
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
        Lines.Clear();
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

    [RelayCommand(CanExecute = nameof(CanAddDictionaryValue))]
    private void AddRow()
    {
        var dictionary = SelectedDictionary;
        if (dictionary is null)
        {
            return;
        }

        var line = Lines.AddNew(new SysDictDataDto
        {
            Uid = Guid.Empty,
            DictCode = dictionary.Code,
            DataNameEn = string.Empty,
            DataNameZh = string.Empty,
            SortOrder = (Lines.Items.Max(x => x.SortOrder) ?? 0) + 10,
            IsEnabled = true,
            IsSystem = false
        });

        SelectedLine = line;
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var line = SelectedLine;
        if (line is null || line.IsSystem)
        {
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(line.DataNameZh)
            ? line.DataNameEn
            : line.DataNameZh;
        var confirmed = await _dialogService.ConfirmAsync(
            "删除字典值",
            $"确定删除“{displayName}”吗？保存前可以撤销。",
            "删除");
        if (!confirmed)
        {
            return;
        }

        Lines.Remove(line);
        SelectedLine = Lines.Items.FirstOrDefault();
        UpdateChangeState();
    }

    [RelayCommand(CanExecute = nameof(CanSaveValues))]
    private async Task SaveAsync()
    {
        await RunDictionaryWriteAsync(async () =>
        {
            var dictionary = SelectedDictionary;
            if (dictionary is null || !CanEditValues)
            {
                return;
            }

            var validationError = await Lines.ValidateAllAsync();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SetValidationFeedback(validationError);
                _messageService.ShowWarning("字典值未保存", validationError);
                return;
            }

            var request = new SysDictDataBatchSaveRequest
            {
                CreateItems = Lines.AddedItems.Select(line =>
                {
                    var dto = line.ToDto();
                    dto.DictCode = dictionary.Code;
                    return new CreateItemRequest<SysDictDataDto>
                    {
                        ClientKey = $"new-{line.RowIndex}-{Guid.NewGuid():N}",
                        RowIndex = line.RowIndex,
                        Data = dto
                    };
                }).ToList(),
                UpdateItems = Lines.ModifiedItems.Select(line =>
                {
                    var dto = line.ToDto();
                    dto.DictCode = dictionary.Code;
                    return new UpdateItemRequest<SysDictDataDto>
                    {
                        ClientKey = line.Uid.ToString("N"),
                        RowIndex = line.RowIndex,
                        Uid = line.Uid,
                        Version = line.Version,
                        Data = dto
                    };
                }).ToList(),
                DeleteItems = Lines.DeletedItems
                    .Where(line => line.Uid != Guid.Empty)
                    .Select(line => new DeleteItemRequest
                    {
                        ClientKey = line.Uid.ToString("N"),
                        RowIndex = line.RowIndex,
                        Uid = line.Uid,
                        Version = line.Version,
                        IsSoftDelete = true
                    }).ToList()
            };

            var result = await _apiCallRunner.RunAsync(
                () => _dictDataApi.BatchSaveAsync(request),
                "保存字典值失败");
            if (!result.Success)
            {
                SetValidationFeedback(result.Message);
                _messageService.ShowError("保存字典值失败", result.Message);
                return;
            }

            _messageService.ShowSuccess(
                "字典值已保存",
                $"已提交 {result.Data?.SuccessCount ?? 0} 条变更。");
            await LoadDictionaryDataAsync(dictionary);
            await RefreshDictionaryCacheAfterSaveAsync("字典值已保存，但缓存刷新失败");
        }, "正在保存字典值...");
    }

    [RelayCommand(CanExecute = nameof(CanRejectValueChanges))]
    private void RejectAllChanges()
    {
        Lines.RejectChanges();
        SelectedLine = Lines.Items.FirstOrDefault();
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

        foreach (var code in DictCodes.DictCodeList)
        {
            var type = allTypes.FirstOrDefault(item =>
                string.Equals(item.DictCode, code, StringComparison.OrdinalIgnoreCase));
            Dictionaries.Add(new DictionaryTypeItemModel(code, GetFallbackName(code), type));
        }

        var additionalTypes = allTypes
            .Where(type => !DictCodes.DictCodeList.Contains(type.DictCode, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(type => type.IsSystem)
            .ThenBy(type => type.DictName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(type => type.DictCode, StringComparer.OrdinalIgnoreCase);
        foreach (var type in additionalTypes)
        {
            Dictionaries.Add(new DictionaryTypeItemModel(type.DictCode, type.DictName, type));
        }

        var selected = Dictionaries.FirstOrDefault(item =>
                string.Equals(item.Code, preferredCode, StringComparison.OrdinalIgnoreCase))
            ?? Dictionaries.FirstOrDefault(item => item.IsConfigured)
            ?? Dictionaries.FirstOrDefault();
        if (selected is not null)
        {
            await ActivateDictionaryAsync(selected);
        }
        else
        {
            SetSelectedDictionary(null);
            SetTypeEditor(null);
            Lines.Clear();
            ResultSummary = "暂无字典类型。";
            UpdateChangeState();
        }
    }

    private async Task ActivateDictionaryAsync(DictionaryTypeItemModel item)
    {
        SetSelectedDictionary(item);
        Lines.Clear();
        SelectedLine = null;

        if (!item.IsConfigured)
        {
            SetTypeEditor(null);
            SetValidationFeedback($"系统字典类型 {item.Code} 尚未配置，请联系管理员处理。");
            ResultSummary = "字典类型未配置。";
            UpdateChangeState();
            return;
        }

        var editor = new DictionaryTypeEditModel();
        editor.Load(item.Data!);
        SetTypeEditor(editor);
        await LoadDictionaryDataAsync(item);
    }

    private async Task LoadDictionaryDataAsync(DictionaryTypeItemModel item)
    {
        var (version, cancellationToken) = BeginQuery();
        var result = await _apiCallRunner.RunAsync(
            () => _dictDataApi.GetByCodeAsync(item.Code).WaitAsync(cancellationToken),
            cancellationToken,
            "加载字典值失败");
        if (!CanAcceptQuery(version, cancellationToken))
        {
            return;
        }

        if (!result.Success)
        {
            Lines.Clear();
            SelectedLine = null;
            SetValidationFeedback(result.Message);
            ResultSummary = $"{item.Name}加载失败。";
            _messageService.ShowError("加载字典值失败", result.Message);
            UpdateChangeState();
            return;
        }

        Lines.LoadFromDtos(result.Data ?? []);
        SelectedLine = Lines.Items.FirstOrDefault();
        SetValidationFeedback(null);
        ResultSummary = $"{item.Name}共 {Lines.Items.Count} 个字典值。";
        AcceptChanges();
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
        IsDirty = HasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanEditValues));
        OnPropertyChanged(nameof(IsValueEditorReadOnly));
        OnPropertyChanged(nameof(IsEmpty));
        AddDictionaryTypeCommand.NotifyCanExecuteChanged();
        SaveDictionaryTypeCommand.NotifyCanExecuteChanged();
        DeleteDictionaryTypeCommand.NotifyCanExecuteChanged();
        RejectDictionaryTypeChangesCommand.NotifyCanExecuteChanged();
        AddRowCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RejectAllChangesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnLinesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BaseListModel<DictionaryDataLineModel, SysDictDataDto>.HasChanges)
            or nameof(BaseListModel<DictionaryDataLineModel, SysDictDataDto>.CheckedCount))
        {
            UpdateChangeState();
        }
    }

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

    partial void OnSelectedLineChanged(DictionaryDataLineModel? value)
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanSelectDictionary(DictionaryTypeItemModel? item) => item is not null && IsNotBusy;

    private bool CanAddDictionaryType() => CanWrite && IsNotBusy && HasDictionaryPermission("Create");

    private bool CanSaveDictionaryType() => CanWrite
        && IsNotBusy
        && TypeEditor is { IsDirty: true, IsProtected: false } editor
        && HasDictionaryPermission(editor.IsNew ? "Create" : "Edit");

    private bool CanDeleteDictionaryType() => CanWrite
        && IsNotBusy
        && TypeEditor is { IsProtected: false } editor
        && (editor.IsNew || HasDictionaryPermission("Delete"));

    private bool CanRejectDictionaryTypeChanges() => IsNotBusy && TypeEditor?.IsDirty == true;

    private bool CanWriteDictionaryValues() => CanWrite && IsNotBusy && CanEditValues;

    /// <summary>字典值新建只要求创建能力，避免将读取或编辑误当新增权限。</summary>
    private bool CanAddDictionaryValue() => CanWriteDictionaryValues() && HasDictionaryPermission("Create");

    private bool CanDeleteSelected() => CanWriteDictionaryValues() && HasDictionaryPermission("Delete")
        && SelectedLine is { IsSystem: false };

    private bool CanSaveValues() => CanWriteDictionaryValues() && Lines.HasChanges
        && (!Lines.AddedItems.Any() || HasDictionaryPermission("Create"))
        && (!Lines.ModifiedItems.Any() || HasDictionaryPermission("Edit"))
        && (!Lines.DeletedItems.Any(item => item.Uid != Guid.Empty) || HasDictionaryPermission("Delete"));

    /// <summary>以当前 me 快照控制字典写按钮；后端仍逐操作重验授权。</summary>
    private bool HasDictionaryPermission(string operation) => _currentUser.HasPermission($"System.Dictionary.{operation}");

    private void OnCurrentUserChanged(object? sender, EventArgs e) => UpdateChangeState();

    private bool CanRejectValueChanges() => IsNotBusy && Lines.HasChanges;

    private bool CanRefresh() => IsNotBusy;

    private async Task RunDictionaryWriteAsync(Func<Task> action, string busyMessage)
    {
        if (IsWriting)
        {
            return;
        }

        IsWriting = true;
        try
        {
            await RunWriteAsync(action, busyMessage);
        }
        finally
        {
            IsWriting = false;
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
        return !_isDisposed
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
        Lines.PropertyChanged -= OnLinesPropertyChanged;
        if (TypeEditor is not null)
        {
            TypeEditor.PropertyChanged -= OnTypeEditorPropertyChanged;
        }
    }

    private static string GetFallbackName(string code) => code switch
    {
        DictCodes.CustomerType => "客户类型",
        DictCodes.Incoterm => "国际贸易术语",
        DictCodes.PaymentMethod => "付款方式",
        DictCodes.ShippedBy => "运输方式",
        DictCodes.Unit => "计量单位",
        _ => code
    };
}
