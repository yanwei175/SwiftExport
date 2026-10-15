using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证管理页批量主图使用明确勾选集，保护草稿、权限、在途请求和页面释放。</summary>
[TestClass]
public sealed class ProductMasterMainImageBatchTests
{
    /// <summary>当前行不作为隐含选集，成功只回填勾选行展示及版本，不重载整页。</summary>
    [TestMethod]
    public async Task Success_UsesCheckedRowsAndAcceptsOnlyImageProjection()
    {
        using var f = new Fixture(); var vm = f.ViewModel;
        vm.SelectedMaster = vm.Masters.Items[1];
        Assert.IsFalse(vm.SetMainImagesCommand.CanExecute(null));
        var row = vm.Masters.Items[0]; row.IsChecked = true;
        await vm.SetMainImagesCommand.ExecuteAsync(null);
        Assert.AreEqual(row.Uid, f.Request!.Items.Single().ProductMasterId);
        Assert.AreEqual(1, f.Request.Items[0].ExpectedVersion);
        Assert.AreEqual(2, row.Version); Assert.AreEqual("https://example.test/main.jpg", row.MainImagePath);
        Assert.AreEqual(10m, row.DefaultSalesPriceUsd); Assert.AreEqual(20m, row.DefaultPurchasePriceCny);
        Assert.AreEqual(1, vm.Masters.Items[1].Version); Assert.AreEqual("old.jpg", vm.Masters.Items[1].MainImagePath);
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.AreEqual(1, f.Calls);
    }

    /// <summary>无勾选、新增、草稿、单元格模式、权限和原生编辑失败均不能请求批量写入。</summary>
    [TestMethod]
    [DataRow("unchecked")]
    [DataRow("new")]
    [DataRow("dirty")]
    [DataRow("cell")]
    [DataRow("permission")]
    [DataRow("native")]
    [DataRow("cancel")]
    [DataRow("draft-in-confirm")]
    public async Task InvalidContext_NeverRequestsWrite(string scenario)
    {
        using var f = new Fixture(); var vm = f.ViewModel;
        var row = vm.Masters.Items[0]; row.IsChecked = scenario != "unchecked";
        switch (scenario)
        {
            case "new": row.IsChecked = false; vm.Masters.AddNewMaster().IsChecked = true; break;
            case "dirty": row.DefaultDescriptionZh = "保留草稿"; break;
            case "cell": vm.IsCellSelectionMode = true; break;
            case "permission": f.User.Clear(); break;
            case "native": vm.CommitPendingEdits = () => false; break;
            case "cancel": f.Confirm = false; break;
            case "draft-in-confirm": f.OnConfirm = () => row.DefaultDescriptionZh = "确认期间的修改"; break;
        }
        await vm.SetMainImagesCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Calls); Assert.AreEqual(1, row.Version); Assert.AreEqual("old.jpg", row.MainImagePath);
        if (scenario is "dirty" or "draft-in-confirm") Assert.IsTrue(row.IsDirty);
    }

    /// <summary>在途请求阻止重复执行和关闭；失败不接纳版本或主图，勾选仍保留。</summary>
    [TestMethod]
    public async Task InFlightFailure_BlocksReentryAndPreservesRows()
    {
        using var f = new Fixture(); var vm = f.ViewModel; vm.Masters.Items[0].IsChecked = true;
        f.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.SetMainImagesCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy); Assert.IsFalse(await vm.CanCloseAsync(default));
        await vm.SetMainImagesCommand.ExecuteAsync(null); Assert.AreEqual(1, f.Calls);
        f.Completion.SetResult(ServiceResult<BatchSetProductMasterMainImagesResponse>.Fail("VERSION_CONFLICT", "测试冲突"));
        await pending;
        Assert.AreEqual(1, vm.Masters.Items[0].Version); Assert.AreEqual("old.jpg", vm.Masters.Items[0].MainImagePath);
        Assert.IsTrue(vm.Masters.Items[0].IsChecked); Assert.IsFalse(vm.IsBusy);
    }

    /// <summary>页面释放或登出后到达的成功响应不能回填旧会话列表。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LateSuccess_AfterDisposeOrLogoutDoesNotWriteBack(bool logout)
    {
        using var f = new Fixture(); var vm = f.ViewModel; vm.Masters.Items[0].IsChecked = true;
        f.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.SetMainImagesCommand.ExecuteAsync(null);
        if (logout) f.User.Clear(); else vm.Dispose();
        f.Completion.SetResult(f.Success()); await pending;
        Assert.AreEqual(1, vm.Masters.Items[0].Version); Assert.AreEqual("old.jpg", vm.Masters.Items[0].MainImagePath);
    }

    /// <summary>请求期间其他详情重读已推进列表版本时，较晚到达的主图回执不能倒退该版本。</summary>
    [TestMethod]
    public async Task LateSuccess_DoesNotRegressNewerRowVersion()
    {
        using var f = new Fixture(); var vm = f.ViewModel; var row = vm.Masters.Items[0]; row.IsChecked = true;
        f.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.SetMainImagesCommand.ExecuteAsync(null);
        row.Version = 3;
        f.Completion.SetResult(f.Success()); await pending;
        Assert.AreEqual(3, row.Version); Assert.AreEqual("old.jpg", row.MainImagePath);
    }

    /// <summary>只接受批量主图调用，额外分页或详情请求会使测试直接失败。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly CurrentUserService User = new();
        internal readonly ProductMasterManagerViewModel ViewModel;
        internal int Calls; internal bool Confirm = true; internal Action? OnConfirm;
        internal BatchSetProductMasterMainImagesRequest? Request;
        internal TaskCompletionSource<ServiceResult<BatchSetProductMasterMainImagesResponse>>? Completion;
        internal Fixture()
        {
            User.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsMasterEdit] });
            var api = Proxy<IProductMasterApi>((method, args) =>
            {
                Assert.AreEqual(nameof(IProductMasterApi.SetMainImagesAsync), method.Name);
                Calls++; Request = (BatchSetProductMasterMainImagesRequest)args[0]!;
                return Completion?.Task ?? Task.FromResult(Success());
            });
            ViewModel = new(api, Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()), null!,
                Proxy<IMessageService>((_, _) => null), Proxy<IDialogService>((_, _) => { OnConfirm?.Invoke(); return Task.FromResult(Confirm); }),
                null!, null!, new ImageAddressResolverStub(), User);
            ViewModel.Masters.LoadFromDtos(Enumerable.Range(0, 2).Select(index => new ProductMasterListItemDto
            { Uid = Guid.NewGuid(), Version = 1, MainImagePath = "old.jpg", Data = new() { MasterSku = "M" + index } }));
        }
        internal ServiceResult<BatchSetProductMasterMainImagesResponse> Success() => ServiceResult<BatchSetProductMasterMainImagesResponse>.Ok(new()
        {
            Items = [new() { ProductMasterId = Request!.Items[0].ProductMasterId, NewVersion = 2,
                MainImagePath = "https://example.test/main.jpg", DefaultSalesPriceUsd = 10, DefaultPurchasePriceCny = 20 }]
        });
        /// <summary>释放本用例的页面订阅。</summary>
        public void Dispose() => ViewModel.Dispose();
    }
    /// <summary>复用既有接口代理类型，不创建网络或窗口。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    { var value = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>(); ((ProductMasterSupplierEditingTests.TestProxy)(object)value).InvokeMethod = call; return value; }
}
