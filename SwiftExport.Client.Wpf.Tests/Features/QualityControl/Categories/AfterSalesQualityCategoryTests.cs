using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.QualityControl.Categories.Models;
using SwiftExportClient.Features.QualityControl.Categories.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.Categories;

/// <summary>验证分类层级、编辑状态、回执接纳和动态权限；全部使用本地替身。</summary>
[TestClass]
public sealed class AfterSalesQualityCategoryTests
{
    /// <summary>新增父子保留临时引用，未保存子树删除不产生数据库删除项。</summary>
    [TestMethod]
    public void NewSubtreePreservesTemporaryParentAndCanBeRemoved()
    {
        var tree = new AfterSalesQualityCategoryTree();
        var parent = tree.Add(null); var child = tree.Add(parent.Uid);
        var request = tree.CreateRequest();
        Assert.AreEqual(2, request.CreateItems.Count);
        Assert.AreEqual(parent.Uid, request.CreateItems.Single(x => x.Data.Uid == child.Uid).Data.ParentId);
        Assert.IsTrue(child.IsEnabled);
        tree.Remove(parent);
        Assert.IsFalse(tree.CreateRequest().HasChanges);
        Assert.IsFalse(tree.Lines.HasChanges);
        Assert.AreEqual(0, tree.Roots.Count);
    }

    /// <summary>禁止移入自身后代，合法移动更新排序，删除持久化子树保留版本。</summary>
    [TestMethod]
    public void MoveRejectsCycleAndDeletePreservesVersions()
    {
        var root = Category("根"); var child = Category("子", root.Uid); var other = Category("其他");
        var tree = new AfterSalesQualityCategoryTree(); tree.Load([root, child, other]);
        var r = tree.Lines.Items.Single(x => x.Uid == root.Uid);
        var c = tree.Lines.Items.Single(x => x.Uid == child.Uid);
        Assert.IsFalse(tree.Move(r, c.Uid, 0));
        Assert.AreEqual(root.Uid, c.ParentId);
        Assert.IsTrue(tree.Move(c, other.Uid, 0));
        Assert.AreEqual(other.Uid, c.ParentId); Assert.AreEqual(1, c.SortNo);
        tree.Remove(tree.Lines.Items.Single(x => x.Uid == other.Uid));
        var request = tree.CreateRequest();
        Assert.AreEqual(2, request.DeleteItems.Count);
        Assert.IsTrue(request.DeleteItems.All(x => x.Version == 3));
    }

    /// <summary>名称、排序和编码在本地明确校验，展开不产生脏状态。</summary>
    [TestMethod]
    public void ValidatesFieldsAndIgnoresExpansion()
    {
        var tree = new AfterSalesQualityCategoryTree(); tree.Load([Category("父")]);
        tree.Roots[0].IsExpanded = false; Assert.IsFalse(tree.Lines.HasChanges);
        var first = tree.Roots[0]; var second = tree.Add(null);
        first.CategoryName = " "; Assert.IsNotNull(tree.Validate().Message);
        first.CategoryName = "父"; first.SortNo = -1; Assert.IsNotNull(tree.Validate().Message);
        first.SortNo = 1; first.CategoryCode = " A "; second.CategoryCode = "a";
        Assert.IsNotNull(tree.Validate().Message);
        second.CategoryCode = ""; Assert.IsNull(tree.Validate().Message);
    }

    /// <summary>服务端重分配新增身份后保留选中项，刷新失败阻止旧变更再次提交。</summary>
    [TestMethod]
    public async Task SaveReceiptsPreserveSelectionAndRefreshFailureBlocksResubmit()
    {
        var api = new CategoryApi(); using var vm = Manager(api, User(true)); await vm.InitializeAsync();
        vm.AddSiblingCommand.Execute(null); var temporary = vm.CurrentCategory!.Uid;
        vm.CurrentCategory.CategoryName = "新增"; await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreNotEqual(temporary, vm.CurrentCategory!.Uid);
        Assert.AreEqual(api.Items.Single().Uid, vm.CurrentCategory.Uid);
        Assert.IsFalse(vm.HasUnsavedChanges);
        vm.CurrentCategory.CategoryName = "修改"; await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(4, vm.CurrentCategory!.Version);
        Assert.AreEqual(3, api.LastRequest!.UpdateItems.Single().Version);
        api.FailRead = true; vm.CurrentCategory.CategoryName = "再修改"; await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.NeedsReload); Assert.IsFalse(vm.CanEdit);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null)); Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>服务端拒绝保留输入和版本，授予/撤回权限实时影响基类及界面准入。</summary>
    [TestMethod]
    public async Task FailedSaveAndPermissionChangesKeepConsistentState()
    {
        var user = User(false); var api = new CategoryApi { Items = [Category("初始")] };
        using var vm = Manager(api, user); await vm.InitializeAsync();
        Assert.IsFalse(vm.CanEdit); Assert.IsTrue(vm.IsReadOnly);
        SetPermissions(user, true); Assert.IsTrue(vm.CanEdit); Assert.IsFalse(vm.IsReadOnly);
        vm.CurrentCategory!.CategoryName = "修改"; api.FailSave = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasUnsavedChanges); Assert.AreEqual("修改", vm.CurrentCategory.CategoryName);
        Assert.AreEqual(3, vm.CurrentCategory.Version);
        StringAssert.Contains(vm.StatusMessage, "分类已被其他用户修改");
        api.FailSave = false; await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual("修改", api.Items.Single().CategoryName);
        SetPermissions(user, false); Assert.IsFalse(vm.CanEdit); Assert.IsTrue(vm.IsReadOnly);
        Assert.IsFalse(vm.AddChildCommand.CanExecute(null)); Assert.IsFalse(vm.Move(vm.CurrentCategory!, null, 0));
    }

    /// <summary>保存中的第二次执行不会重复发送，未确定提交期间不允许关闭。</summary>
    [TestMethod]
    public async Task PendingWriteRejectsReentryAndClose()
    {
        var completion = new TaskCompletionSource<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>>();
        var api = new CategoryApi { SaveOverride = _ => completion.Task };
        using var vm = Manager(api, User(true)); await vm.InitializeAsync(); vm.AddSiblingCommand.Execute(null);
        var pending = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy);
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, api.SaveCalls);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        completion.SetResult(ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail("拒绝"));
        await pending; Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>释放后取消读取并拒绝迟到响应。</summary>
    [TestMethod]
    public async Task DisposeRejectsLateLoad()
    {
        var completion = new TaskCompletionSource<ServiceResult<List<AfterSalesQualityCategoryDto>>>();
        CancellationToken captured = default;
        var api = new CategoryApi { ReadOverride = token => { captured = token; return completion.Task; } };
        var vm = Manager(api, User(true)); var pending = vm.InitializeAsync(); vm.Dispose();
        Assert.IsTrue(captured.IsCancellationRequested);
        completion.SetResult(ServiceResult<List<AfterSalesQualityCategoryDto>>.Ok([Category("迟到")]));
        await pending; Assert.AreEqual(0, vm.Tree.Roots.Count);
    }

    /// <summary>维护路由使用固定查看权限。</summary>
    [TestMethod]
    public void NavigationUsesQualityViewPermission()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(AfterSalesQualityCategoryManagerViewModel.RouteKey, out var route));
        Assert.AreEqual(typeof(AfterSalesQualityCategoryManagerViewModel), route.ViewModelType);
        Assert.AreEqual(PermissionKeys.QualityControlAfterSalesView, route.RequiredPermission);
    }

    private static AfterSalesQualityCategoryDto Category(string name, Guid? parent = null) => new() { Uid = Guid.NewGuid(), Version = 3, CategoryName = name, ParentId = parent, IsEnabled = true };
    private static CurrentUserService User(bool maintain) { var user = new CurrentUserService(); SetPermissions(user, maintain); return user; }
    private static void SetPermissions(CurrentUserService user, bool maintain) => user.SetCurrentUser(new MeDto { Permissions = maintain ? [PermissionKeys.QualityControlAfterSalesView, PermissionKeys.AfterSalesMaintain] : [PermissionKeys.QualityControlAfterSalesView] });
    private static AfterSalesQualityCategoryManagerViewModel Manager(CategoryApi api, CurrentUserService user) => new(api, new Runner(), user, new Dialogs());

    /// <summary>内存分类端口，支持延迟和失败注入。</summary>
    private sealed class CategoryApi : IAfterSalesQualityCategoryApi
    {
        public List<AfterSalesQualityCategoryDto> Items { get; set; } = [];
        public bool FailRead { get; set; }
        public bool FailSave { get; set; }
        public int SaveCalls { get; private set; }
        public AfterSalesQualityCategoryBatchSaveRequest? LastRequest { get; private set; }
        public Func<CancellationToken, Task<ServiceResult<List<AfterSalesQualityCategoryDto>>>>? ReadOverride { get; init; }
        public Func<AfterSalesQualityCategoryBatchSaveRequest, Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>>>? SaveOverride { get; init; }
        /// <inheritdoc/>
        public Task<ServiceResult<List<AfterSalesQualityCategoryDto>>> GetAllAsync(CancellationToken cancellationToken = default) => ReadOverride?.Invoke(cancellationToken) ?? Task.FromResult(FailRead ? ServiceResult<List<AfterSalesQualityCategoryDto>>.Fail("读取失败") : ServiceResult<List<AfterSalesQualityCategoryDto>>.Ok(Items));
        /// <inheritdoc/>
        public Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>> BatchSaveAsync(AfterSalesQualityCategoryBatchSaveRequest request, CancellationToken cancellationToken = default)
        {
            SaveCalls++; LastRequest = request;
            if (SaveOverride is not null) return SaveOverride(request);
            if (FailSave) return Task.FromResult(ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Fail("CONCURRENCY_CONFLICT", "版本变化"));
            var results = new List<SaveItemResult>();
            foreach (var create in request.CreateItems)
            {
                var item = Category(create.Data.CategoryName); Items.Add(item);
                results.Add(new() { ClientKey = create.ClientKey, Uid = item.Uid, NewVersion = item.Version, Success = true });
            }
            foreach (var update in request.UpdateItems)
            {
                var item = Items.Single(x => x.Uid == update.Uid); item.CategoryName = update.Data.CategoryName; item.Version++;
                results.Add(new() { ClientKey = update.ClientKey, Uid = item.Uid, NewVersion = item.Version, Success = true });
            }
            return Task.FromResult(ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>.Ok(new() { Items = results }));
        }
    }
    /// <summary>本地调用执行器，不访问网络。</summary>
    private sealed class Runner : IApiCallRunner
    {
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        /// <inheritdoc/>
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    /// <summary>本地确认替身，不创建 UI。</summary>
    private sealed class Dialogs : IDialogService
    {
        /// <inheritdoc/>
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        /// <inheritdoc/>
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <inheritdoc/>
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
