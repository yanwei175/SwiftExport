using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

/// <summary>编排用户、角色和权限目录管理，按聚合版本保存完整关系并隔离会话异步结果。</summary>
public sealed partial class RbacManagementViewModel : ViewModelBaseEx, IDisposable
{
    private readonly ISystemRbacApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private CancellationTokenSource? _selection;
    private readonly string? _sessionUserId;
    private HashSet<string> _permissionSnapshot;
    private long _accessRevision;
    private long _activeWriteRevision;
    private int _selectionVersion;
    private int _userVersion;
    private int _roleVersion;
    private bool _applying;
    private bool _initialized;
    private bool _usersLoaded;
    private bool _rolesLoaded;
    private bool _permissionsLoaded;
    private bool _disposed;
    private bool _userProfileDirty;
    private bool _userRolesDirty;
    private bool _roleProfileDirty;
    private bool _rolePermissionsDirty;
    private bool _newRoleDirty;
    private long _userProfileRevision;
    private long _userRolesRevision;
    private long _roleProfileRevision;
    private long _rolePermissionsRevision;

    /// <summary>接入共享 Refit API、当前会话与窗口本地确认服务。</summary>
    public RbacManagementViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService authService,
        ICurrentUserService currentUser, IDialogService dialogs)
    {
        _api = api;
        _runner = runner;
        _authService = authService;
        _currentUser = currentUser;
        _dialogs = dialogs;
        _lifetimeToken = _lifetime.Token;
        _sessionUserId = currentUser.CurrentUser?.UserId;
        _permissionSnapshot = currentUser.Permissions.ToHashSet(StringComparer.Ordinal);
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        Title = "用户、角色与权限";
        foreach (var command in new IRelayCommand[] { SelectUserCommand, SelectRoleCommand, SaveUserRolesCommand,
                     SaveRolePermissionsCommand, SaveUserProfileCommand, SetUserStatusCommand, CreateRoleCommand,
                     SaveRoleProfileCommand, SetRoleStatusCommand, DeleteRoleCommand, RefreshCommand })
            RegisterBusyAwareCommand(command);
    }

    /// <summary>可选用户列表，含停用账号。</summary>
    public ObservableCollection<ManagedUserDto> Users { get; } = [];

    /// <summary>可选角色列表，含停用角色。</summary>
    public ObservableCollection<ManagedRoleDto> Roles { get; } = [];

    /// <summary>服务端只读权限定义。</summary>
    public ObservableCollection<ManagedPermissionDto> Permissions { get; } = [];

    /// <summary>当前用户的角色候选及勾选。</summary>
    public ObservableCollection<RbacChoiceModel> UserRoleChoices { get; } = [];

    /// <summary>当前普通角色的权限候选及勾选。</summary>
    public ObservableCollection<RbacChoiceModel> RolePermissionChoices { get; } = [];

    /// <summary>当前用户的有效权限键，只读展示。</summary>
    public ObservableCollection<string> EffectivePermissions { get; } = [];

    /// <summary>已确认选中的用户。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveUserRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveUserProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetUserStatusCommand))]
    public partial ManagedUserDto? SelectedUser { get; private set; }

    /// <summary>已确认选中的角色。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemRole))]
    [NotifyPropertyChangedFor(nameof(IsOrdinaryRole))]
    [NotifyCanExecuteChangedFor(nameof(SaveRolePermissionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveRoleProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetRoleStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRoleCommand))]
    public partial ManagedRoleDto? SelectedRole { get; private set; }

    /// <summary>内置管理员角色自动拥有全部系统权限。</summary>
    public bool IsSystemRole => SelectedRole?.IsSystem == true;

    /// <summary>普通角色才允许编辑权限关系和状态。</summary>
    public bool IsOrdinaryRole => SelectedRole is { IsSystem: false };

    /// <summary>当前角色已选权限数；管理员角色来自服务端自动目录。</summary>
    public int SelectedPermissionCount => RolePermissionChoices.Count(item => item.IsSelected);

    /// <summary>用户资料中的显示名称。</summary>
    [ObservableProperty]
    public partial string UserNickName { get; set; } = string.Empty;

    /// <summary>用户资料中的邮箱。</summary>
    [ObservableProperty]
    public partial string UserEmail { get; set; } = string.Empty;

    /// <summary>用户资料中的电话。</summary>
    [ObservableProperty]
    public partial string UserPhone { get; set; } = string.Empty;

    /// <summary>新角色稳定编码。</summary>
    [ObservableProperty]
    public partial string NewRoleCode { get; set; } = string.Empty;

    /// <summary>当前角色的显示名称。</summary>
    [ObservableProperty]
    public partial string RoleName { get; set; } = string.Empty;

    /// <summary>当前角色的说明。</summary>
    [ObservableProperty]
    public partial string RoleDescription { get; set; } = string.Empty;

    /// <summary>新角色独立的显示名称，不与现有角色资料共用脏状态。</summary>
    [ObservableProperty]
    public partial string NewRoleName { get; set; } = string.Empty;

    /// <summary>新角色独立的说明。</summary>
    [ObservableProperty]
    public partial string NewRoleDescription { get; set; } = string.Empty;

    /// <summary>当前操作的成功或错误说明。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "加载用户、角色和权限目录后可维护授权。";

    /// <summary>写命令执行期间拒绝窗口关闭和再次提交。</summary>
    [ObservableProperty]
    public partial bool IsWriting { get; private set; }

    /// <summary>任一保存范围仍有本地修改时保持关闭提示。</summary>
    public bool HasUnsavedChanges => IsDirty;

    /// <summary>供单个表单关闭门禁分别判断草稿，不把资料与授权合并保存。</summary>
    public bool HasUserProfileDraft => _userProfileDirty;
    /// <summary>用户角色整组替换表单的未保存状态。</summary>
    public bool HasUserRolesDraft => _userRolesDirty;
    /// <summary>角色资料表单的未保存状态。</summary>
    public bool HasRoleProfileDraft => _roleProfileDirty;
    /// <summary>角色权限整组替换表单的未保存状态。</summary>
    public bool HasRolePermissionsDraft => _rolePermissionsDirty;
    /// <summary>新角色资料表单的未保存状态。</summary>
    public bool HasNewRoleDraft => _newRoleDirty;
    /// <summary>每次后端确认写入后递增，供表单仅在保存成功后关闭。</summary>
    public long SuccessfulWriteRevision { get; private set; }

    /// <summary>首次加载完整分页结果和只读目录；会话切换时不接纳旧响应。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        await LoadListsAsync();
        if (CanAccept(_lifetimeToken))
        {
            if (Users.FirstOrDefault() is { } user) await SelectUserAsync(user);
            if (Roles.FirstOrDefault() is { } role) await SelectRoleAsync(role);
        }
    }

    /// <summary>只初始化用户页需要的数据；角色候选仅在当前身份有角色读取权时加载。</summary>
    public async Task InitializeUsersPageAsync()
    {
        if (_disposed || !_currentUser.HasPermission("System.Users.View")) return;
        if (!_usersLoaded) await LoadUsersAsync();
        if (_currentUser.HasPermission("System.Roles.View") && !_rolesLoaded) await LoadRolesAsync();
        if (CanAccept(_lifetimeToken) && SelectedUser is null && Users.FirstOrDefault() is { } user)
            await SelectUserAsync(user);
    }

    /// <summary>只初始化角色页及其权限候选，避免打开其他页面时请求管理员接口。</summary>
    public async Task InitializeRolesPageAsync()
    {
        if (_disposed || !_currentUser.HasPermission("System.Roles.View")) return;
        if (!_rolesLoaded) await LoadRolesAsync();
        if (_currentUser.HasPermission("System.Permissions.View") && !_permissionsLoaded)
            await LoadPermissionsAsync();
        if (CanAccept(_lifetimeToken) && SelectedRole is null && Roles.FirstOrDefault() is { } role)
            await SelectRoleAsync(role);
    }

    /// <summary>权限定义页仅查询只读目录，不触发用户和角色管理请求。</summary>
    public async Task InitializePermissionsPageAsync()
    {
        if (_disposed || !_currentUser.HasPermission("System.Permissions.View") || _permissionsLoaded) return;
        await LoadPermissionsAsync();
    }

    /// <summary>创建用户后重读用户列表并定位新用户；创建与授权仍是两个操作。</summary>
    public async Task RefreshUsersPageAsync(string? userName = null)
    {
        if (_disposed || IsWriting || !_currentUser.HasPermission("System.Users.View")
            || ((_userProfileDirty || _userRolesDirty) && !await ConfirmDiscardAsync())) return;
        _usersLoaded = false;
        await LoadUsersAsync();
        if (!CanAccept(_lifetimeToken)) return;
        var target = Users.FirstOrDefault(item => string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase))
            ?? Users.FirstOrDefault(item => item.Uid == SelectedUser?.Uid);
        if (target is null) return;
        SelectedUser = null;
        await SelectUserAsync(target);
    }

    /// <summary>确认放弃角色页草稿后重读角色与所选权限关系。</summary>
    public async Task RefreshRolesPageAsync()
    {
        if (_disposed || IsWriting || !_currentUser.HasPermission("System.Roles.View")
            || ((_roleProfileDirty || _rolePermissionsDirty || _newRoleDirty)
            && !await ConfirmDiscardAsync())) return;
        var selectedId = SelectedRole?.Uid;
        _rolesLoaded = false;
        await LoadRolesAsync();
        if (!CanAccept(_lifetimeToken)) return;
        SelectedRole = null;
        ReplaceChoices(RolePermissionChoices, [], []);
        if (Roles.FirstOrDefault(item => item.Uid == selectedId) is { } role)
            await SelectRoleAsync(role);
        _roleProfileDirty = _rolePermissionsDirty = _newRoleDirty = false;
        _applying = true;
        try { NewRoleCode = NewRoleName = NewRoleDescription = string.Empty; }
        finally { _applying = false; }
        UpdateDirty();
    }

    /// <summary>刷新只读权限定义，保留其他页面编辑状态。</summary>
    public async Task RefreshPermissionsPageAsync()
    {
        if (_disposed || IsWriting || !_currentUser.HasPermission("System.Permissions.View")) return;
        _permissionsLoaded = false;
        await LoadPermissionsAsync();
    }

    /// <summary>离开用户页时确认并丢弃仅属于用户资料和角色集合的草稿。</summary>
    public async Task<bool> CanLeaveUsersPageAsync()
    {
        if (IsWriting || IsBusy) return false;
        if (!_userProfileDirty && !_userRolesDirty) return true;
        if (!await ConfirmDiscardAsync()) return false;
        DiscardUserDraft();
        return true;
    }

    /// <summary>表单内确认放弃后清除用户资料及角色草稿，下一次打开重新读取服务端聚合。</summary>
    public void DiscardUserDraft()
    {
        _userProfileDirty = _userRolesDirty = false;
        SelectedUser = null;
        ReplaceChoices(UserRoleChoices, [], []);
        EffectivePermissions.Clear();
        UpdateDirty();
    }

    /// <summary>离开角色页时确认并丢弃资料、授权和新角色输入。</summary>
    public async Task<bool> CanLeaveRolesPageAsync()
    {
        if (IsWriting || IsBusy) return false;
        if (!_roleProfileDirty && !_rolePermissionsDirty && !_newRoleDirty) return true;
        if (!await ConfirmDiscardAsync()) return false;
        DiscardRoleDraft();
        return true;
    }

    /// <summary>表单内确认放弃后清除角色资料、权限及新建草稿，避免下次打开复用旧输入。</summary>
    public void DiscardRoleDraft()
    {
        _roleProfileDirty = _rolePermissionsDirty = _newRoleDirty = false;
        SelectedRole = null;
        ReplaceChoices(RolePermissionChoices, [], []);
        _applying = true;
        try { NewRoleCode = NewRoleName = NewRoleDescription = string.Empty; }
        finally { _applying = false; }
        UpdateDirty();
    }

    /// <summary>重新从服务端读取列表和所选聚合，冲突后由用户主动触发。</summary>
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private async Task RefreshAsync()
    {
        if (IsDirty && !await ConfirmDiscardAsync()) return;
        var userId = SelectedUser?.Uid;
        var roleId = SelectedRole?.Uid;
        await LoadListsAsync();
        if (!CanAccept(_lifetimeToken)) return;
        SelectedUser = null;
        SelectedRole = null;
        ReplaceChoices(UserRoleChoices, [], []);
        ReplaceChoices(RolePermissionChoices, [], []);
        if (Users.FirstOrDefault(item => item.Uid == userId) is { } user) await SelectUserAsync(user);
        if (Roles.FirstOrDefault(item => item.Uid == roleId) is { } role) await SelectRoleAsync(role);
        ClearAllDirty();
    }

    /// <summary>切换用户前确认未保存变更，并读回角色与有效权限。</summary>
    [RelayCommand(CanExecute = nameof(CanSelectUser))]
    private async Task SelectUserAsync(ManagedUserDto? user)
    {
        if (user is null || _disposed || (SelectedUser?.Uid == user.Uid && UserRoleChoices.Count > 0)) return;
        if ((_userProfileDirty || _userRolesDirty) && !await ConfirmDiscardAsync()) return;
        var version = Interlocked.Increment(ref _selectionVersion);
        var accessRevision = _accessRevision;
        var token = ReplaceSelectionToken();
        await RunBusyAsync(async () =>
        {
            var roles = await _runner.RunAsync(() => _api.GetUserRolesAsync(user.Uid).WaitAsync(token), token, "读取用户角色失败");
            var effective = await _runner.RunAsync(() => _api.GetEffectivePermissionsAsync(user.Uid).WaitAsync(token), token, "读取有效权限失败");
            if (!CanAccept(token) || version != _selectionVersion || accessRevision != _accessRevision
                || !_currentUser.HasPermission("System.Users.View")) return;
            if (!roles.Success || roles.Data is null || !effective.Success)
            {
                SetStatus(roles.Success ? effective.Message : roles.Message, true);
                return;
            }
            _applying = true;
            try
            {
                SelectedUser = user;
                _userVersion = roles.Data.Version;
                UserNickName = user.NickName ?? string.Empty;
                UserEmail = user.Email ?? string.Empty;
                UserPhone = user.PhoneNumber ?? string.Empty;
                ReplaceChoices(UserRoleChoices, Roles.Where(item => item.IsActive)
                    .Select(item => new RbacChoiceModel(item.Uid, $"{item.RoleCode} · {item.RoleName}")), roles.Data.RoleIds);
                EffectivePermissions.Clear();
                foreach (var key in effective.Data ?? []) EffectivePermissions.Add(key);
                _userProfileDirty = false;
                _userRolesDirty = false;
                UpdateDirty();
            }
            finally { _applying = false; }
            SetStatus($"用户角色已读取，当前有效权限 {EffectivePermissions.Count} 项。", false);
        }, "正在读取用户授权...");
    }

    /// <summary>切换角色前确认未保存变更，系统管理员显示自动授权事实。</summary>
    [RelayCommand(CanExecute = nameof(CanSelectRole))]
    private async Task SelectRoleAsync(ManagedRoleDto? role)
    {
        if (role is null || _disposed || (SelectedRole?.Uid == role.Uid && RolePermissionChoices.Count > 0)) return;
        if ((_roleProfileDirty || _rolePermissionsDirty || _newRoleDirty) && !await ConfirmDiscardAsync()) return;
        var version = Interlocked.Increment(ref _selectionVersion);
        var accessRevision = _accessRevision;
        var token = ReplaceSelectionToken();
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.GetRolePermissionsAsync(role.Uid).WaitAsync(token), token, "读取角色权限失败");
            if (!CanAccept(token) || version != _selectionVersion || accessRevision != _accessRevision
                || !_currentUser.HasPermission("System.Roles.View")) return;
            if (!result.Success || result.Data is null) { SetStatus(result.Message, true); return; }
            _applying = true;
            try
            {
                SelectedRole = role;
                _roleVersion = result.Data.Version;
                RoleName = role.RoleName ?? string.Empty;
                RoleDescription = role.Description ?? string.Empty;
                NewRoleCode = string.Empty;
                NewRoleName = string.Empty;
                NewRoleDescription = string.Empty;
                ReplaceChoices(RolePermissionChoices, Permissions.Select(item => new RbacChoiceModel(item.Uid,
                    $"{item.GroupKey} · {item.DisplayName} ({item.PermissionKey})", item.IsAssignable && item.IsActive)), result.Data.PermissionIds);
                _roleProfileDirty = false;
                _rolePermissionsDirty = false;
                _newRoleDirty = false;
                UpdateDirty();
            }
            finally { _applying = false; }
            SetStatus(result.Data.IsAutomatic ? "内置管理员自动拥有全部权限，不能逐项修改。" : "角色权限已读取。", false);
        }, "正在读取角色权限...");
    }

    /// <summary>按用户版本整组替换角色；冲突时保留本地选择供核对。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteUser))]
    private Task SaveUserRolesAsync() => WriteAsync(async () =>
    {
        var user = SelectedUser!;
        var submittedRevision = _userRolesRevision;
        var request = new ReplaceUserRolesRequest { ExpectedVersion = _userVersion,
            RoleIds = UserRoleChoices.Where(item => item.IsSelected).Select(item => item.Id).ToList() };
        var result = await _runner.RunAsync(() => _api.ReplaceUserRolesAsync(user.Uid, request), "保存用户角色失败");
        if (!CanAccept(_lifetimeToken)) return;
        if (!AcceptWrite(result)) return;
        _userVersion = result.Data!.Version;
        user.Version = _userVersion;
        // 请求发出后仍可能改动勾选；只接纳服务端已确认的那一版输入。
        if (_userRolesRevision == submittedRevision) _userRolesDirty = false;
        UpdateDirty();
        await RefreshSelfAsync();
    }, "正在保存用户角色...");

    /// <summary>更新用户非凭据资料，显示服务端版本冲突。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteUser))]
    private Task SaveUserProfileAsync() => WriteAsync(async () =>
    {
        var user = SelectedUser!;
        var submittedRevision = _userProfileRevision;
        var request = new UpdateManagedUserRequest
        { ExpectedVersion = _userVersion, NickName = UserNickName, Email = UserEmail, PhoneNumber = UserPhone };
        var result = await _runner.RunAsync(() => _api.UpdateUserAsync(user.Uid, request), "保存用户资料失败");
        if (!CanAccept(_lifetimeToken)) return;
        if (!AcceptWrite(result)) return;
        _userVersion = result.Data!.Version;
        CopyUser(user, result.Data);
        if (_userProfileRevision == submittedRevision) _userProfileDirty = false;
        UpdateDirty();
        await RefreshSelfAsync();
    }, "正在保存用户资料...");

    /// <summary>启停选中用户，最后管理员保护由后端事务执行。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteUser))]
    private Task SetUserStatusAsync() => WriteAsync(async () =>
    {
        var user = SelectedUser!;
        var result = await _runner.RunAsync(() => _api.SetUserStatusAsync(user.Uid,
            new SetManagedStatusRequest { ExpectedVersion = _userVersion, IsActive = !user.IsActive }), "修改用户状态失败");
        if (!AcceptWrite(result)) return;
        _userVersion = result.Data!.Version;
        CopyUser(user, result.Data);
        await RefreshSelfAsync();
    }, "正在修改用户状态...");

    /// <summary>创建稳定编码的普通角色，随后重新读取角色目录。</summary>
    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task CreateRoleAsync()
    {
        Guid? createdId = null;
        await WriteAsync(async () =>
        {
            var request = new CreateManagedRoleRequest
            { RoleCode = NewRoleCode, RoleName = NewRoleName, Description = NewRoleDescription };
            var result = await _runner.RunAsync(() => _api.CreateRoleAsync(request), "创建角色失败");
            if (!AcceptWrite(result)) return;
            createdId = result.Data!.Uid;
            NewRoleCode = string.Empty;
            NewRoleName = string.Empty;
            NewRoleDescription = string.Empty;
            _newRoleDirty = false;
            UpdateDirty();
            await RefreshSelfAsync();
        }, "正在创建角色...");
        if (createdId is null) return;
        await LoadRolesAsync();
        SelectedRole = null;
        ReplaceChoices(RolePermissionChoices, [], []);
        if (Roles.FirstOrDefault(item => item.Uid == createdId) is { } role) await SelectRoleAsync(role);
    }

    /// <summary>更新普通角色资料，编码始终不可修改。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteRole))]
    private Task SaveRoleProfileAsync() => WriteAsync(async () =>
    {
        var role = SelectedRole!;
        var submittedRevision = _roleProfileRevision;
        var request = new UpdateManagedRoleRequest
        { ExpectedVersion = _roleVersion, RoleName = RoleName, Description = RoleDescription };
        var result = await _runner.RunAsync(() => _api.UpdateRoleAsync(role.Uid, request), "保存角色资料失败");
        if (!CanAccept(_lifetimeToken)) return;
        if (!AcceptWrite(result)) return;
        _roleVersion = result.Data!.Version;
        CopyRole(role, result.Data);
        if (_roleProfileRevision == submittedRevision) _roleProfileDirty = false;
        UpdateDirty();
        await RefreshSelfAsync();
    }, "正在保存角色资料...");

    /// <summary>启停普通角色，撤权后的服务端请求实时生效。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteRole))]
    private Task SetRoleStatusAsync() => WriteAsync(async () =>
    {
        var role = SelectedRole!;
        var result = await _runner.RunAsync(() => _api.SetRoleStatusAsync(role.Uid,
            new SetManagedStatusRequest { ExpectedVersion = _roleVersion, IsActive = !role.IsActive }), "修改角色状态失败");
        if (!AcceptWrite(result)) return;
        _roleVersion = result.Data!.Version;
        CopyRole(role, result.Data);
        await RefreshSelfAsync();
    }, "正在修改角色状态...");

    /// <summary>删除无用户关联的普通角色，保留后端业务拒绝原文。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteRole))]
    private async Task DeleteRoleAsync()
    {
        var role = SelectedRole!;
        if (!await _dialogs.ConfirmAsync("删除角色", $"确定删除 {role.RoleName}？", "删除")) return;
        var deleted = false;
        await WriteAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.DeleteRoleAsync(role.Uid, _roleVersion), "删除角色失败");
            if (!AcceptWrite(result)) return;
            deleted = true;
            _roleProfileDirty = false;
            _rolePermissionsDirty = false;
            _newRoleDirty = false;
            UpdateDirty();
            await RefreshSelfAsync();
        }, "正在删除角色...");
        if (!deleted) return;
        SelectedRole = null;
        ReplaceChoices(RolePermissionChoices, [], []);
        await LoadListsAsync();
    }

    /// <summary>按角色版本整组替换可分配权限，不提交保留管理键。</summary>
    [RelayCommand(CanExecute = nameof(CanWriteRole))]
    private Task SaveRolePermissionsAsync() => WriteAsync(async () =>
    {
        var role = SelectedRole!;
        var submittedRevision = _rolePermissionsRevision;
        var request = new ReplaceRolePermissionsRequest { ExpectedVersion = _roleVersion,
            PermissionIds = RolePermissionChoices.Where(item => item.IsSelected && item.IsEnabled).Select(item => item.Id).ToList() };
        var result = await _runner.RunAsync(() => _api.ReplaceRolePermissionsAsync(role.Uid, request), "保存角色权限失败");
        if (!CanAccept(_lifetimeToken)) return;
        if (!AcceptWrite(result)) return;
        _roleVersion = result.Data!.Version;
        role.Version = _roleVersion;
        if (_rolePermissionsRevision == submittedRevision) _rolePermissionsDirty = false;
        UpdateDirty();
        await RefreshSelfAsync();
    }, "正在保存角色权限...");

    /// <summary>关闭窗口前确认未保存变更，写命令中禁止关闭。</summary>
    public async Task<bool> CanCloseAsync() => !IsWriting && (!IsDirty || await ConfirmDiscardAsync());

    private async Task LoadListsAsync()
    {
        await LoadPermissionsAsync();
        await LoadUsersAsync();
        await LoadRolesAsync();
    }

    /// <summary>单独读取权限目录，避免字典或用户页面被无关接口的权限阻断。</summary>
    private async Task LoadPermissionsAsync()
    {
        var token = _lifetimeToken;
        var accessRevision = _accessRevision;
        await RunBusyAsync(async () =>
        {
            var permissions = await _runner.RunAsync(() => _api.GetPermissionsAsync().WaitAsync(token), token, "读取权限目录失败");
            if (!CanAccept(token) || accessRevision != _accessRevision
                || !_currentUser.HasPermission("System.Permissions.View")) return;
            if (!permissions.Success || permissions.Data is null) { SetStatus(permissions.Message, true); return; }
            Permissions.Clear();
            foreach (var item in permissions.Data) Permissions.Add(item);
            _permissionsLoaded = true;
            SetStatus($"已载入 {Permissions.Count} 项权限定义。", false);
        }, "正在加载权限目录...");
    }

    /// <summary>按服务端分页读取用户列表，页面离开或会话切换后不接纳结果。</summary>
    private async Task LoadUsersAsync()
    {
        var token = _lifetimeToken;
        var accessRevision = _accessRevision;
        await RunBusyAsync(async () =>
        {
            var users = await LoadPagesAsync(_api.SearchUsersAsync, token);
            if (!CanAccept(token) || accessRevision != _accessRevision
                || !_currentUser.HasPermission("System.Users.View") || users is null) return;
            Users.Clear();
            foreach (var item in users) Users.Add(item);
            _usersLoaded = true;
            SetStatus($"已载入 {Users.Count} 位用户。", false);
        }, "正在加载用户...");
    }

    /// <summary>按服务端分页读取角色候选，不因用户列表加载而提前读取。</summary>
    private async Task LoadRolesAsync()
    {
        var token = _lifetimeToken;
        var accessRevision = _accessRevision;
        await RunBusyAsync(async () =>
        {
            var roles = await LoadPagesAsync(_api.SearchRolesAsync, token);
            if (!CanAccept(token) || accessRevision != _accessRevision
                || !_currentUser.HasPermission("System.Roles.View") || roles is null) return;
            Roles.Clear();
            foreach (var item in roles) Roles.Add(item);
            _rolesLoaded = true;
            SetStatus($"已载入 {Roles.Count} 个角色。", false);
        }, "正在加载角色...");
    }

    /// <summary>按服务端分页完整读取管理列表，避免超过一页时遗漏目标。</summary>
    private async Task<List<T>?> LoadPagesAsync<T>(Func<ManagedPageRequest, Task<ServiceResult<PagedResponseBase<T>>>> query,
        CancellationToken token)
    {
        var items = new List<T>();
        for (var page = 1; ; page++)
        {
            var request = new ManagedPageRequest { PageIndex = page, PageSize = 100 };
            var result = await _runner.RunAsync(() => query(request).WaitAsync(token), token, "读取管理列表失败");
            if (!CanAccept(token)) return null;
            if (!result.Success || result.Data is null) { SetStatus(result.Message, true); return null; }
            items.AddRange(result.Data.Items);
            if (items.Count >= result.Data.TotalCount || result.Data.Items.Count == 0) return items;
        }
    }

    /// <summary>提交成功后刷新本会话身份，导航与命令通过 CurrentUserChanged 重新求值。</summary>
    private async Task RefreshSelfAsync()
    {
        var result = await _authService.RefreshCurrentUserAsync();
        if (!result.Success) SetStatus($"保存已完成；身份刷新失败：{result.Message}", true);
    }

    /// <summary>写入期间阻止重入，失败时保持输入和版本供用户核对。</summary>
    private async Task WriteAsync(Func<Task> action, string message)
    {
        if (IsWriting || _disposed || !CanManage()) return;
        _activeWriteRevision = _accessRevision;
        IsWriting = true;
        try { await RunWriteAsync(action, message); }
        finally { IsWriting = false; }
    }

    private bool AcceptWrite<T>(ServiceResult<T> result)
    {
        if (!CanAccept(_lifetimeToken) || !_currentUser.IsAdmin
            || _activeWriteRevision != _accessRevision) return false;
        if (result.Success && result.Data is not null)
        {
            SuccessfulWriteRevision++;
            SetStatus("保存成功。", false);
            return true;
        }
        SetStatus(result.Error?.Code == ServiceErrorCodes.VersionConflict
            ? "数据已被其他管理员修改，请核对后使用“刷新”重新读取。" : result.Message, true);
        return false;
    }

    private void SetStatus(string message, bool error)
    {
        StatusMessage = message;
        ValidationSummary = error ? message : null;
    }

    private async Task<bool> ConfirmDiscardAsync() => await _dialogs.ConfirmAsync(
        "放弃未保存变更", "切换或刷新会放弃当前未保存输入，是否继续？", "放弃变更");

    private CancellationToken ReplaceSelectionToken()
    {
        var next = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        var old = Interlocked.Exchange(ref _selection, next);
        old?.Cancel();
        old?.Dispose();
        return next.Token;
    }

    private bool CanAccept(CancellationToken token) => !_disposed && !token.IsCancellationRequested
        && _currentUser.IsAuthenticated && _currentUser.CurrentUser?.UserId == _sessionUserId;

    private bool CanQuery() => !IsBusy && !_disposed;
    private bool CanSelectUser(ManagedUserDto? user) => CanQuery() && user is not null;
    private bool CanSelectRole(ManagedRoleDto? role) => CanQuery() && role is not null;
    private bool CanManage() => CanQuery() && _currentUser.IsAdmin;
    private bool CanWriteUser() => CanManage() && SelectedUser is not null;
    private bool CanWriteRole() => CanManage() && IsOrdinaryRole;

    private static void CopyUser(ManagedUserDto target, ManagedUserDto source)
    { target.Version = source.Version; target.NickName = source.NickName; target.Email = source.Email;
      target.PhoneNumber = source.PhoneNumber; target.IsActive = source.IsActive; target.IsAdmin = source.IsAdmin; }

    private static void CopyRole(ManagedRoleDto target, ManagedRoleDto source)
    { target.Version = source.Version; target.RoleName = source.RoleName; target.Description = source.Description;
      target.IsActive = source.IsActive; }

    private void ReplaceChoices(ObservableCollection<RbacChoiceModel> target, IEnumerable<RbacChoiceModel> source,
        IReadOnlyCollection<Guid> selected)
    {
        foreach (var old in target) old.PropertyChanged -= OnChoiceChanged;
        target.Clear();
        var selectedIds = selected.ToHashSet();
        foreach (var item in source)
        {
            item.IsSelected = selectedIds.Contains(item.Id);
            item.PropertyChanged += OnChoiceChanged;
            target.Add(item);
        }
        if (ReferenceEquals(target, RolePermissionChoices)) OnPropertyChanged(nameof(SelectedPermissionCount));
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RbacChoiceModel.IsSelected)) return;
        if (sender is RbacChoiceModel choice && RolePermissionChoices.Contains(choice))
            OnPropertyChanged(nameof(SelectedPermissionCount));
        if (_applying) return;
        if (sender is RbacChoiceModel userChoice && UserRoleChoices.Contains(userChoice))
        { _userRolesDirty = true; _userRolesRevision++; }
        if (sender is RbacChoiceModel roleChoice && RolePermissionChoices.Contains(roleChoice))
        { _rolePermissionsDirty = true; _rolePermissionsRevision++; }
        UpdateDirty();
    }

    partial void OnUserNickNameChanged(string value) { if (!_applying) MarkUserProfileDirty(); }
    partial void OnUserEmailChanged(string value) { if (!_applying) MarkUserProfileDirty(); }
    partial void OnUserPhoneChanged(string value) { if (!_applying) MarkUserProfileDirty(); }
    partial void OnRoleNameChanged(string value) { if (!_applying) MarkRoleProfileDirty(); }
    partial void OnRoleDescriptionChanged(string value) { if (!_applying) MarkRoleProfileDirty(); }
    partial void OnNewRoleCodeChanged(string value) { if (!_applying) { _newRoleDirty = true; UpdateDirty(); } }
    partial void OnNewRoleNameChanged(string value) { if (!_applying) { _newRoleDirty = true; UpdateDirty(); } }
    partial void OnNewRoleDescriptionChanged(string value) { if (!_applying) { _newRoleDirty = true; UpdateDirty(); } }

    /// <summary>记录用户资料的每次输入修订，使在途保存只清理已提交的版本。</summary>
    private void MarkUserProfileDirty()
    {
        _userProfileDirty = true;
        _userProfileRevision++;
        UpdateDirty();
    }

    /// <summary>角色资料修订独立于新建表单，使在途响应只清理已提交输入。</summary>
    private void MarkRoleProfileDirty()
    {
        _roleProfileDirty = true;
        _roleProfileRevision++;
        UpdateDirty();
    }

    /// <summary>只在所有保存范围均清洁时解除关闭确认。</summary>
    private void UpdateDirty() => IsDirty = _userProfileDirty || _userRolesDirty || _roleProfileDirty
        || _rolePermissionsDirty || _newRoleDirty;

    /// <summary>明确放弃全部待保存输入后重置关闭门禁。</summary>
    private void ClearAllDirty()
    {
        _userProfileDirty = _userRolesDirty = _roleProfileDirty = _rolePermissionsDirty = _newRoleDirty = false;
        UpdateDirty();
    }

    /// <summary>账号切换时立即失效当前窗口的数据与在途请求。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_currentUser.CurrentUser?.UserId != _sessionUserId) { Dispose(); return; }
        var current = _currentUser.Permissions.ToHashSet(StringComparer.Ordinal);
        if (_permissionSnapshot.SetEquals(current)) return;
        _permissionSnapshot = current;
        _accessRevision++;
        _selection?.Cancel();
        if (!current.Contains("System.Users.View"))
        {
            Users.Clear(); ReplaceChoices(UserRoleChoices, [], []); EffectivePermissions.Clear();
            SelectedUser = null; _usersLoaded = false;
            _applying = true;
            try { UserNickName = UserEmail = UserPhone = string.Empty; }
            finally { _applying = false; }
            _userProfileDirty = _userRolesDirty = false;
        }
        if (!current.Contains("System.Roles.View"))
        {
            Roles.Clear(); ReplaceChoices(RolePermissionChoices, [], []);
            SelectedRole = null; _rolesLoaded = false;
            _applying = true;
            try { RoleName = RoleDescription = NewRoleCode = NewRoleName = NewRoleDescription = string.Empty; }
            finally { _applying = false; }
            _roleProfileDirty = _rolePermissionsDirty = _newRoleDirty = false;
        }
        if (!current.Contains("System.Permissions.View"))
        {
            Permissions.Clear(); _permissionsLoaded = false;
        }
        UpdateDirty();
    }

    /// <summary>撤销查询、清空旧会话数据并解除所有事件订阅。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _selection?.Cancel();
        _selection?.Dispose();
        _lifetime.Dispose();
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        foreach (var item in UserRoleChoices) item.PropertyChanged -= OnChoiceChanged;
        foreach (var item in RolePermissionChoices) item.PropertyChanged -= OnChoiceChanged;
        Users.Clear(); Roles.Clear(); Permissions.Clear(); EffectivePermissions.Clear();
        UserRoleChoices.Clear(); RolePermissionChoices.Clear();
    }
}
