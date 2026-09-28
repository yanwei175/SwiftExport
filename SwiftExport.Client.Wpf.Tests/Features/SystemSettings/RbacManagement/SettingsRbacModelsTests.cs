using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Permissions.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Profile.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Query;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings.RbacManagement;

/// <summary>验证窄列表模型和独立 RBAC 表单的版本、草稿、筛选与会话边界。</summary>
[TestClass]
public sealed class SettingsRbacModelsTests
{
    /// <summary>三个列表只读各自目录；账号切换清空旧会话结果。</summary>
    [TestMethod]
    public async Task QueryModels_LoadOnlyOwnCatalogAndClearOnSessionSwitch()
    {
        var (state, api, user) = CreateContext();
        var dialogs = new SettingsDialogStub();
        using var users = new UsersQueryViewModel(api, new RunnerStub(), new AuthStub(), user, dialogs);
        using var roles = new RolesQueryViewModel(api, new RunnerStub(), new AuthStub(), user, dialogs, new DialogStub());
        using var permissions = new PermissionsQueryViewModel(api, new RunnerStub(), user);
        await users.InitializeAsync();
        await roles.InitializeAsync();
        await permissions.InitializeAsync();
        Assert.AreEqual(state.User.Uid, users.SelectedUser?.Uid);
        Assert.AreEqual(state.Role.Uid, roles.SelectedRole?.Uid);
        Assert.AreEqual(2, permissions.Permissions.Count);
        user.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString() });
        Assert.HasCount(0, users.Users);
        Assert.HasCount(0, roles.Roles);
        Assert.HasCount(0, permissions.Permissions);
    }

    /// <summary>用户角色完整集合成功保存；版本冲突保留勾选和脏状态。</summary>
    [TestMethod]
    public async Task UserRolesForm_SaveFullSetAndPreserveConflictDraft()
    {
        var (state, api, _) = CreateContext();
        var auth = new AuthStub();
        using var form = new UserRolesFormViewModel(api, new RunnerStub(), auth);
        Assert.IsTrue(await form.InitializeAsync(state.User));
        Assert.HasCount(1, form.UserRoleChoices);
        Assert.HasCount(0, form.EffectivePermissions);
        form.UserRoleChoices.Single().IsSelected = true;
        await form.SaveCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { state.Role.Uid }, state.UserRoles);
        Assert.AreEqual(2, state.User.Version);
        Assert.IsTrue(form.Saved);
        Assert.AreEqual(1, auth.RefreshCalls);

        using var conflict = new UserRolesFormViewModel(api, new RunnerStub(), auth);
        Assert.IsTrue(await conflict.InitializeAsync(state.User));
        conflict.UserRoleChoices.Single().IsSelected = false;
        state.RejectNextUserSave = true;
        await conflict.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(conflict.IsDirty);
        Assert.IsFalse(conflict.Saved);
        StringAssert.Contains(conflict.StatusMessage, "刷新");
        CollectionAssert.AreEqual(new[] { state.Role.Uid }, state.UserRoles);
    }

    /// <summary>已分配的停用角色仍属于完整关系，保存时不能因候选过滤而移除。</summary>
    [TestMethod]
    public async Task UserRolesForm_PreservesAssignedInactiveRole()
    {
        var (state, api, _) = CreateContext();
        state.Role.IsActive = false;
        state.UserRoles = [state.Role.Uid];
        using var form = new UserRolesFormViewModel(api, new RunnerStub(), new AuthStub());
        Assert.IsTrue(await form.InitializeAsync(state.User));
        Assert.HasCount(1, form.UserRoleChoices);
        Assert.IsTrue(form.UserRoleChoices.Single().IsSelected);
        await form.SaveCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { state.Role.Uid }, state.UserRoles);
    }

    /// <summary>搜索仅裁剪显示；角色权限保存始终提交完整可分配集合。</summary>
    [TestMethod]
    public void RolePermissionsForm_FilterDoesNotTrimSubmittedSet() => StaTest.RunAsync(async () =>
    {
        var (state, api, _) = CreateContext();
        using var form = new RolePermissionsFormViewModel(api, new RunnerStub(), new AuthStub());
        Assert.IsTrue(await form.InitializeAsync(state.Role));
        Assert.HasCount(2, form.RolePermissionChoices);
        Assert.IsFalse(form.RolePermissionChoices.Single(item => !item.IsEnabled).IsSelected);
        form.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected = true;
        form.SearchText = "不存在";
        Assert.AreEqual(0, form.Choices.Count);
        await form.SaveCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { state.Assignable.Uid }, state.RolePermissions);
        Assert.AreEqual(2, state.Role.Version);
        Assert.IsTrue(form.Saved);
    });

    /// <summary>不同资料表单不共享草稿；保存其中一份不清理另一份。</summary>
    [TestMethod]
    public async Task ProfileForms_KeepIndependentDraftsAndVersions()
    {
        var (state, api, _) = CreateContext();
        var auth = new AuthStub();
        using var user = new UserProfileFormViewModel(api, new RunnerStub(), auth);
        using var role = new RoleProfileFormViewModel(api, new RunnerStub(), auth);
        user.Initialize(state.User);
        role.InitializeEdit(state.Role);
        user.UserNickName = "未保存昵称";
        role.RoleName = "新角色名";
        await role.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual("新角色名", state.Role.RoleName);
        Assert.IsTrue(user.IsDirty);
        Assert.AreEqual("未保存昵称", user.UserNickName);
        Assert.IsTrue(role.Saved);
        Assert.IsFalse(user.Saved);
        Assert.IsFalse(user.CloseState.CanClose(DialogResult.Close));
        user.CloseState.KeepEditingCommand.Execute(null);
        Assert.IsFalse(user.CloseState.IsDiscardPromptVisible);
    }

    /// <summary>写入中关闭受阻；强制失效后迟到响应不得刷新新会话。</summary>
    [TestMethod]
    public async Task UserRolesForm_ForcedDuringWriteIgnoresLateResponse()
    {
        var (state, api, _) = CreateContext();
        state.PausedMethod = "UserRoles";
        var auth = new AuthStub();
        using var form = new UserRolesFormViewModel(api, new RunnerStub(), auth);
        Assert.IsTrue(await form.InitializeAsync(state.User));
        form.UserRoleChoices.Single().IsSelected = true;
        var pending = form.SaveCommand.ExecuteAsync(null);
        await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(form.IsBusy);
        Assert.IsFalse(form.CloseState.CanClose(DialogResult.Close));
        Assert.IsFalse(form.SaveCommand.CanExecute(null));
        form.CloseState.ForceClose();
        form.Dispose();
        state.Release.SetResult();
        await pending;
        Assert.IsFalse(form.Saved);
        Assert.AreEqual(0, auth.RefreshCalls);
    }

    /// <summary>角色创建使用全新草稿，明确返回服务端新角色标识。</summary>
    [TestMethod]
    public async Task RoleProfile_CreateOwnDraftAndReturnCreatedId()
    {
        var (state, api, _) = CreateContext();
        using var form = new RoleProfileFormViewModel(api, new RunnerStub(), new AuthStub());
        form.InitializeCreate();
        Assert.IsTrue(form.IsCreating);
        Assert.AreEqual(string.Empty, form.RoleCode);
        form.RoleCode = "NEW_ROLE";
        form.RoleName = "新角色";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(form.Saved);
        Assert.AreEqual("NEW_ROLE", state.CreatedRole?.RoleCode);
        Assert.AreEqual("新角色", state.CreatedRole?.RoleName);
        Assert.AreEqual(state.CreatedRole?.Uid, form.TargetId);
    }

    /// <summary>用户资料并发冲突保留本次表单输入，不修改列表行快照。</summary>
    [TestMethod]
    public async Task UserProfile_ConflictPreservesDraftAndListRow()
    {
        var (state, api, _) = CreateContext();
        using var form = new UserProfileFormViewModel(api, new RunnerStub(), new AuthStub());
        form.Initialize(state.User);
        form.UserNickName = "待保存昵称";
        state.RejectNextProfileSave = true;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(form.Saved);
        Assert.IsTrue(form.IsDirty);
        Assert.AreEqual("待保存昵称", form.UserNickName);
        Assert.AreEqual("目标", state.User.NickName);
        StringAssert.Contains(form.StatusMessage, "刷新");
    }

    /// <summary>列表仅在表单确认成功时重新读取目标；取消不触发多余查询。</summary>
    [TestMethod]
    public async Task UsersQuery_RefreshesOnlyAfterSavedDialogResult()
    {
        var (state, api, user) = CreateContext();
        var dialogs = new SettingsDialogStub();
        using var list = new UsersQueryViewModel(api, new RunnerStub(), new AuthStub(), user, dialogs);
        await list.InitializeAsync();
        Assert.AreEqual(1, state.UserSearchCalls);
        await list.EditProfileCommand.ExecuteAsync(state.User);
        Assert.AreEqual(1, state.UserSearchCalls);
        dialogs.Result = new(true, state.User.Uid);
        await list.EditProfileCommand.ExecuteAsync(state.User);
        Assert.AreEqual(2, state.UserSearchCalls);
        Assert.AreEqual(state.User.Uid, list.SelectedUser?.Uid);
    }
    private static (ApiState State, ISystemRbacApi Api, CurrentUserService User) CreateContext()
    {
        var state = new ApiState();
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        return (state, api, user);
    }
    /// <summary>提供服务端版本与关系集合的窄假件，按 API 方法分发请求。</summary>
    public sealed class ApiState
    {
        public Guid AdminId { get; } = Guid.NewGuid();
        public ManagedUserDto User { get; } = new() { Uid = Guid.NewGuid(), UserName = "target", NickName = "目标", Version = 1, IsActive = true };
        public ManagedRoleDto Role { get; } = new() { Uid = Guid.NewGuid(), RoleCode = "VIEWER", RoleName = "只读", Version = 1, IsActive = true };
        public ManagedPermissionDto Assignable { get; } = new() { Uid = Guid.NewGuid(), PermissionKey = "Suppliers.Supplier.View", GroupKey = "Suppliers", DisplayName = "供应商查看", IsActive = true, IsAssignable = true };
        public ManagedPermissionDto Reserved { get; } = new() { Uid = Guid.NewGuid(), PermissionKey = "System.Users.Manage", GroupKey = "System", DisplayName = "用户管理", IsActive = true, IsAssignable = false };
        public List<Guid> UserRoles { get; set; } = [];
        public List<Guid> RolePermissions { get; set; } = [];
        public bool RejectNextUserSave { get; set; }
        public bool RejectNextProfileSave { get; set; }
        public int UserSearchCalls { get; private set; }
        public ManagedRoleDto? CreatedRole { get; private set; }
        public string? PausedMethod { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public object Call(MethodInfo method, object?[]? args) => method.Name switch
        {
            nameof(ISystemRbacApi.GetPermissionsAsync) => Task.FromResult(ServiceResult<List<ManagedPermissionDto>>.Ok([Assignable, Reserved])),
            nameof(ISystemRbacApi.SearchUsersAsync) => SearchUsers(),
            nameof(ISystemRbacApi.SearchRolesAsync) => Task.FromResult(ServiceResult<PagedResponseBase<ManagedRoleDto>>.Ok(
                new PagedResponseBase<ManagedRoleDto> { Items = [Role], TotalCount = 1, PageIndex = 1, PageSize = 100 })),
            nameof(ISystemRbacApi.GetUserRolesAsync) => Task.FromResult(ServiceResult<UserRolesDto>.Ok(
                new UserRolesDto { UserId = User.Uid, Version = User.Version, RoleIds = UserRoles.ToList() })),
            nameof(ISystemRbacApi.GetEffectivePermissionsAsync) => Task.FromResult(ServiceResult<List<string>>.Ok(
                UserRoles.Count > 0 && RolePermissions.Count > 0 ? [Assignable.PermissionKey] : [])),
            nameof(ISystemRbacApi.GetRolePermissionsAsync) => Task.FromResult(ServiceResult<RolePermissionsDto>.Ok(
                new RolePermissionsDto { RoleId = Role.Uid, Version = Role.Version, PermissionIds = RolePermissions.ToList() })),
            nameof(ISystemRbacApi.ReplaceUserRolesAsync) => ReplaceUserRoles((ReplaceUserRolesRequest)args![1]!),
            nameof(ISystemRbacApi.ReplaceRolePermissionsAsync) => ReplaceRolePermissions((ReplaceRolePermissionsRequest)args![1]!),
            nameof(ISystemRbacApi.UpdateUserAsync) => UpdateUser((UpdateManagedUserRequest)args![1]!),
            nameof(ISystemRbacApi.UpdateRoleAsync) => UpdateRole((UpdateManagedRoleRequest)args![1]!),
            nameof(ISystemRbacApi.CreateRoleAsync) => CreateRole((CreateManagedRoleRequest)args![0]!),
            _ => throw new NotSupportedException(method.Name)
        };

        private Task<ServiceResult<PagedResponseBase<ManagedUserDto>>> SearchUsers()
        {
            UserSearchCalls++;
            return Task.FromResult(ServiceResult<PagedResponseBase<ManagedUserDto>>.Ok(
                new PagedResponseBase<ManagedUserDto>
                { Items = [User], TotalCount = 1, PageIndex = 1, PageSize = 100 }));
        }

        private Task<ServiceResult<ManagedRoleDto>> CreateRole(CreateManagedRoleRequest request)
        {
            CreatedRole = new ManagedRoleDto { Uid = Guid.NewGuid(), Version = 1,
                RoleCode = request.RoleCode, RoleName = request.RoleName, Description = request.Description,
                IsActive = true };
            return Task.FromResult(ServiceResult<ManagedRoleDto>.Ok(CreatedRole));
        }
        /// <summary>在指定保存路径捕获请求后延迟响应，以复现等待中的再次编辑。</summary>
        private async Task PauseAsync(string method)
        {
            if (PausedMethod != method) return;
            Started.SetResult();
            await Release.Task;
        }

        private async Task<ServiceResult<UserRolesDto>> ReplaceUserRoles(ReplaceUserRolesRequest request)
        {
            var submittedRoles = request.RoleIds.ToList();
            await PauseAsync("UserRoles");
            if (RejectNextUserSave || request.ExpectedVersion != User.Version)
            {
                RejectNextUserSave = false;
                return ServiceResult<UserRolesDto>.Fail(ServiceErrorCodes.VersionConflict, "冲突");
            }
            UserRoles = submittedRoles;
            User.Version++;
            return ServiceResult<UserRolesDto>.Ok(new UserRolesDto
            { UserId = User.Uid, Version = User.Version, RoleIds = UserRoles.ToList() });
        }

        private async Task<ServiceResult<RolePermissionsDto>> ReplaceRolePermissions(ReplaceRolePermissionsRequest request)
        {
            var submittedPermissions = request.PermissionIds.ToList();
            await PauseAsync("RolePermissions");
            Assert.AreEqual(request.ExpectedVersion, Role.Version);
            Assert.IsFalse(request.PermissionIds.Contains(Reserved.Uid));
            RolePermissions = submittedPermissions;
            Role.Version++;
            return ServiceResult<RolePermissionsDto>.Ok(new RolePermissionsDto
            { RoleId = Role.Uid, Version = Role.Version, PermissionIds = RolePermissions.ToList() });
        }

        private async Task<ServiceResult<ManagedUserDto>> UpdateUser(UpdateManagedUserRequest request)
        {
            var submitted = new { request.NickName, request.Email, request.PhoneNumber };
            await PauseAsync("UserProfile");
            if (RejectNextProfileSave)
            {
                RejectNextProfileSave = false;
                return ServiceResult<ManagedUserDto>.Fail(ServiceErrorCodes.VersionConflict, "冲突");
            }
            Assert.AreEqual(request.ExpectedVersion, User.Version);
            User.NickName = submitted.NickName;
            User.Email = submitted.Email;
            User.PhoneNumber = submitted.PhoneNumber;
            User.Version++;
            return ServiceResult<ManagedUserDto>.Ok(User);
        }

        private async Task<ServiceResult<ManagedRoleDto>> UpdateRole(UpdateManagedRoleRequest request)
        {
            var submitted = new { request.RoleName, request.Description };
            await PauseAsync("RoleProfile");
            Assert.AreEqual(request.ExpectedVersion, Role.Version);
            Role.RoleName = submitted.RoleName;
            Role.Description = submitted.Description;
            Role.Version++;
            return ServiceResult<ManagedRoleDto>.Ok(Role);
        }
    }

    /// <summary>将反射分发限制在此测试的 RBAC API，不进行网络访问。</summary>
    public class RbacApiProxy : DispatchProxy
    {
        public ApiState State { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => State.Call(targetMethod!, args);
    }

    /// <summary>按原结果返回 API，保留失败代码与消息。</summary>
    private sealed class RunnerStub : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    /// <summary>统计保存后的本会话权限刷新调用。</summary>
    private sealed class AuthStub : IAuthService
    {
        public int RefreshCalls { get; private set; }
        public Task<ServiceResult<MeDto>> RefreshCurrentUserAsync()
        { RefreshCalls++; return Task.FromResult(ServiceResult<MeDto>.Ok(new MeDto())); }
        public Task<ServiceResult<MeDto>> LoginAsync(string userName, string password) => throw new NotSupportedException();
        public Task<bool> TryRestoreSessionAsync() => throw new NotSupportedException();
    }

    /// <summary>确认测试中的选择切换与删除请求。</summary>
    private sealed class DialogStub : IDialogService
    {
        public bool Result { get; init; } = true;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定",
            string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
