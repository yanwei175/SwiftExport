using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Services;

[TestClass]
public sealed class ReferenceDataCacheServiceTests
{
    [TestMethod]
    public async Task EnsureLoadedAsync_ShouldCoalesceConcurrentFirstLoad()
    {
        var colorGate = new TaskCompletionSource<ServiceResult<List<ProductColorDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var categoryGate = new TaskCompletionSource<ServiceResult<List<ProductCategoryDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var colorApi = new ProductColorApiStub(() => colorGate.Task);
        var categoryApi = new ProductCategoryApiStub(() => categoryGate.Task);
        var currencyApi = new CurrencyApiStub(() => SuccessCurrencies(Currency("USD")));
        using var cache = CreateCache(colorApi, categoryApi, currencyApi: currencyApi);

        var first = cache.EnsureLoadedAsync();
        var second = cache.EnsureLoadedAsync();

        colorGate.SetResult(ServiceResult<List<ProductColorDto>>.Ok([Color("RED")]));
        categoryGate.SetResult(ServiceResult<List<ProductCategoryDto>>.Ok([Category("ROOT", 10)]));
        var results = await Task.WhenAll(first, second);
        var third = await cache.EnsureLoadedAsync();

        Assert.IsTrue(results.All(result => result.Success));
        Assert.IsTrue(third.Success);
        Assert.AreEqual(1, colorApi.GetAllCallCount);
        Assert.AreEqual(1, categoryApi.GetAllCallCount);
        Assert.AreEqual(1, currencyApi.GetEnabledCallCount);
        Assert.AreEqual("USD", cache.Currencies.Single().CurrencyCode);
    }

    [TestMethod]
    public async Task RefreshAllAsync_ShouldBuildSortedTreeAndPromoteInvalidRelationships()
    {
        var root = Category("ROOT", 20);
        var child = Category("CHILD", 1, root.Uid);
        var orphan = Category("ORPHAN", 5, Guid.NewGuid());
        var self = Category("SELF", 6);
        self.ParentId = self.Uid;
        var cycleA = Category("CYCLE-A", 7);
        var cycleB = Category("CYCLE-B", 8, cycleA.Uid);
        cycleA.ParentId = cycleB.Uid;
        var colorApi = new ProductColorApiStub(() => SuccessColors(Color("BLUE"), Color("RED")));
        var categoryApi = new ProductCategoryApiStub(
            () => SuccessCategories(root, child, orphan, self, cycleA, cycleB));
        using var cache = CreateCache(colorApi, categoryApi);

        var result = await cache.RefreshAllAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, cache.ProductColors.Count);
        var cachedRoot = cache.ProductCategoryTree.Single(node => node.Uid == root.Uid);
        Assert.AreEqual(child.Uid, cachedRoot.Children.Single().Uid);
        CollectionAssert.IsSubsetOf(
            new[] { orphan.Uid, self.Uid, cycleA.Uid, cycleB.Uid },
            cache.ProductCategoryTree.Select(node => node.Uid).ToArray());
        CollectionAssert.AreEqual(
            cache.ProductCategoryTree.OrderBy(node => node.SortNo).ThenBy(node => node.CategoryName).Select(node => node.Uid).ToArray(),
            cache.ProductCategoryTree.Select(node => node.Uid).ToArray());
        CollectionAssert.AreEqual(
            new[] { child.Uid },
            cache.ProductCategories.Where(node => node.ParentId == root.Uid).Select(node => node.Uid).ToArray());
        Assert.AreSame(cachedRoot, cache.ProductCategories.Single(node => node.Uid == root.Uid));
        Assert.AreEqual("ROOT / CHILD", cache.ProductCategories.Single(node => node.Uid == child.Uid).CategoryPath);
    }

    [TestMethod]
    public async Task Refresh_ShouldKeepCollectionInstanceAndPublishSingleReset()
    {
        var colorResponses = new Queue<ServiceResult<List<ProductColorDto>>>(
        [
            ServiceResult<List<ProductColorDto>>.Ok([Color("OLD")]),
            ServiceResult<List<ProductColorDto>>.Ok([Color("NEW")])
        ]);
        var colorApi = new ProductColorApiStub(() => Task.FromResult(colorResponses.Dequeue()));
        using var cache = CreateCache(colorApi, new ProductCategoryApiStub(() => SuccessCategories()));
        var collection = cache.ProductColors;
        var resetCount = 0;
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        await cache.RefreshProductColorsAsync();
        await cache.RefreshProductColorsAsync();

        Assert.AreSame(collection, cache.ProductColors);
        Assert.AreEqual(2, resetCount);
        Assert.AreEqual("NEW", cache.ProductColors.Single().ColorCode);
    }

    [TestMethod]
    public async Task RefreshCategories_ShouldKeepTreeAndFlatCollectionInstances()
    {
        var firstRoot = Category("FIRST", 1);
        var secondRoot = Category("SECOND", 1);
        var secondChild = Category("CHILD", 1, secondRoot.Uid);
        var responses = new Queue<ServiceResult<List<ProductCategoryDto>>>(
        [
            ServiceResult<List<ProductCategoryDto>>.Ok([firstRoot]),
            ServiceResult<List<ProductCategoryDto>>.Ok([secondRoot, secondChild]),
        ]);
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => Task.FromResult(responses.Dequeue())));
        var tree = cache.ProductCategoryTree;
        var flat = cache.ProductCategories;
        var flatResetCount = 0;
        ((INotifyCollectionChanged)flat).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                flatResetCount++;
            }
        };

        await cache.RefreshProductCategoriesAsync();
        await cache.RefreshProductCategoriesAsync();

        Assert.AreSame(tree, cache.ProductCategoryTree);
        Assert.AreSame(flat, cache.ProductCategories);
        Assert.AreEqual(2, flatResetCount);
        CollectionAssert.AreEqual(new[] { secondRoot.Uid, secondChild.Uid }, flat.Select(node => node.Uid).ToArray());
        Assert.AreEqual("SECOND / CHILD", flat[1].CategoryPath);
    }

    [TestMethod]
    public async Task RefreshDictionaries_ShouldKeepInstancesSortAndFilterEnabledValues()
    {
        var customType = new SysDictTypeDto
        {
            Uid = Guid.NewGuid(),
            DictCode = "CustomStatus",
            DictName = "自定义状态",
            SortOrder = 20
        };
        var systemType = new SysDictTypeDto
        {
            Uid = Guid.NewGuid(),
            DictCode = "PaymentMethod",
            DictName = "支付方式",
            SortOrder = 10,
            IsSystem = true
        };
        var disabled = new SysDictDataDto
        {
            Uid = Guid.NewGuid(),
            DictCode = "PaymentMethod",
            DataNameEn = "OLD",
            DataNameZh = "停用",
            SortOrder = 1,
            IsEnabled = false
        };
        var enabled = new SysDictDataDto
        {
            Uid = Guid.NewGuid(),
            DictCode = "PaymentMethod",
            DataNameEn = "TT",
            DataNameZh = "电汇",
            SortOrder = 2,
            IsEnabled = true
        };
        var typeApi = new DictTypeApiStub(() => SuccessDictionaryTypes(customType, systemType));
        var dataApi = new DictDataApiStub(() => SuccessDictionaryValues(enabled, disabled));
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            dictTypeApi: typeApi,
            dictDataApi: dataApi);
        var types = cache.DictionaryTypes;
        var values = cache.DictionaryValues;

        var result = await cache.RefreshDictionariesAsync();

        Assert.IsTrue(result.Success);
        Assert.AreSame(types, cache.DictionaryTypes);
        Assert.AreSame(values, cache.DictionaryValues);
        CollectionAssert.AreEqual(
            new[] { systemType.Uid, customType.Uid },
            cache.DictionaryTypes.Select(item => item.Uid).ToArray());
        CollectionAssert.AreEqual(
            new[] { disabled.Uid, enabled.Uid },
            cache.DictionaryValues.Select(item => item.Uid).ToArray());
        CollectionAssert.AreEqual(
            new[] { enabled.Uid },
            cache.GetDictionaryValues("paymentmethod").Select(item => item.Uid).ToArray());
        CollectionAssert.AreEqual(
            new[] { disabled.Uid, enabled.Uid },
            cache.GetDictionaryValues("PaymentMethod", enabledOnly: false).Select(item => item.Uid).ToArray());
        Assert.AreEqual(1, typeApi.GetAllCallCount);
        Assert.AreEqual(1, dataApi.GetAllCallCount);
    }

    [TestMethod]
    public async Task RefreshDictionaries_WhenOneRequestFails_ShouldRetainBothOldSnapshots()
    {
        var oldType = new SysDictTypeDto
        {
            Uid = Guid.NewGuid(),
            DictCode = "Old",
            DictName = "旧字典"
        };
        var oldValue = new SysDictDataDto
        {
            Uid = Guid.NewGuid(),
            DictCode = "Old",
            DataNameEn = "OLD",
            DataNameZh = "旧值",
            IsEnabled = true
        };
        var typeResponses = new Queue<ServiceResult<IReadOnlyList<SysDictTypeDto>>>(
        [
            ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok([oldType]),
            ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(
                [new SysDictTypeDto { Uid = Guid.NewGuid(), DictCode = "New", DictName = "新字典" }])
        ]);
        var dataResponses = new Queue<ServiceResult<IReadOnlyList<SysDictDataDto>>>(
        [
            ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok([oldValue]),
            ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("字典值接口失败")
        ]);
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            dictTypeApi: new DictTypeApiStub(() => Task.FromResult(typeResponses.Dequeue())),
            dictDataApi: new DictDataApiStub(() => Task.FromResult(dataResponses.Dequeue())));

        Assert.IsTrue((await cache.RefreshDictionariesAsync()).Success);
        var failed = await cache.RefreshDictionariesAsync();

        Assert.IsFalse(failed.Success);
        Assert.AreEqual(oldType.Uid, cache.DictionaryTypes.Single().Uid);
        Assert.AreEqual(oldValue.Uid, cache.DictionaryValues.Single().Uid);
    }

    [TestMethod]
    public async Task RefreshAllAsync_WhenOneRequestFails_ShouldRetainBothOldSnapshots()
    {
        var oldColor = Color("OLD");
        var oldCategory = Category("OLD", 1);
        var colorResponses = new Queue<ServiceResult<List<ProductColorDto>>>(
        [
            ServiceResult<List<ProductColorDto>>.Ok([oldColor]),
            ServiceResult<List<ProductColorDto>>.Ok([Color("NEW")])
        ]);
        var categoryResponses = new Queue<ServiceResult<List<ProductCategoryDto>>>(
        [
            ServiceResult<List<ProductCategoryDto>>.Ok([oldCategory]),
            ServiceResult<List<ProductCategoryDto>>.Fail("分类接口失败")
        ]);
        using var cache = CreateCache(
            new ProductColorApiStub(() => Task.FromResult(colorResponses.Dequeue())),
            new ProductCategoryApiStub(() => Task.FromResult(categoryResponses.Dequeue())));

        Assert.IsTrue((await cache.RefreshAllAsync()).Success);
        var failed = await cache.RefreshAllAsync();

        Assert.IsFalse(failed.Success);
        Assert.AreEqual(oldColor.Uid, cache.ProductColors.Single().Uid);
        Assert.AreEqual(oldCategory.Uid, cache.ProductCategoryTree.Single().Uid);
        Assert.AreEqual(oldCategory.Uid, cache.ProductCategories.Single().Uid);
    }

    [TestMethod]
    public async Task SessionEnded_ShouldClearAndRequireNextSessionReload()
    {
        var session = new SessionServiceStub();
        var colorApi = new ProductColorApiStub(() => SuccessColors(Color("RED")));
        var categoryApi = new ProductCategoryApiStub(() => SuccessCategories(Category("ROOT", 1)));
        using var cache = CreateCache(colorApi, categoryApi, session);
        await cache.EnsureLoadedAsync();

        session.EndSession(SessionEndReason.Logout);

        Assert.AreEqual(0, cache.ProductColors.Count);
        Assert.AreEqual(0, cache.ProductCategoryTree.Count);
        Assert.AreEqual(0, cache.ProductCategories.Count);
        Assert.AreEqual(0, cache.Currencies.Count);
        Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);
        Assert.AreEqual(2, colorApi.GetAllCallCount);
        Assert.AreEqual(2, categoryApi.GetAllCallCount);
    }

    [TestMethod]
    public async Task SessionEnded_DuringLoad_ShouldIgnorePreviousSessionResponse()
    {
        var session = new SessionServiceStub();
        var colorGate = new TaskCompletionSource<ServiceResult<List<ProductColorDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var categoryGate = new TaskCompletionSource<ServiceResult<List<ProductCategoryDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = CreateCache(
            new ProductColorApiStub(() => colorGate.Task),
            new ProductCategoryApiStub(() => categoryGate.Task),
            session);
        var loading = cache.EnsureLoadedAsync();

        session.EndSession(SessionEndReason.Logout);
        colorGate.SetResult(ServiceResult<List<ProductColorDto>>.Ok([Color("OLD-SESSION")]));
        categoryGate.SetResult(ServiceResult<List<ProductCategoryDto>>.Ok([Category("OLD-SESSION", 1)]));
        var result = await loading;

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, cache.ProductColors.Count);
        Assert.AreEqual(0, cache.ProductCategoryTree.Count);
        Assert.AreEqual(0, cache.ProductCategories.Count);
    }

    [TestMethod]
    public void ProductColorGetAll_ShouldMatchControllerRoute()
    {
        var method = typeof(IProductColorApi).GetMethod(nameof(IProductColorApi.GetAllAsync));
        var get = method?.GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().SingleOrDefault();

        Assert.IsNotNull(method);
        Assert.IsNotNull(get);
        Assert.AreEqual("/all", get.Path);
    }

    [TestMethod]
    public void ClientServices_ShouldRegisterReferenceDataCacheAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSwiftExportClientServices(new ConfigurationBuilder().Build());

        var descriptor = services.Single(item => item.ServiceType == typeof(IReferenceDataCacheService));

        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.AreEqual(typeof(ReferenceDataCacheService), descriptor.ImplementationType);
    }

    [TestMethod]
    public void ProductCategoryLookup_ShouldFilterTreeAndKeepPopupOpenForRefreshFocus()
    {
        StaTest.RunAsync(async () =>
        {
            var root = Category("ROOT", 1);
            var child = Category("CHILD", 1, root.Uid);
            using var cache = CreateCache(
                new ProductColorApiStub(() => SuccessColors()),
                new ProductCategoryApiStub(() => SuccessCategories(root, child)));
            Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);

            var refreshCount = 0;
            var lookup = new ProductCategoryLookupTextBox
            {
                ItemsSource = cache.ProductCategoryTree,
                RefreshCommand = new RelayCommand(() => refreshCount++),
            };
            var window = new Window
            {
                Width = 420,
                Height = 180,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = (LookupTextBox?)lookup.FindName("LookupEditor");
                Assert.IsNotNull(editor);
                Assert.IsFalse(editor.IsDropDownOpen, "缓存初始化不得自动展开分类树。");

                var input = (TextBox?)editor.FindName("InputTextBox");
                Assert.IsNotNull(input);
                window.Activate();
                input.Focus();
                Keyboard.Focus(input);
                input.Text = "CHILD";
                await Task.Delay(360);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsTrue(editor.IsDropDownOpen);
                Assert.HasCount(1, lookup.FilteredRoots);
                Assert.AreEqual(root.Uid, lookup.FilteredRoots[0].Uid);
                Assert.HasCount(1, lookup.FilteredRoots[0].Children);
                Assert.AreEqual(child.Uid, lookup.FilteredRoots[0].Children[0].Uid);
                Assert.IsTrue(lookup.FilteredRoots[0].IsExpanded);

                var popupContent = (DependencyObject?)editor.CustomDropDownContent;
                Assert.IsNotNull(popupContent);
                var categoryTree = FindVisualDescendant<TreeView>(popupContent);
                Assert.IsNotNull(categoryTree);
                Assert.AreEqual(1, categoryTree.Items.Count);
                var buttons = FindVisualDescendants<Button>(popupContent).ToArray();
                var expansionButton = buttons.Single(button => button.Command is null);
                Assert.AreEqual("折叠全部", expansionButton.Content);
                expansionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsFalse(lookup.FilteredRoots[0].IsExpanded);
                Assert.AreEqual("展开全部", expansionButton.Content);
                expansionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(lookup.FilteredRoots[0].IsExpanded);
                Assert.AreEqual("折叠全部", expansionButton.Content);

                var refreshButton = buttons.Single(button => button.Command is not null);
                Assert.IsNotNull(refreshButton);
                Assert.IsNotNull(refreshButton.Command);
                refreshButton.Focus();
                Keyboard.Focus(refreshButton);
                refreshButton.Command.Execute(null);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsTrue(editor.IsDropDownOpen, "焦点进入 Popup 刷新按钮时不得关闭分类树。");
                Assert.AreEqual(1, refreshCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task ProductCategoryFilter_ShouldIncludeSelectedNodeAndAllDescendants()
    {
        var root = Category("ROOT", 1);
        var child = Category("CHILD", 1, root.Uid);
        var grandchild = Category("GRANDCHILD", 1, child.Uid);
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories(root, child, grandchild)));
        Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);

        var method = typeof(ProductsViewModel).GetMethod(
            "GetCategoryIds",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);
        var ids = ((IEnumerable<Guid>)method.Invoke(null, [cache.ProductCategoryTree.Single()])!).ToArray();

        CollectionAssert.AreEqual(new[] { root.Uid, child.Uid, grandchild.Uid }, ids);
    }

    private static ReferenceDataCacheService CreateCache(
        IProductColorApi colorApi,
        IProductCategoryApi categoryApi,
        ISessionService? session = null,
        ISysDicCurrencyApi? currencyApi = null,
        ISysDictTypeApi? dictTypeApi = null,
        ISysDictDataApi? dictDataApi = null) =>
        new(
            colorApi,
            categoryApi,
            currencyApi ?? new CurrencyApiStub(() => SuccessCurrencies()),
            dictTypeApi ?? new DictTypeApiStub(() => SuccessDictionaryTypes()),
            dictDataApi ?? new DictDataApiStub(() => SuccessDictionaryValues()),
            new DirectApiCallRunner(),
            session ?? new SessionServiceStub());

    private static ProductColorDto Color(string code) => new()
    {
        Uid = Guid.NewGuid(),
        ColorCode = code,
        ColorCodeUnique = code,
        ColorNameEn = code,
        ColorNameZh = code
    };

    private static ProductCategoryDto Category(string code, int sortNo, Guid? parentId = null) => new()
    {
        Uid = Guid.NewGuid(),
        CategoryCode = code,
        CategoryName = code,
        FakeName = code,
        SortNo = sortNo,
        ParentId = parentId
    };

    private static SysDicCurrencyDto Currency(string code) => new()
    {
        Uid = Guid.NewGuid(),
        CurrencyCode = code,
        CurrencyNameEn = code,
        CurrencyNameZh = code,
        IsEnabled = true,
    };

    private static Task<ServiceResult<List<ProductColorDto>>> SuccessColors(params ProductColorDto[] colors) =>
        Task.FromResult(ServiceResult<List<ProductColorDto>>.Ok(colors.ToList()));

    private static Task<ServiceResult<List<ProductCategoryDto>>> SuccessCategories(params ProductCategoryDto[] categories) =>
        Task.FromResult(ServiceResult<List<ProductCategoryDto>>.Ok(categories.ToList()));

    private static Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> SuccessDictionaryTypes(
        params SysDictTypeDto[] types) =>
        Task.FromResult(ServiceResult<IReadOnlyList<SysDictTypeDto>>.Ok(types));

    private static Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> SuccessDictionaryValues(
        params SysDictDataDto[] values) =>
        Task.FromResult(ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(values));

    private static Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> SuccessCurrencies(
        params SysDicCurrencyDto[] currencies) =>
        Task.FromResult(ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Ok(currencies));

    private sealed class CurrencyApiStub(
        Func<Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>>> getEnabled) : ISysDicCurrencyApi
    {
        public int GetEnabledCallCount { get; private set; }
        public Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync()
        {
            GetEnabledCallCount++;
            return getEnabled();
        }
        public Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetAllAsync() => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<SysDicCurrencyDto>>> GetPagedAsync(SysDicCurrencySearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<SysDicCurrencyBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync() => throw new NotSupportedException();
        public Task<ServiceResult<int>> SetNewBaseCurrency(SysDicCurrencyDto dto) => throw new NotSupportedException();
        public Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrency() => throw new NotSupportedException();
        public Task<ServiceResult<SysDicCurrencyDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(SysDicCurrencyDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SysDicCurrencyDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    [TestMethod]
    public async Task RefreshCurrencies_ShouldKeepCollectionInstanceAndRetainSnapshotOnFailure()
    {
        var oldCurrency = Currency("USD");
        var responses = new Queue<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>>(
        [
            ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Ok([oldCurrency]),
            ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Fail("币种接口失败"),
        ]);
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            currencyApi: new CurrencyApiStub(() => Task.FromResult(responses.Dequeue())));
        var currencies = cache.Currencies;

        Assert.IsTrue((await cache.RefreshCurrenciesAsync()).Success);
        var failed = await cache.RefreshCurrenciesAsync();

        Assert.IsFalse(failed.Success);
        Assert.AreSame(currencies, cache.Currencies);
        Assert.AreEqual(oldCurrency.Uid, cache.Currencies.Single().Uid);
    }

    private sealed class ProductColorApiStub(
        Func<Task<ServiceResult<List<ProductColorDto>>>> getAll) : IProductColorApi
    {
        public int GetAllCallCount { get; private set; }

        public Task<ServiceResult<List<ProductColorDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return getAll();
        }

        public Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductColorDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductColorDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class ProductCategoryApiStub(
        Func<Task<ServiceResult<List<ProductCategoryDto>>>> getAll) : IProductCategoryApi
    {
        public int GetAllCallCount { get; private set; }

        public Task<ServiceResult<List<ProductCategoryDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return getAll();
        }

        public Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(ProductCategorySearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(ProductCategoryBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductCategoryDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class DictTypeApiStub(
        Func<Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>>> getAll) : ISysDictTypeApi
    {
        public int GetAllCallCount { get; private set; }

        public Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return getAll();
        }

        public Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>> GetPagedAsync(SysDictTypeSearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<SysDictTypeBatchSaveResponse>> BatchSaveAsync(SysDictTypeBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync() => throw new NotSupportedException();
        public Task<ServiceResult<SysDictTypeDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(SysDictTypeDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SysDictTypeDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class DictDataApiStub(
        Func<Task<ServiceResult<IReadOnlyList<SysDictDataDto>>>> getAll) : ISysDictDataApi
    {
        public int GetAllCallCount { get; private set; }

        public Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return getAll();
        }

        public Task<ServiceResult<PagedResponseBase<SysDictDataDto>>> GetPagedAsync(SysDictDataSearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode) => throw new NotSupportedException();
        public Task<ServiceResult<SysDictDataDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(SysDictDataDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SysDictDataDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class SessionServiceStub : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;

        public void ClearSession()
        {
        }

        public void EndSession(SessionEndReason reason) =>
            SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
