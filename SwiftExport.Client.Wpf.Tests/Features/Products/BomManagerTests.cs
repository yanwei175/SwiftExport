using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.Services;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>以真实页面命令验证竞态、变更保护、失败保留和独立复制；接口通过显式委托替代。</summary>
[TestClass]
public sealed class BomManagerTests
{
    /// <summary>复制为目标下新增，保留组件资料但清除来源身份、版本、审计，来源完全不变。</summary>
    [TestMethod]
    public void Copy_ResetsIdentityAndCannotReplaceNonemptyTarget()
    {
        var source = Line("SOURCE"); source.Version = 9; source.CreatedBy = "原创建"; source.CreatedDate = DateTime.Now; source.UpdatedBy = "原更新";
        var list = new BomListModel(); list.CopyIntoEmpty("TARGET", [source]);
        var created = list.CreateSaveRequest().CreateItems.Single();
        Assert.IsTrue(list.HasChanges); Assert.AreEqual(Guid.Empty, created.Data.Uid); Assert.AreEqual(0, created.Data.Version);
        Assert.IsNull(created.Data.CreatedBy); Assert.IsNull(created.Data.CreatedDate); Assert.IsNull(created.Data.UpdatedBy);
        Assert.AreEqual("TARGET", created.Data.BomNo); Assert.AreEqual(source.SubSkuId, created.Data.SubSkuId);
        Assert.AreEqual(source.MainImage, created.Data.MainImage); Assert.AreEqual(source.Features, created.Data.Features);
        Assert.AreEqual("SOURCE", source.BomNo); Assert.AreEqual(9, source.Version); Assert.AreEqual("原创建", source.CreatedBy);
        Assert.ThrowsExactly<InvalidOperationException>(() => list.CopyIntoEmpty("OTHER", [source]));
    }

    /// <summary>旧详情不遵守取消也不能覆盖新选择，关闭释放后同样不能回写。</summary>
    [TestMethod]
    public async Task Selection_LatestRequestWinsAndDisposalCancelsReads()
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var first = Product("FIRST"); var second = Product("SECOND");
        var older = new TaskCompletionSource<ServiceResult<List<BomDto>>>();
        var newer = new TaskCompletionSource<ServiceResult<List<BomDto>>>();
        fixture.Load = (number, token) => { fixture.Tokens.Add(token); return number == "FIRST" ? older.Task : newer.Task; };
        var a = vm.SelectProductCommand.ExecuteAsync(first);
        var b = vm.SelectProductCommand.ExecuteAsync(second);
        Assert.IsTrue(fixture.Tokens[0].IsCancellationRequested);
        newer.SetResult(ServiceResult<List<BomDto>>.Ok([Line("SECOND")])); await b;
        older.SetResult(ServiceResult<List<BomDto>>.Ok([Line("FIRST")])); await a;
        Assert.AreSame(second, vm.SelectedProduct); Assert.AreEqual("SECOND", vm.Boms.Items.Single().BomNo);
        var pending = new TaskCompletionSource<ServiceResult<List<BomDto>>>();
        fixture.Load = (_, token) => { fixture.Tokens.Add(token); return pending.Task; };
        var closing = vm.SelectProductCommand.ExecuteAsync(first); vm.Dispose();
        Assert.IsTrue(fixture.Tokens.Last().IsCancellationRequested);
        pending.SetResult(ServiceResult<List<BomDto>>.Ok([Line("LATE")])); await closing;
        Assert.AreSame(second, vm.SelectedProduct); Assert.AreEqual("SECOND", vm.Boms.Items.Single().BomNo);
    }

    /// <summary>取消查询、分页、选择和关闭均保留输入及查询上下文，刷新失败也不丢弃编辑。</summary>
    [TestMethod]
    public async Task DirtyGuards_RestoreContextAndKeepEditsOnRefreshFailure()
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var original = vm.SelectedProduct; var line = vm.Boms.Items.Single(); line.Note = "未保存";
        fixture.Confirm = false; vm.Keyword = "NEW"; vm.BindingFilterIndex = 2;
        await vm.SearchCommand.ExecuteAsync(null);
        await ((IAsyncRelayCommand)vm.PageChangeCommand).ExecuteAsync(new PageChangeRequest { OldIndex = 1, NewIndex = 3, OldSize = 20, NewSize = 50 });
        await vm.SelectProductCommand.ExecuteAsync(Product("OTHER"));
        Assert.IsFalse(await vm.CanCloseAsync(default)); Assert.AreSame(original, vm.SelectedProduct);
        Assert.AreEqual(string.Empty, vm.Keyword); Assert.AreEqual(0, vm.BindingFilterIndex); Assert.AreEqual(1, vm.PageIndex); Assert.AreEqual(20, vm.PageSize);
        Assert.AreSame(line, vm.Boms.Items.Single()); Assert.AreEqual("未保存", line.Note); Assert.AreEqual(1, fixture.PageCalls);
        fixture.Confirm = true; fixture.Load = (_, _) => Task.FromResult(ServiceResult<List<BomDto>>.Fail("读取失败"));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(original, vm.SelectedProduct); Assert.AreSame(line, vm.Boms.Items.Single()); Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>写入期间禁止重复保存、编辑和关闭；版本冲突与结果未知保留输入，未知禁止直接重试。</summary>
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Save_FailureKeepsInputAndPreventsDuplicateSubmission(bool unknown)
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var line = vm.Boms.Items.Single(); line.Note = "KEEP";
        var completion = new TaskCompletionSource<ServiceResult<BomBatchSaveResponse>>(); fixture.Save = _ => completion.Task;
        var first = vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy); Assert.IsFalse(vm.SaveCommand.CanExecute(null)); Assert.IsFalse(vm.CanEditLine(line));
        Assert.IsFalse(await vm.CanCloseAsync(default)); await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.SaveCalls);
        completion.SetResult(ServiceResult<BomBatchSaveResponse>.Fail(unknown ? ServiceErrorCodes.OutcomeUnknown : ServiceErrorCodes.VersionConflict, "保存失败"));
        await first;
        Assert.AreSame(line, vm.Boms.Items.Single()); Assert.AreEqual("KEEP", line.Note); Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.AreEqual(unknown, vm.IsSaveOutcomeUnknown); Assert.AreEqual(!unknown, vm.SaveCommand.CanExecute(null));
    }

    /// <summary>成功回执先回填新增身份并接纳基线，后续刷新失败不能再次提交已成功新增。</summary>
    [TestMethod]
    public async Task Save_SuccessThenRefreshFailureAcceptsReceiptAndWarnsSeparately()
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var added = vm.Boms.AddNewItem("ROOT"); added.SubSkuId = Guid.NewGuid(); added.SubSku = "NEW";
        var id = Guid.NewGuid(); fixture.Save = request =>
        {
            fixture.Load = (_, _) => Task.FromResult(ServiceResult<List<BomDto>>.Fail("刷新失败"));
            return Task.FromResult(ServiceResult<BomBatchSaveResponse>.Ok(new() { Items = [new() { ClientKey = request.CreateItems.Single().ClientKey, Uid = id, NewVersion = 1, Success = true, Operation = SaveOperation.Create }] }));
        };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(id, added.Uid); Assert.AreEqual(1, added.Version); Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.AreEqual(0, vm.Boms.CreateSaveRequest().CreateItems.Count);
        Assert.IsTrue(fixture.Notices.Any(message => message.Contains("刷新失败", StringComparison.Ordinal)));
    }

    /// <summary>空成功响应不能被当作全部变更已保存，须保留编辑并核实结果。</summary>
    [TestMethod]
    public async Task Save_IncompleteReceiptKeepsEditingAndRequiresVerification()
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var line = vm.Boms.Items.Single(); line.Note = "保留";
        fixture.Save = _ => Task.FromResult(ServiceResult<BomBatchSaveResponse>.Ok(new()));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsSaveOutcomeUnknown); Assert.IsTrue(vm.HasUnsavedChanges); Assert.AreSame(line, vm.Boms.Items.Single());
        Assert.IsFalse(vm.SaveCommand.CanExecute(null)); Assert.AreEqual("保留", line.Note);
    }

    /// <summary>比例、重复和自引用阻断保存；只读结构要求先保存；共享确认取消不发起写入。</summary>
    [TestMethod] [DataRow("ratio")] [DataRow("duplicate")] [DataRow("self")] [DataRow("shared")]
    public async Task Save_ValidatesLocalStructureAndSharedImpact(string scenario)
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var line = vm.Boms.Items.Single(); line.Note = "dirty";
        if (scenario == "ratio") line.Numerator = 0;
        if (scenario == "self") line.SubSkuId = vm.SelectedProduct!.Uid;
        if (scenario == "duplicate") { var duplicate = vm.Boms.AddNewItem("ROOT"); duplicate.SubSkuId = line.SubSkuId; duplicate.SubSku = line.SubSku; }
        if (scenario == "shared") { fixture.SharedCount = 3; fixture.Confirm = false; }
        await vm.ValidateCommand.ExecuteAsync(null); await vm.ShowTreeCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.SaveCalls); Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.IsTrue(fixture.Notices.Any(message => message.Contains("先保存", StringComparison.Ordinal)));
        if (scenario == "shared") StringAssert.Contains(fixture.ConfirmationText!, "3 个产品");
    }

    /// <summary>绑定弹窗未知结果不允许重复提交，输入和版本保持原值供关闭后核实。</summary>
    [TestMethod]
    public async Task BindingDialog_UnknownOutcomeBlocksRetry()
    {
        var service = Proxy<IBomBindingClientService>((method, _) => method.Name == "PreviewAsync"
            ? Task.FromResult(ServiceResult<BomNumberInfoDto>.Ok(new()))
            : Task.FromResult(ServiceResult<SaveItemResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知")));
        using var vm = new BomBindingDialogViewModel(service, Product("ROOT"));
        vm.BomNo = "INPUT"; await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.OutcomeUnknown); Assert.IsFalse(vm.SaveCommand.CanExecute(null)); Assert.AreEqual("INPUT", vm.BomNo); Assert.IsNull(vm.Saved);
    }

    /// <summary>主查询与子件 Lookup 分别取消前次请求，不遵守取消的旧结果仍不能回写。</summary>
    [TestMethod]
    public async Task SearchAndLookup_UseIndependentLatestRequests()
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var older = new TaskCompletionSource<ServiceResult<PagedResponseBase<BomProductListItemDto>>>();
        var newer = new TaskCompletionSource<ServiceResult<PagedResponseBase<BomProductListItemDto>>>();
        fixture.Search = (request, token) => { fixture.Tokens.Add(token); return request.Keyword == "old" ? older.Task : newer.Task; };
        vm.Keyword = "old"; var a = vm.SearchCommand.ExecuteAsync(null); vm.Keyword = "new"; var b = vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.Tokens[0].IsCancellationRequested);
        newer.SetResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new() { Items = [new() { Uid = Guid.NewGuid(), Sku = "NEW" }] })); await b;
        older.SetResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new() { Items = [new() { Uid = Guid.NewGuid(), Sku = "OLD" }] })); await a;
        Assert.AreEqual("NEW", vm.Products.Single().Sku); Assert.AreEqual("new", vm.Keyword);
        var oldLookup = new TaskCompletionSource<ServiceResult<List<ProductLookupDto>>>(); var newLookup = new TaskCompletionSource<ServiceResult<List<ProductLookupDto>>>();
        fixture.Lookup = (request, token) => { fixture.Tokens.Add(token); Assert.AreEqual(vm.SelectedProduct!.Uid, request.ExcludeProductId); return request.Keyword == "old" ? oldLookup.Task : newLookup.Task; };
        a = vm.SearchProductsCommand.ExecuteAsync("old"); b = vm.SearchProductsCommand.ExecuteAsync("new");
        Assert.IsTrue(fixture.Tokens[^2].IsCancellationRequested);
        newLookup.SetResult(ServiceResult<List<ProductLookupDto>>.Ok([new() { Sku = "LATEST" }])); await b;
        oldLookup.SetResult(ServiceResult<List<ProductLookupDto>>.Ok([new() { Sku = "STALE" }])); await a;
        Assert.AreEqual("LATEST", vm.ProductCandidates.Single().Sku); Assert.IsFalse(vm.IsLookupBusy);
    }

    /// <summary>来源弹窗使用自身分页大小与最新请求，目标及空结构不作为默认可确认来源。</summary>
    [TestMethod]
    public async Task SourceDialog_CancelsOldPageAndForwardsPageSize()
    {
        var targetId = Guid.NewGuid(); var tokens = new List<CancellationToken>();
        var old = new TaskCompletionSource<ServiceResult<PagedResponseBase<BomProductListItemDto>>>();
        var current = new TaskCompletionSource<ServiceResult<PagedResponseBase<BomProductListItemDto>>>();
        var api = Proxy<IBomApi>((method, args) =>
        {
            Assert.AreEqual("SearchProductsAsync", method.Name); var request = (BomProductSearchRequest)args![0]!;
            Assert.AreEqual(true, request.IsBound); Assert.AreEqual(50, request.PageSize); tokens.Add((CancellationToken)args[1]!);
            return request.Keyword == "old" ? old.Task : current.Task;
        });
        using var model = new BomSourcePickerViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), targetId) { PageSize = 50, Keyword = "old" };
        var first = model.LoadAsync(); model.Keyword = "current"; var second = model.LoadAsync();
        Assert.IsTrue(tokens[0].IsCancellationRequested);
        var source = new BomProductListItemDto { Uid = Guid.NewGuid(), Sku = "SOURCE", IsBound = true, ComponentCount = 1 };
        current.SetResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new() { Items = [new() { Uid = targetId, ComponentCount = 1 }, new() { Uid = Guid.NewGuid(), ComponentCount = 0 }, source] })); await second;
        old.SetResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new())); await first;
        Assert.AreEqual(source.Uid, model.SelectedProduct!.Uid); Assert.IsTrue(model.IsPrimaryButtonEnabled);
        model.SelectedProduct = model.Products[0]; Assert.IsFalse(model.IsPrimaryButtonEnabled);
        model.Dispose(); Assert.IsTrue(tokens.Last().IsCancellationRequested); Assert.IsFalse(model.IsPrimaryButtonEnabled);
    }

    /// <summary>管理页和复制来源页接受分页器的真实对象参数，普通页、末页及页大小均传入查询。</summary>
    [TestMethod] [DataRow(2, 20)] [DataRow(11, 20)] [DataRow(1, 50)]
    public async Task PageNavigation_AcceptsControlRequestAndForwardsIndexAndSize(int index, int size)
    {
        var fixture = new Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        BomProductSearchRequest? managerRequest = null;
        fixture.Search = (request, _) =>
        {
            managerRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new()
            { PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 201, Items = [] }));
        };
        var change = new PageChangeRequest { OldIndex = 1, NewIndex = index, OldSize = 20, NewSize = size };
        Assert.IsTrue(((ICommand)vm.PageChangeCommand).CanExecute(change));
        await ((IAsyncRelayCommand)vm.PageChangeCommand).ExecuteAsync(change);
        Assert.AreEqual(index, managerRequest!.PageIndex); Assert.AreEqual(size, managerRequest.PageSize);
        Assert.AreEqual(index, vm.PageIndex); Assert.AreEqual(size, vm.PageSize);

        BomProductSearchRequest? sourceRequest = null;
        var api = Proxy<IBomApi>((method, args) =>
        {
            Assert.AreEqual("SearchProductsAsync", method.Name); sourceRequest = (BomProductSearchRequest)args![0]!;
            return Task.FromResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new()
            { PageIndex = sourceRequest.PageIndex, PageSize = sourceRequest.PageSize, TotalCount = 201, Items = [] }));
        });
        using var source = new BomSourcePickerViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), Guid.NewGuid());
        Assert.IsTrue(((ICommand)source.PageChangeCommand).CanExecute(change));
        await ((IAsyncRelayCommand)source.PageChangeCommand).ExecuteAsync(change);
        Assert.AreEqual(index, sourceRequest!.PageIndex); Assert.AreEqual(size, sourceRequest.PageSize);
        Assert.AreEqual(index, source.PageIndex); Assert.AreEqual(size, source.PageSize);
    }

    /// <summary>正式路由使用新页面；重复打开保留编辑实例，取消关闭不丢失变更。</summary>
    [TestMethod]
    public async Task Shell_ReusesBomTabAndPreservesEditing()
    {
        var registry = new NavigationRouteRegistry(); Assert.IsTrue(registry.TryGetRoute("product-bom", out var route));
        Assert.AreEqual("BOM 管理", route.Title); Assert.AreEqual(typeof(BomManagerViewModel), route.ViewModelType);
        Assert.AreEqual(PermissionKeys.ProductsBomView, route.RequiredPermission);
        var fixture = new Fixture(); using var vm = fixture.Create();
        var creates = 0;
        var lease = Proxy<INavigationTabLease>((method, _) => method.Name == "get_ViewModel" ? vm : null);
        var factory = Proxy<INavigationTabFactory>((_, _) => { creates++; return lease; });
        var user = new CurrentUserService(); user.SetCurrentUser(new() { UserId = "test", Permissions = [PermissionKeys.ProductsBomView] });
        using var navigation = new NavigationService(factory, registry, new NavigationAccessService(registry, user), user,
            Proxy<IStatusMessageService>((_, _) => null), NullLogger<NavigationService>.Instance);
        await navigation.OpenAsync("product-bom"); var first = navigation.SelectedTab!;
        vm.Boms.Items.Single().Note = "编辑中"; await navigation.OpenAsync("product-bom");
        Assert.AreEqual(1, creates); Assert.AreEqual(1, navigation.OpenTabs.Count); Assert.AreSame(first, navigation.SelectedTab);
        Assert.IsTrue(first.IsDirty); Assert.AreEqual("编辑中", vm.Boms.Items.Single().Note);
        fixture.Confirm = false; await navigation.CloseAsync(first); Assert.AreEqual(1, navigation.OpenTabs.Count);
    }

    internal static BomDto Line(string number) => new() { Uid = Guid.NewGuid(), Version = 2, BomNo = number, SubSkuId = Guid.NewGuid(), SubSku = "CHILD", Numerator = 2, Denominator = 3, MainImage = "img.jpg", Features = "配置" };
    internal static BomProductItemModel Product(string number) => new() { Uid = Guid.NewGuid(), Version = 3, Sku = number, BomNo = number, BomVersion = 1, IsBound = true };

    /// <summary>暴露可控时序及请求计数的无网络夹具，供命令与真实视图绑定测试共同使用。</summary>
    internal sealed class Fixture
    {
        public Func<string, CancellationToken, Task<ServiceResult<List<BomDto>>>> Load = (number, _) => Task.FromResult(ServiceResult<List<BomDto>>.Ok([Line(number)]));
        public Func<BomBatchSaveRequest, Task<ServiceResult<BomBatchSaveResponse>>> Save = _ => Task.FromResult(ServiceResult<BomBatchSaveResponse>.Fail("未配置保存"));
        public Func<BomProductSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<BomProductListItemDto>>>>? Search;
        public Func<ProductLookupRequest, CancellationToken, Task<ServiceResult<List<ProductLookupDto>>>>? Lookup;
        public bool Confirm = true;
        public int SharedCount = 1, PageCalls, SaveCalls;
        public string? ConfirmationText;
        public readonly List<CancellationToken> Tokens = [];
        public readonly List<string> Notices = [];
        public BomManagerViewModel Create()
        {
            var parentId = Guid.NewGuid();
            var api = Proxy<IBomApi>((method, args) => method.Name switch
            {
                "SearchProductsAsync" => Search?.Invoke((BomProductSearchRequest)args![0]!, (CancellationToken)args[1]!) ?? Page((BomProductSearchRequest)args![0]!, parentId),
                "GetBomByBomNo" => Load((string)args![0]!, (CancellationToken)args[1]!),
                "GetNumberInfoAsync" => Task.FromResult(ServiceResult<BomNumberInfoDto>.Ok(new() { BoundProductCount = SharedCount, ComponentCount = 1, StoredComponentCount = 1 })),
                "BatchSaveAsync" => Write((BomBatchSaveRequest)args![0]!),
                _ => throw new AssertFailedException("不应调用已保存结构接口：" + method.Name)
            });
            var user = new CurrentUserService(); user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsProductView, PermissionKeys.ProductsProductEdit, PermissionKeys.ProductsBomView, PermissionKeys.ProductsBomCreate, PermissionKeys.ProductsBomEdit, PermissionKeys.ProductsBomDelete] });
            var cache = new ReferenceDataCacheServiceStub();
            var dialogs = Proxy<IDialogService>((method, args) => { if (method.Name != "ConfirmAsync") throw new AssertFailedException(method.Name); ConfirmationText = (string)args![1]!; return Task.FromResult(Confirm); });
            return new(api, Proxy<IProductApi>((_, args) => Lookup?.Invoke((ProductLookupRequest)args![0]!, (CancellationToken)args[1]!) ?? throw new AssertFailedException("意外 Lookup")), new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), cache, user,
                new Messages(Notices), dialogs, Proxy<IBomManagementDialogService>((_, _) => throw new AssertFailedException("意外弹窗")), NullLogger<BomManagerViewModel>.Instance);
        }
        private Task<ServiceResult<PagedResponseBase<BomProductListItemDto>>> Page(BomProductSearchRequest request, Guid id)
        { PageCalls++; return Task.FromResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new() { PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 1, Items = [new() { Uid = id, Version = 3, Sku = "ROOT", BomNo = "ROOT", BomVersion = 1, IsBound = true }] })); }
        private Task<ServiceResult<BomBatchSaveResponse>> Write(BomBatchSaveRequest request) { SaveCalls++; return Save(request); }
    }
    internal static T Proxy<T>(Func<MethodInfo, object?[]?, object?> action) where T : class
    { var result = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)result).Action = action; return result; }
    /// <summary>仅调度测试显式允许的接口成员。</summary>
    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Action { get; set; } = null!;
        /// <summary>把真实命令产生的调用转交夹具。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Action(targetMethod!, args);
    }
    /// <summary>记录反馈文本，避免单元测试弹出通知。</summary>
    private sealed class Messages(List<string> notices) : IMessageService
    {
        public void ShowInfo(string title, string message) => notices.Add(message);
        public void ShowSuccess(string title, string message) => notices.Add(message);
        public void ShowWarning(string title, string message) => notices.Add(message);
        public void ShowError(string title, string message) => notices.Add(message);
    }
}
