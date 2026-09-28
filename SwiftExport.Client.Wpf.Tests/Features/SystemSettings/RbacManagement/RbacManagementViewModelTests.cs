using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings.RbacManagement;

/// <summary>验证新管理页从服务端读回角色/权限、整组保存及版本冲突反馈。</summary>
[TestClass]
public sealed class RbacManagementViewModelTests
{
    /// <summary>角色和权限来自新接口；保存完整集合后保持服务端版本，冲突保留勾选。</summary>
    [TestMethod]
    public async Task LoadAndSaveRolesAndPermissions_KeepVersionAndUnsavedConflict()
    {
        var state = new ApiState();
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        var auth = new AuthStub();
        using var model = new RbacManagementViewModel(api, new RunnerStub(), auth, currentUser, new DialogStub());

        await model.InitializeAsync();

        Assert.AreEqual(state.User.Uid, model.SelectedUser?.Uid);
        Assert.AreEqual(state.Role.Uid, model.SelectedRole?.Uid);
        Assert.AreEqual(0, model.EffectivePermissions.Count);
        Assert.AreEqual(2, model.Permissions.Count);
        Assert.IsFalse(model.RolePermissionChoices.Single(item => !item.IsEnabled).IsSelected);
        model.UserRoleChoices.Single().IsSelected = true;
        await model.SaveUserRolesCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { state.Role.Uid }, state.UserRoles);
        Assert.AreEqual(2, state.User.Version);
        Assert.IsFalse(model.HasUnsavedChanges);

        model.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected = true;
        await model.SaveRolePermissionsCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { state.Assignable.Uid }, state.RolePermissions);
        Assert.AreEqual(2, state.Role.Version);
        Assert.AreEqual(1, model.SelectedPermissionCount);
        Assert.AreEqual(2, auth.RefreshCalls);

        model.UserRoleChoices.Single().IsSelected = false;
        state.RejectNextUserSave = true;
        await model.SaveUserRolesCommand.ExecuteAsync(null);
        Assert.IsTrue(model.HasUnsavedChanges);
        StringAssert.Contains(model.StatusMessage, "刷新");
        CollectionAssert.AreEqual(new[] { state.Role.Uid }, state.UserRoles);
    }

    /// <summary>账号切换后在途管理数据不得写回新会话。</summary>
    [TestMethod]
    public async Task SessionSwitch_ClearsOldManagementData()
    {
        var state = new ApiState();
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        using var model = new RbacManagementViewModel(api, new RunnerStub(), new AuthStub(), currentUser, new DialogStub());
        await model.InitializeAsync();
        Assert.HasCount(1, model.Users);

        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString() });

        Assert.HasCount(0, model.Users);
        Assert.HasCount(0, model.RolePermissionChoices);
        Assert.IsFalse(model.SaveUserRolesCommand.CanExecute(null));
    }

    /// <summary>切换用户时拒绝放弃未保存勾选，应保留原用户及原角色选择。</summary>
    [TestMethod]
    public async Task CancelDiscard_KeepsUserAndUnsavedRoleSelection()
    {
        var state = new ApiState();
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        using var model = new RbacManagementViewModel(api, new RunnerStub(), new AuthStub(), currentUser,
            new DialogStub { Result = false });
        await model.InitializeAsync();
        var original = model.SelectedUser;
        model.UserRoleChoices.Single().IsSelected = true;

        await model.SelectUserCommand.ExecuteAsync(new ManagedUserDto { Uid = Guid.NewGuid(), UserName = "other" });

        Assert.AreSame(original, model.SelectedUser);
        Assert.IsTrue(model.UserRoleChoices.Single().IsSelected);
        Assert.IsTrue(model.HasUnsavedChanges);
    }

    /// <summary>仅保存角色关系或权限关系后，其他资料编辑仍须阻止无提示关闭。</summary>
    [TestMethod]
    public async Task PartialSave_KeepsOtherEditScopesDirty()
    {
        var state = new ApiState();
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        using var model = new RbacManagementViewModel(api, new RunnerStub(), new AuthStub(), currentUser,
            new DialogStub { Result = false });
        await model.InitializeAsync();

        model.UserNickName = "未保存昵称";
        model.UserRoleChoices.Single().IsSelected = true;
        await model.SaveUserRolesCommand.ExecuteAsync(null);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.IsFalse(await model.CanCloseAsync());
        Assert.AreEqual("未保存昵称", model.UserNickName);

        model.RoleName = "未保存角色名";
        model.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected = true;
        await model.SaveRolePermissionsCommand.ExecuteAsync(null);
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.IsFalse(await model.CanCloseAsync());
        Assert.AreEqual("未保存角色名", model.RoleName);
    }

    /// <summary>四条保存请求等待响应时再次编辑同一范围，成功响应只能接纳已提交快照。</summary>
    [TestMethod]
    [DataRow("UserRoles")]
    [DataRow("UserProfile")]
    [DataRow("RolePermissions")]
    [DataRow("RoleProfile")]
    public async Task EditDuringSave_KeepsLaterInputDirtyUntilItIsSubmitted(string scope)
    {
        var state = new ApiState { PausedMethod = scope };
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        using var model = new RbacManagementViewModel(api, new RunnerStub(), new AuthStub(), currentUser,
            new DialogStub { Result = false });
        await model.InitializeAsync();

        Task pending = scope switch
        {
            "UserRoles" => StartUserRoles(),
            "UserProfile" => StartUserProfile(),
            "RolePermissions" => StartRolePermissions(),
            "RoleProfile" => StartRoleProfile(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(model.IsWriting);
        Assert.IsFalse(await model.CanCloseAsync());
        switch (scope)
        {
            case "UserRoles": model.UserRoleChoices.Single().IsSelected = false; break;
            case "UserProfile": model.UserNickName = "后续昵称"; break;
            case "RolePermissions": model.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected = false; break;
            case "RoleProfile": model.RoleName = "后续角色名"; break;
        }
        state.Release.SetResult();
        await pending;
        state.PausedMethod = null;

        Assert.IsTrue(model.HasUnsavedChanges, scope);
        Assert.IsFalse(await model.CanCloseAsync(), scope);
        switch (scope)
        {
            case "UserRoles":
                Assert.HasCount(1, state.UserRoles);
                Assert.IsFalse(model.UserRoleChoices.Single().IsSelected);
                await model.SaveUserRolesCommand.ExecuteAsync(null);
                Assert.HasCount(0, state.UserRoles);
                break;
            case "UserProfile":
                Assert.AreEqual("提交昵称", state.User.NickName);
                Assert.AreEqual("后续昵称", model.UserNickName);
                await model.SaveUserProfileCommand.ExecuteAsync(null);
                Assert.AreEqual("后续昵称", state.User.NickName);
                break;
            case "RolePermissions":
                Assert.HasCount(1, state.RolePermissions);
                Assert.IsFalse(model.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected);
                await model.SaveRolePermissionsCommand.ExecuteAsync(null);
                Assert.HasCount(0, state.RolePermissions);
                break;
            case "RoleProfile":
                Assert.AreEqual("提交角色名", state.Role.RoleName);
                Assert.AreEqual("后续角色名", model.RoleName);
                await model.SaveRoleProfileCommand.ExecuteAsync(null);
                Assert.AreEqual("后续角色名", state.Role.RoleName);
                break;
        }
        Assert.IsFalse(model.HasUnsavedChanges, scope);

        Task StartUserRoles()
        {
            model.UserRoleChoices.Single().IsSelected = true;
            return model.SaveUserRolesCommand.ExecuteAsync(null);
        }
        Task StartUserProfile()
        {
            model.UserNickName = "提交昵称";
            return model.SaveUserProfileCommand.ExecuteAsync(null);
        }
        Task StartRolePermissions()
        {
            model.RolePermissionChoices.Single(item => item.IsEnabled).IsSelected = true;
            return model.SaveRolePermissionsCommand.ExecuteAsync(null);
        }
        Task StartRoleProfile()
        {
            model.RoleName = "提交角色名";
            return model.SaveRoleProfileCommand.ExecuteAsync(null);
        }
    }

    /// <summary>账号切换后到达的保存响应不得刷新新会话或恢复旧窗口数据。</summary>
    [TestMethod]
    public async Task SessionSwitchDuringSave_DoesNotAcceptLateResponse()
    {
        var state = new ApiState { PausedMethod = "UserRoles" };
        var api = DispatchProxy.Create<ISystemRbacApi, RbacApiProxy>();
        ((RbacApiProxy)(object)api).State = state;
        var currentUser = new CurrentUserService();
        currentUser.SetCurrentUser(new MeDto { UserId = state.AdminId.ToString(), IsAdmin = true,
            Permissions = ["System.Users.View", "System.Roles.View", "System.Permissions.View"] });
        var auth = new AuthStub();
        using var model = new RbacManagementViewModel(api, new RunnerStub(), auth, currentUser,
            new DialogStub { Result = false });
        await model.InitializeAsync();
        model.UserRoleChoices.Single().IsSelected = true;
        var pending = model.SaveUserRolesCommand.ExecuteAsync(null);
        await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        currentUser.SetCurrentUser(new MeDto { UserId = Guid.NewGuid().ToString() });
        state.Release.SetResult();
        await pending;

        Assert.HasCount(0, model.Users);
        Assert.HasCount(0, model.UserRoleChoices);
        Assert.AreEqual(0, auth.RefreshCalls);
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
        public string? PausedMethod { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public object Call(MethodInfo method, object?[]? args) => method.Name switch
        {
            nameof(ISystemRbacApi.GetPermissionsAsync) => Task.FromResult(ServiceResult<List<ManagedPermissionDto>>.Ok([Assignable, Reserved])),
            nameof(ISystemRbacApi.SearchUsersAsync) => Task.FromResult(ServiceResult<PagedResponseBase<ManagedUserDto>>.Ok(
                new PagedResponseBase<ManagedUserDto> { Items = [User], TotalCount = 1, PageIndex = 1, PageSize = 100 })),
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
            _ => throw new NotSupportedException(method.Name)
        };

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
