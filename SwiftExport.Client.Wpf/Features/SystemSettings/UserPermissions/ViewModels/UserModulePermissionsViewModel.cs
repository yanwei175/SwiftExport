using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.UserPermissions.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.UserPermissions.ViewModels;

/// <summary>读取普通用户的直接模块权限，并以完整集合执行原子替换。</summary>
public sealed partial class UserModulePermissionsViewModel
    : ViewModelBaseEx, IAsyncInitializable, IDocumentTabViewModel, IDisposable
{
    private readonly IAuthApi _authApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IDialogService _dialogService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _selectionCancellation;
    private long _selectionVersion;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _isApplyingModules;

    /// <summary>创建用户模块权限整组维护用例。</summary>
    public UserModulePermissionsViewModel(
        IAuthApi authApi,
        IApiCallRunner apiCallRunner,
        IDialogService dialogService)
    {
        _authApi = authApi;
        _apiCallRunner = apiCallRunner;
        _dialogService = dialogService;
        Title = "模块权限";
        Modules = CreateModuleOptions();
        foreach (var module in Modules)
        {
            module.PropertyChanged += OnModulePropertyChanged;
        }

        RegisterBusyAwareCommand(SelectUserCommand);
        RegisterBusyAwareCommand(SaveCommand);
    }

    /// <summary>管理员可维护的最小用户列表。</summary>
    public ObservableCollection<UserSummaryDto> Users { get; } = [];

    /// <summary>可直接分配的一级模块和供应商虚拟仓独立管理能力。</summary>
    public IReadOnlyList<ModulePermissionOptionModel> Modules { get; }

    /// <summary>供统一未保存检查显示的窗口标题。</summary>
    public string DocumentTitle => Title;

    /// <summary>指示当前勾选是否尚未保存。</summary>
    public bool HasUnsavedChanges => IsDirty;

    /// <summary>获取当前已确认选择的目标用户。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetEditable))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial UserSummaryDto? SelectedUser { get; private set; }

    /// <summary>获取当前操作提示或错误消息。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "请选择用户查看模块权限。";

    /// <summary>指示当前状态消息是否为错误。</summary>
    [ObservableProperty]
    public partial bool IsError { get; private set; }

    /// <summary>指示模块整组保存是否正在执行；执行期间窗口不能关闭。</summary>
    [ObservableProperty]
    public partial bool IsWriting { get; private set; }

    /// <summary>管理员账号固定拥有全部模块，因此只能查看、不能编辑。</summary>
    public bool IsTargetEditable => SelectedUser is { IsAdmin: false };

    /// <summary>幂等加载用户列表，并默认选择首个普通用户。</summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _isInitialized = true;
        await RunBusyAsync(async () =>
        {
            var token = _lifetimeCancellation.Token;
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.GetUsersAsync().WaitAsync(token),
                token,
                "加载用户失败");
            if (!CanAccept(token))
            {
                return;
            }

            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            Users.Clear();
            foreach (var user in result.Data ?? [])
            {
                Users.Add(user);
            }
        }, "正在加载用户...");

        var initialUser = Users.FirstOrDefault(user => !user.IsAdmin) ?? Users.FirstOrDefault();
        if (initialUser is not null && !_isDisposed)
        {
            await SelectUserAsync(initialUser);
        }
        else if (!_isDisposed)
        {
            SetStatus("当前没有可配置的用户。", isError: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectUser))]
    private async Task SelectUserAsync(UserSummaryDto? user)
    {
        if (user is null || _isDisposed || ReferenceEquals(user, SelectedUser))
        {
            return;
        }

        if (HasUnsavedChanges && !await _dialogService.ConfirmAsync(
                "放弃未保存变更",
                "切换用户会放弃当前模块勾选，是否继续？",
                "放弃并切换"))
        {
            return;
        }

        SelectedUser = user;
        SetAllModules([]);
        IsDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));

        var version = Interlocked.Increment(ref _selectionVersion);
        var cancellation = ReplaceSelectionCancellation();
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.GetUserModulesAsync(user.Uid).WaitAsync(cancellation.Token),
                cancellation.Token,
                "加载模块权限失败");
            if (!CanAccept(cancellation.Token) || version != Volatile.Read(ref _selectionVersion))
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            SetAllModules(result.Data.ModulePermissions);
            IsDirty = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            SetStatus(user.IsAdmin
                ? "管理员权限由服务端固定投影，此处不可修改。"
                : result.Data.ModulePermissions.Count == 0
                    ? "该用户当前没有模块权限，保存前请至少确认所需模块。"
                    : "模块变更会在目标用户下次登录后更新界面。", isError: false);
        }, "正在加载模块权限...");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var user = SelectedUser;
        if (user is null || user.IsAdmin)
        {
            return;
        }

        var selectedKeys = Modules
            .Where(module => module.IsSelected)
            .Select(module => module.PermissionKey)
            .ToList();
        await RunModuleWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.SaveUserModulesAsync(
                    user.Uid,
                    new SaveUserModulePermissionsRequestDto
                    {
                        ModulePermissions = selectedKeys
                    }),
                "保存模块权限失败");
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            SetAllModules(result.Data?.ModulePermissions ?? selectedKeys);
            IsDirty = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            SetStatus("模块权限已保存，将在目标用户下次登录后生效。", isError: false);
        }, "正在保存模块权限...");
    }

    /// <summary>关闭窗口前确认是否放弃尚未保存的勾选。</summary>
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        return await _dialogService.ConfirmAsync(
            "放弃未保存变更",
            "模块权限尚未保存，确定关闭窗口吗？",
            "放弃并关闭",
            cancellationToken: cancellationToken);
    }

    private bool CanSelectUser(UserSummaryDto? user) => IsNotBusy && user is not null;

    private bool CanSave() => IsNotBusy && IsTargetEditable && IsDirty;

    private async Task RunModuleWriteAsync(Func<Task> action, string busyMessage)
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

    private void SetAllModules(IEnumerable<string> selectedKeys)
    {
        var selected = selectedKeys.ToHashSet(StringComparer.Ordinal);
        _isApplyingModules = true;
        try
        {
            foreach (var module in Modules)
            {
                module.IsSelected = selected.Contains(module.PermissionKey);
            }
        }
        finally
        {
            _isApplyingModules = false;
        }
    }

    private CancellationTokenSource ReplaceSelectionCancellation()
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var previous = Interlocked.Exchange(ref _selectionCancellation, next);
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    private bool CanAccept(CancellationToken token)
        => !_isDisposed && !token.IsCancellationRequested;

    private void OnModulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingModules
            || e.PropertyName != nameof(ModulePermissionOptionModel.IsSelected)
            || !IsTargetEditable)
        {
            return;
        }

        IsDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUserChanged(UserSummaryDto? value)
    {
        OnPropertyChanged(nameof(IsTargetEditable));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsError = isError;
        ValidationSummary = isError ? message : null;
    }

    private static IReadOnlyList<ModulePermissionOptionModel> CreateModuleOptions()
    {
        return
        [
            new(PermissionKeys.ModuleProductsAccess, "产品"),
            new(PermissionKeys.ModuleCustomersAccess, "客户"),
            new(PermissionKeys.ModuleSuppliersAccess, "供应商"),
            new(PermissionKeys.ModuleSalesAccess, "销售"),
            new(PermissionKeys.ModuleDemandPlanningAccess, "需求计划"),
            new(PermissionKeys.ModuleProcurementAccess, "采购"),
            new(PermissionKeys.ModuleInventoryAccess, "库存"),
            new(PermissionKeys.InventoryWarehouseManage, "库存 - 供应商虚拟仓管理"),
            new(PermissionKeys.InventoryInboundPost, "库存 - 入库过账"),
            new(PermissionKeys.InventoryInboundReverse, "库存 - 入库反过账"),
            new(PermissionKeys.InventoryOutboundPost, "库存 - 出库过账"),
            new(PermissionKeys.InventoryOutboundReverse, "库存 - 手工出库反过账"),
            new(PermissionKeys.ModuleShippingAccess, "出货")
        ];
    }

    /// <summary>取消用户和模块读取，解除模块事件并阻止迟到结果污染后续会话。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetimeCancellation.Cancel();
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        _lifetimeCancellation.Dispose();
        foreach (var module in Modules)
        {
            module.PropertyChanged -= OnModulePropertyChanged;
        }
    }
}
