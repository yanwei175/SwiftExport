using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证产品页五个主型号拉取命令的选择、授权、确认和 Busy 边界。</summary>
[TestClass]
public sealed class ProductsPullMasterDataTests
{
    /// <summary>五个入口只传勾选产品 UID，未勾选当前行和同主型号其他产品不入请求。</summary>
    [TestMethod]
    [DataRow("descriptions")]
    [DataRow("features")]
    [DataRow("supplier")]
    [DataRow("weight-volume")]
    [DataRow("all")]
    public async Task Pull_UsesCheckedProductsAndCorrectApi(string group)
    {
        var calls = new List<(string Method, PullProductMasterDataRequest Request)>();
        using var vm = CreateVm(true, true, calls);
        vm.Products.LoadFromDtos([Dto("CURRENT"), Dto("CHECKED")]);
        vm.SelectedLine = vm.Products.Items[0];
        vm.Products.Items[1].IsChecked = true;
        var checkedId = vm.Products.Items[1].Uid;
        await Execute(vm, group);
        Assert.HasCount(1, calls);
        Assert.AreEqual(GroupMethod(group), calls[0].Method);
        CollectionAssert.AreEqual(new[] { checkedId }, calls[0].Request.ProductIds);
    }

    /// <summary>没有勾选时使用当前行；取消确认不发送请求。</summary>
    [TestMethod]
    public async Task Pull_FallsBackToCurrentLineAndHonorsCancel()
    {
        var calls = new List<(string Method, PullProductMasterDataRequest Request)>();
        using (var vm = CreateVm(true, true, calls))
        {
            var dto = Dto("CURRENT");
            vm.Products.LoadFromDtos([dto]);
            vm.SelectedLine = vm.Products.Items.Single();
            await vm.PullMasterAllCommand.ExecuteAsync(null);
            Assert.HasCount(1, calls);
            CollectionAssert.AreEqual(new[] { dto.Uid }, calls[0].Request.ProductIds);
        }
        calls.Clear();
        using var cancelled = CreateVm(true, false, calls);
        cancelled.Products.LoadFromDtos([Dto("CANCEL")]);
        cancelled.SelectedLine = cancelled.Products.Items.Single();
        await cancelled.PullMasterAllCommand.ExecuteAsync(null);
        Assert.IsEmpty(calls);
    }

    /// <summary>权限不足、页面草稿和新增产品均禁止命令执行。</summary>
    [TestMethod]
    public void Pull_BlocksPermissionDraftAndUnsavedLine()
    {
        using var denied = CreateVm(false, true, []);
        denied.Products.LoadFromDtos([Dto("A")]);
        denied.SelectedLine = denied.Products.Items.Single();
        Assert.IsFalse(denied.PullMasterAllCommand.CanExecute(null));

        using var dirty = CreateVm(true, true, []);
        dirty.Products.LoadFromDtos([Dto("B")]);
        dirty.SelectedLine = dirty.Products.Items.Single();
        dirty.SelectedLine.DescriptionEn = "草稿";
        Assert.IsFalse(dirty.PullMasterAllCommand.CanExecute(null));

        using var unsaved = CreateVm(true, true, []);
        unsaved.SelectedLine = unsaved.Products.AddNewProduct();
        Assert.IsFalse(unsaved.PullMasterAllCommand.CanExecute(null));
    }

    /// <summary>确认等待期间五个入口共享 Busy 门禁，不允许第二个拉取请求进入。</summary>
    [TestMethod]
    public async Task Pull_ConfirmationWaitBlocksOtherGroups()
    {
        var confirmation = new TaskCompletionSource<bool>();
        var calls = new List<(string Method, PullProductMasterDataRequest Request)>();
        using var vm = CreateVm(true, true, calls, confirmation.Task);
        vm.Products.LoadFromDtos([Dto("A")]);
        vm.SelectedLine = vm.Products.Items.Single();
        var pending = vm.PullMasterDescriptionsCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);
        Assert.IsFalse(vm.PullMasterAllCommand.CanExecute(null));
        confirmation.SetResult(false);
        await pending;
        Assert.IsFalse(vm.IsBusy);
        Assert.IsEmpty(calls);
    }

    /// <summary>确认等待期间出现草稿时取消发送，保留物料编辑而不调用获取或刷新。</summary>
    [TestMethod]
    public async Task Pull_DraftCreatedDuringConfirmationShouldRemainAndBlockRequest()
    {
        var confirmation = new TaskCompletionSource<bool>();
        var calls = new List<(string Method, PullProductMasterDataRequest Request)>();
        using var vm = CreateVm(true, true, calls, confirmation.Task);
        vm.Products.LoadFromDtos([Dto("DRAFT")]);
        vm.SelectedLine = vm.Products.Items.Single();
        var pending = vm.PullMasterFeaturesCommand.ExecuteAsync(null);
        vm.SelectedLine.FeaturesZh = "确认期间新增的物料配置";
        confirmation.SetResult(true);
        await pending;
        Assert.IsEmpty(calls);
        Assert.AreEqual("确认期间新增的物料配置", vm.SelectedLine.FeaturesZh);
        Assert.IsTrue(vm.Products.HasChanges);
    }

    /// <summary>获取失败不重载列表，保留当前物料资料、行身份及选择，只反馈一次失败。</summary>
    [TestMethod]
    public async Task Pull_FailureShouldKeepCurrentRowsWithoutRefreshing()
    {
        var calls = new List<(string Method, PullProductMasterDataRequest Request)>();
        var notices = new List<string>();
        var refreshes = 0;
        using var vm = CreateVm(true, true, calls, response: ServiceResult<PullProductMasterDataResponse>.Fail("获取失败"),
            notices: notices, refreshed: () => refreshes++);
        var dto = Dto("KEEP"); dto.DescriptionZh = "物料自己的描述";
        vm.Products.LoadFromDtos([dto]);
        var row = vm.SelectedLine = vm.Products.Items.Single();
        await vm.PullMasterDescriptionsCommand.ExecuteAsync(null);
        Assert.HasCount(1, calls);
        Assert.AreEqual(0, refreshes);
        Assert.AreSame(row, vm.SelectedLine);
        Assert.AreSame(row, vm.Products.Items.Single());
        Assert.AreEqual("物料自己的描述", row.DescriptionZh);
        Assert.HasCount(1, notices);
        StringAssert.Contains(notices[0], "获取失败");
    }

    /// <summary>无主型号的所选物料仍交给服务端精确跳过，不扩展到当前行或其他已关联物料。</summary>
    [TestMethod]
    public async Task Pull_UnlinkedSelectionShouldKeepExactScopeAndReportSkipped()
    {
        var calls = new List<(string Method, PullProductMasterDataRequest Request)>();
        var notices = new List<string>();
        using var vm = CreateVm(true, true, calls, response: ServiceResult<PullProductMasterDataResponse>.Ok(new()
        { SelectedProductCount = 1, SkippedProductCount = 1, UpdatedProductCount = 0 }), notices: notices);
        var unlinked = Dto("UNLINKED");
        var linked = Dto("LINKED"); linked.ProductMasterId = Guid.NewGuid();
        vm.Products.LoadFromDtos([unlinked, linked]);
        vm.SelectedLine = vm.Products.Items[1];
        vm.Products.Items[0].IsChecked = true;
        Assert.IsTrue(vm.PullMasterAllCommand.CanExecute(null));
        await vm.PullMasterAllCommand.ExecuteAsync(null);
        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { unlinked.Uid }, calls[0].Request.ProductIds);
        Assert.IsTrue(notices.Any(message => message.Contains("跳过 1 个", StringComparison.Ordinal)
            && message.Contains("实际更新 0 个", StringComparison.Ordinal)));
    }

    private static ProductsViewModel CreateVm(bool allowed, bool confirmed,
        List<(string Method, PullProductMasterDataRequest Request)> calls, Task<bool>? confirmation = null,
        ServiceResult<PullProductMasterDataResponse>? response = null, List<string>? notices = null, Action? refreshed = null)
    {
        var api = Proxy<IProductApi>((method, args) =>
        {
            if (method.Name == nameof(IProductApi.GetPagedAsync))
            {
                refreshed?.Invoke();
                return Task.FromResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new()
                {
                    PageIndex = 1, PageSize = 20, Items = []
                }));
            }
            calls.Add((method.Name, (PullProductMasterDataRequest)args[0]!));
            return Task.FromResult(response ?? ServiceResult<PullProductMasterDataResponse>.Ok(new()
            {
                SelectedProductCount = ((PullProductMasterDataRequest)args[0]!).ProductIds.Count,
                UpdatedProductCount = 1
            }));
        });
        var runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
        var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync)
            ? confirmation ?? Task.FromResult(confirmed) : Task.CompletedTask);
        var user = Proxy<ICurrentUserService>((method, args) => method.Name == nameof(ICurrentUserService.HasPermission)
            ? allowed && (string)args[0]! == PermissionKeys.ProductsProductEdit : null);
        var bom = new BomDrawerViewModel(null!, api, runner, null!, dialogs);
        return new ProductsViewModel(api, null!, null!, runner, null!,
            Proxy<IMessageService>((_, args) => { notices?.Add((string)args[1]!); return null; }), dialogs, bom, null!, user);
    }

    private static ProductDto Dto(string sku) => new() { Uid = Guid.NewGuid(), Version = 1, Sku = sku };

    private static Task Execute(ProductsViewModel vm, string group) => group switch
    {
        "descriptions" => vm.PullMasterDescriptionsCommand.ExecuteAsync(null),
        "features" => vm.PullMasterFeaturesCommand.ExecuteAsync(null),
        "supplier" => vm.PullMasterSupplierCommand.ExecuteAsync(null),
        "weight-volume" => vm.PullMasterWeightVolumeCommand.ExecuteAsync(null),
        _ => vm.PullMasterAllCommand.ExecuteAsync(null)
    };

    private static string GroupMethod(string group) => group switch
    {
        "descriptions" => nameof(IProductApi.PullMasterDescriptionsAsync),
        "features" => nameof(IProductApi.PullMasterFeaturesAsync),
        "supplier" => nameof(IProductApi.PullMasterSupplierAsync),
        "weight-volume" => nameof(IProductApi.PullMasterWeightVolumeAsync),
        _ => nameof(IProductApi.PullMasterAllAsync)
    };

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }
}
