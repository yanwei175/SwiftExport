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
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Client.Wpf.Tests.Controls;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>验证会话基础资料缓存的完整快照、请求合并、失败保留与会话隔离。</summary>
[TestClass]
public sealed class ReferenceDataCacheServiceTests
{
    /// <summary>并行首次使用合并标签请求，完整发布后所有读者获得同一组及其全部选项。</summary>
    [TestMethod]
    public async Task ProductAttributes_FirstEnsure_ShouldCoalesceAndPublishCompleteSnapshot()
    {
        var gate = new TaskCompletionSource<ServiceResult<ProductAttributeDefinitionsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new AttributeApiStub(_ => gate.Task);
        using var cache = CreateCache(new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()), attributeApi: api);
        var group = AttributeGroup(Guid.NewGuid(), "安装方式", "吸顶", "挂墙");
        var published = 0;
        cache.ProductAttributesChanged += (_, _) =>
        {
            published++;
            Assert.IsTrue(cache.AreProductAttributesLoaded);
            Assert.HasCount(2, cache.GetProductAttributeOptions(group.Uid));
        };

        var first = cache.EnsureProductAttributesLoadedAsync();
        var second = cache.EnsureProductAttributesLoadedAsync();
        gate.SetResult(AttributeSnapshot(group));
        Assert.IsTrue((await first).Success);
        Assert.IsTrue((await second).Success);
        Assert.IsTrue((await cache.EnsureProductAttributesLoadedAsync()).Success);

        Assert.AreEqual(1, api.GetDefinitionsCallCount);
        Assert.AreEqual(1, published);
        Assert.AreEqual(group.Uid, cache.GetProductAttributeGroups(group.CategoryId).Single().Uid);
        Assert.IsEmpty(cache.GetProductAttributeGroups(Guid.NewGuid()));
        Assert.IsEmpty(cache.GetProductAttributeOptions(Guid.NewGuid()));
    }

    /// <summary>标签请求失败不阻断其他基础资料首载，后续 Ensure 只重试失败的标签。</summary>
    [TestMethod]
    public async Task ProductAttributes_InitialFailure_ShouldKeepOtherReferenceDataUsableAndRetry()
    {
        var responses = new Queue<ServiceResult<ProductAttributeDefinitionsDto>>([
            ServiceResult<ProductAttributeDefinitionsDto>.Fail("标签服务暂不可用"), AttributeSnapshot()]);
        var api = new AttributeApiStub(_ => Task.FromResult(responses.Dequeue()));
        var colors = new ProductColorApiStub(() => SuccessColors(Color("RED")));
        using var cache = CreateCache(colors, new ProductCategoryApiStub(() => SuccessCategories()), attributeApi: api);

        Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);
        Assert.HasCount(1, cache.ProductColors);
        Assert.IsFalse(cache.AreProductAttributesLoaded);
        Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);
        Assert.IsTrue(cache.AreProductAttributesLoaded, "成功的空快照必须区别于未加载或失败。");
        Assert.IsEmpty(cache.ProductAttributeGroups);
        Assert.AreEqual(2, api.GetDefinitionsCallCount);
        Assert.AreEqual(1, colors.GetAllCallCount);
    }

    /// <summary>刷新失败保留旧组与其选项，成功刷新一次性替换两者且集合身份稳定。</summary>
    [TestMethod]
    public async Task ProductAttributes_Refresh_ShouldRetainSnapshotOnFailureAndReplaceOnceOnSuccess()
    {
        var original = AttributeGroup(Guid.NewGuid(), "安装方式", "吸顶", "挂墙");
        var replacement = AttributeGroup(original.CategoryId, "尺寸", "8寸", "10寸");
        var responses = new Queue<ServiceResult<ProductAttributeDefinitionsDto>>([
            AttributeSnapshot(original), ServiceResult<ProductAttributeDefinitionsDto>.Fail("请求失败"), AttributeSnapshot(replacement)]);
        using var cache = CreateCache(new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()), attributeApi: new AttributeApiStub(_ => Task.FromResult(responses.Dequeue())));
        var collection = cache.ProductAttributeGroups;
        var resets = 0;
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        Assert.IsTrue((await cache.RefreshProductAttributesAsync()).Success);
        var oldGroup = cache.ProductAttributeGroups.Single();
        Assert.IsFalse((await cache.RefreshProductAttributesAsync()).Success);
        Assert.AreSame(oldGroup, cache.ProductAttributeGroups.Single());
        Assert.HasCount(2, cache.GetProductAttributeOptions(original.Uid));
        Assert.IsTrue(cache.AreProductAttributesLoaded);
        Assert.IsTrue((await cache.RefreshProductAttributesAsync()).Success);

        Assert.AreSame(collection, cache.ProductAttributeGroups);
        Assert.AreEqual(2, resets);
        Assert.AreEqual(replacement.Uid, collection.Single().Uid);
        Assert.IsEmpty(cache.GetProductAttributeOptions(original.Uid));
        CollectionAssert.AreEquivalent(new[] { "8寸", "10寸" }, cache.GetProductAttributeOptions(replacement.Uid).Select(option => option.OptionName).ToArray());
    }

    /// <summary>响应缺少整个快照不能伪装为空成功，也不能清空已有候选。</summary>
    [TestMethod]
    public async Task ProductAttributes_MissingSuccessfulPayload_ShouldRetainSnapshot()
    {
        var group = AttributeGroup(Guid.NewGuid(), "安装方式", "吸顶");
        var responses = new Queue<ServiceResult<ProductAttributeDefinitionsDto>>([
            AttributeSnapshot(group), ServiceResult<ProductAttributeDefinitionsDto>.Ok(null!)]);
        using var cache = CreateCache(new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()), attributeApi: new AttributeApiStub(_ => Task.FromResult(responses.Dequeue())));
        Assert.IsTrue((await cache.RefreshProductAttributesAsync()).Success);

        Assert.IsFalse((await cache.RefreshProductAttributesAsync()).Success);
        Assert.AreEqual(group.Uid, cache.ProductAttributeGroups.Single().Uid);
        Assert.IsTrue(cache.AreProductAttributesLoaded);
    }

    /// <summary>登出取消标签请求，忽略不遵守取消的晚到响应，新会话仍能重新加载。</summary>
    [TestMethod]
    public async Task ProductAttributes_SessionChange_ShouldCancelAndIgnoreLateResponse()
    {
        var gate = new TaskCompletionSource<ServiceResult<ProductAttributeDefinitionsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestToken = CancellationToken.None;
        var api = new AttributeApiStub(token =>
        {
            requestToken = token;
            return gate.Task;
        });
        var session = new SessionServiceStub();
        using var cache = CreateCache(new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()), session: session, attributeApi: api);
        var loading = cache.EnsureProductAttributesLoadedAsync();

        session.EndSession(SessionEndReason.Logout);
        Assert.IsTrue(requestToken.IsCancellationRequested);
        Assert.IsFalse(cache.AreProductAttributesLoaded);
        gate.SetResult(AttributeSnapshot(AttributeGroup(Guid.NewGuid(), "旧会话标签", "旧值")));
        Assert.IsFalse((await loading).Success);
        Assert.IsEmpty(cache.ProductAttributeGroups);

        Assert.IsTrue((await cache.EnsureProductAttributesLoadedAsync()).Success);
        Assert.IsTrue(cache.AreProductAttributesLoaded);
        Assert.AreEqual(2, api.GetDefinitionsCallCount);
    }

    /// <summary>手动刷新和整合首载共享候选定义，缓存副本不因原始响应被修改而失真。</summary>
    [TestMethod]
    public async Task ProductAttributes_Snapshot_ShouldSortAndDetachResponseObjects()
    {
        var group = AttributeGroup(Guid.NewGuid(), "安装方式", "挂墙", "吸顶");
        group.Options[0].SortOrder = 20;
        group.Options[1].SortOrder = 10;
        var later = AttributeGroup(group.CategoryId, "尺寸", "8寸");
        group.SortOrder = 20;
        later.SortOrder = 10;
        using var cache = CreateCache(new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()), attributeApi: new AttributeApiStub(_ => Task.FromResult(AttributeSnapshot(group, later))));
        Assert.IsTrue((await cache.EnsureLoadedAsync()).Success);

        CollectionAssert.AreEqual(new[] { later.Uid, group.Uid }, cache.GetProductAttributeGroups(group.CategoryId).Select(item => item.Uid).ToArray());
        CollectionAssert.AreEqual(new[] { "吸顶", "挂墙" }, cache.GetProductAttributeOptions(group.Uid).Select(item => item.OptionName).ToArray());
        group.GroupName = "被修改";
        group.Options[0].OptionName = "被修改";
        Assert.AreEqual("安装方式", cache.ProductAttributeGroups.Single(item => item.Uid == group.Uid).GroupName);
        CollectionAssert.AreEqual(new[] { "吸顶", "挂墙" }, cache.GetProductAttributeOptions(group.Uid).Select(item => item.OptionName).ToArray());
    }
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
    public async Task RefreshCustomersAndSuppliers_ShouldLoadEveryPageAndKeepCollectionInstances()
    {
        var firstCustomer = Customer("C002");
        var secondCustomer = Customer("C001");
        var firstSupplier = Supplier("S002");
        var secondSupplier = Supplier("S001");
        var customerApi = new CustomerApiStub(request => Task.FromResult(Page<CustomerDto>(
            request,
            request.PageIndex == 1 ? [firstCustomer] : [secondCustomer],
            2)));
        var supplierApi = new SupplierApiStub(request => Task.FromResult(Page<SupplierDto>(
            request,
            request.PageIndex == 1 ? [firstSupplier] : [secondSupplier],
            2)));
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            customerApi: customerApi,
            supplierApi: supplierApi);
        var customers = cache.Customers;
        var suppliers = cache.Suppliers;

        var customerResult = await cache.RefreshCustomersAsync();
        var supplierResult = await cache.RefreshSuppliersAsync();

        Assert.IsTrue(customerResult.Success);
        Assert.IsTrue(supplierResult.Success);
        Assert.AreSame(customers, cache.Customers);
        Assert.AreSame(suppliers, cache.Suppliers);
        CollectionAssert.AreEqual(new[] { "C001", "C002" }, cache.Customers.Select(item => item.CustomerCode).ToArray());
        CollectionAssert.AreEqual(new[] { "S001", "S002" }, cache.Suppliers.Select(item => item.SupplierCode).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, customerApi.RequestedPages.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, supplierApi.RequestedPages.ToArray());
        Assert.IsTrue(customerApi.RequestedPageSizes.All(size => size == 200));
        Assert.IsTrue(supplierApi.RequestedPageSizes.All(size => size == 200));
    }

    [TestMethod]
    public async Task RefreshCustomers_WhenLaterPageFails_ShouldRetainPreviousCompleteSnapshot()
    {
        var oldCustomer = Customer("OLD");
        var useFailureSequence = false;
        var customerApi = new CustomerApiStub(request =>
        {
            if (!useFailureSequence)
            {
                return Task.FromResult(Page(request, [oldCustomer], 1));
            }

            return Task.FromResult(request.PageIndex == 1
                ? Page(request, [Customer("NEW")], 2)
                : ServiceResult<PagedResponseBase<CustomerDto>>.Fail("客户第二页失败"));
        });
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            customerApi: customerApi);

        Assert.IsTrue((await cache.RefreshCustomersAsync()).Success);
        useFailureSequence = true;
        var failed = await cache.RefreshCustomersAsync();

        Assert.IsFalse(failed.Success);
        Assert.AreEqual(oldCustomer.Uid, cache.Customers.Single().Uid);
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
        Assert.AreEqual(0, cache.Customers.Count);
        Assert.AreEqual(0, cache.Suppliers.Count);
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
        Assert.AreEqual(0, cache.Customers.Count);
        Assert.AreEqual(0, cache.Suppliers.Count);
        Assert.AreEqual(0, cache.ProductCategoryTree.Count);
        Assert.AreEqual(0, cache.ProductCategories.Count);
    }

    /// <summary>仓库缓存必须逐页完整读取，并在全部成功后一次性按代码发布。</summary>
    [TestMethod]
    public async Task EnsureWarehouseSites_ShouldLoadEveryPageAndPublishCompleteSnapshot()
    {
        var warehouseApi = new WarehouseApiStub(request => Task.FromResult(
            request.PageIndex == 1
                ? Page<WarehouseSitePickerDto>(request, [Warehouse("WH-B"), Warehouse("WH-A")], 3)
                : Page<WarehouseSitePickerDto>(request, [Warehouse("WH-C")], 3)));
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            warehouseApi: warehouseApi);

        var result = await cache.EnsureWarehouseSitesLoadedAsync();

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { 1, 2 }, warehouseApi.RequestedPages);
        CollectionAssert.AreEqual(new[] { "WH-A", "WH-B", "WH-C" }, cache.WarehouseSites.Select(item => item.Code).ToArray());
    }

    /// <summary>仓库后续分页失败时不得发布残缺数据或覆盖上一次完整快照。</summary>
    [TestMethod]
    public async Task RefreshWarehouseSites_WhenLaterPageFails_ShouldRetainOldSnapshot()
    {
        var useFailure = false;
        var oldWarehouse = Warehouse("WH-OLD");
        var warehouseApi = new WarehouseApiStub(request =>
        {
            if (!useFailure) return Task.FromResult(Page<WarehouseSitePickerDto>(request, [oldWarehouse], 1));
            return Task.FromResult(request.PageIndex == 1
                ? Page<WarehouseSitePickerDto>(request, [Warehouse("WH-NEW")], 2)
                : ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Fail("仓库第二页失败"));
        });
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            warehouseApi: warehouseApi);
        Assert.IsTrue((await cache.RefreshWarehouseSitesAsync()).Success);

        useFailure = true;
        var failed = await cache.RefreshWarehouseSitesAsync();

        Assert.IsFalse(failed.Success);
        Assert.AreSame(oldWarehouse, cache.WarehouseSites.Single());
    }

    /// <summary>退出登录清空仓库快照，旧会话中的迟到响应也不能重新发布。</summary>
    [TestMethod]
    public async Task WarehouseSessionEnded_DuringLoad_ShouldClearAndRejectLateResponse()
    {
        var session = new SessionServiceStub();
        var gate = new TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var warehouseApi = new WarehouseApiStub(_ => gate.Task);
        using var cache = CreateCache(
            new ProductColorApiStub(() => SuccessColors()),
            new ProductCategoryApiStub(() => SuccessCategories()),
            session,
            warehouseApi: warehouseApi);
        var loading = cache.EnsureWarehouseSitesLoadedAsync();

        session.EndSession(SessionEndReason.Logout);
        var request = new PagedRequestBase { PageIndex = 1, PageSize = 200 };
        gate.SetResult(Page<WarehouseSitePickerDto>(request, [Warehouse("WH-LATE")], 1));
        var result = await loading;

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, cache.WarehouseSites.Count);
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

    /// <summary>在统一的应用 Dispatcher 上验证真实输入焦点、本地筛选及弹出层内部刷新焦点。</summary>
    [TestMethod]
    [DoNotParallelize]
    public void ProductCategoryLookup_ShouldFilterTreeAndKeepPopupOpenForRefreshFocus()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            // 复用实际主题资源和长存 STA，避免其他控件测试创建 Application 后本用例仍落在另一条 UI 线程。
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
            foreach (var resource in new[] { "Variables.xaml", "Typography.xaml", "ListBox.xaml" })
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/{resource}") });
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
                await Dispatcher.Yield(DispatcherPriority.Input);
                await WaitForLookupStateAsync(() => window.IsActive, "分类 Lookup 测试窗口未激活。");
                Assert.IsTrue(input.Focus(), "输入框必须成功取得实际键盘焦点。");
                Assert.AreSame(input, Keyboard.Focus(input));
                await WaitForLookupStateAsync(() => input.IsKeyboardFocusWithin, "输入框没有获得键盘焦点，不能模拟用户输入。");
                input.Text = "CHILD";
                await WaitForLookupStateAsync(() => editor.IsDropDownOpen, "本地搜索完成后应打开分类弹出层。");
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
                Assert.IsTrue(refreshButton.Focus());
                Assert.AreSame(refreshButton, Keyboard.Focus(refreshButton));
                await WaitForLookupStateAsync(() => refreshButton.IsKeyboardFocusWithin, "弹出层刷新按钮必须取得键盘焦点。");
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

    /// <summary>等待可观察焦点或搜索结果，不用固定延时假设 Dispatcher 和防抖已完成。</summary>
    private static async Task WaitForLookupStateAsync(Func<bool> completed, string failureMessage)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!completed() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        Assert.IsTrue(completed(), failureMessage);
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
        ISysDictDataApi? dictDataApi = null,
        ICustomerApi? customerApi = null,
        ISupplierApi? supplierApi = null,
        IWarehouseSiteApi? warehouseApi = null,
        IProductAttributeApi? attributeApi = null) =>
        new(
            customerApi ?? new CustomerApiStub(request => Task.FromResult(Page<CustomerDto>(request, [], 0))),
            supplierApi ?? new SupplierApiStub(request => Task.FromResult(Page<SupplierDto>(request, [], 0))),
            warehouseApi ?? new WarehouseApiStub(request => Task.FromResult(Page<WarehouseSitePickerDto>(request, [], 0))),
            colorApi,
            categoryApi,
            attributeApi ?? new AttributeApiStub(_ => Task.FromResult(AttributeSnapshot())),
            currencyApi ?? new CurrencyApiStub(() => SuccessCurrencies()),
            dictTypeApi ?? new DictTypeApiStub(() => SuccessDictionaryTypes()),
            dictDataApi ?? new DictDataApiStub(() => SuccessDictionaryValues()),
            new DirectApiCallRunner(),
            session ?? new SessionServiceStub());

    /// <summary>构造只有定义的候选资料，不携带产品选择状态。</summary>
    private static ProductAttributeGroupDto AttributeGroup(Guid categoryId, string name, params string[] optionNames)
    {
        var group = new ProductAttributeGroupDto { Uid = Guid.NewGuid(), CategoryId = categoryId, GroupName = name, GroupCode = name, Version = 1 };
        group.Options = optionNames.Select(optionName => new ProductAttributeOptionDto
        {
            Uid = Guid.NewGuid(), AttributeGroupId = group.Uid, OptionName = optionName, OptionCode = optionName, Version = 1
        }).ToList();
        return group;
    }

    private static ServiceResult<ProductAttributeDefinitionsDto> AttributeSnapshot(params ProductAttributeGroupDto[] groups) =>
        ServiceResult<ProductAttributeDefinitionsDto>.Ok(new ProductAttributeDefinitionsDto { Groups = groups.ToList() });

    /// <summary>控制定义读取、取消信号及请求合并行为的离线 API。</summary>
    private sealed class AttributeApiStub(Func<CancellationToken, Task<ServiceResult<ProductAttributeDefinitionsDto>>> getDefinitions) : IProductAttributeApi
    {
        public int GetDefinitionsCallCount { get; private set; }
        /// <inheritdoc />
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        {
            GetDefinitionsCallCount++;
            return getDefinitions(cancellationToken);
        }
        /// <inheritdoc />
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetCategoryDefinitionsAsync(Guid categoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ProductColorDto Color(string code) => new()
    {
        Uid = Guid.NewGuid(),
        ColorCode = code,
        ColorCodeUnique = code,
        ColorNameEn = code,
        ColorNameZh = code
    };

    private static CustomerDto Customer(string code) => new()
    {
        Uid = Guid.NewGuid(),
        CustomerCode = code,
        CustomerName = code,
    };

    private static SupplierDto Supplier(string code) => new()
    {
        Uid = Guid.NewGuid(),
        SupplierCode = code,
        SupplierName = code,
    };

    private static WarehouseSitePickerDto Warehouse(string code) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, Code = code, Name = code,
        SupplierId = Guid.NewGuid(), SupplierCode = "S-01", SupplierName = "供应商甲", IsActive = true
    };

    private static ServiceResult<PagedResponseBase<T>> Page<T>(
        PagedRequestBase request,
        IEnumerable<T> items,
        int totalCount) =>
        ServiceResult<PagedResponseBase<T>>.Ok(new PagedResponseBase<T>
        {
            Items = items.ToList(),
            TotalCount = totalCount,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
        });

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

    private sealed class CustomerApiStub(
        Func<CustomerSearchRequest, Task<ServiceResult<PagedResponseBase<CustomerDto>>>> getPage) : ICustomerApi
    {
        public List<int> RequestedPages { get; } = [];
        public List<int> RequestedPageSizes { get; } = [];

        public Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync(CustomerSearchRequest request)
        {
            RequestedPages.Add(request.PageIndex);
            RequestedPageSizes.Add(request.PageSize);
            return getPage(request);
        }

        public Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync(CustomerBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<CustomerDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(CustomerDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, CustomerDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class SupplierApiStub(
        Func<SupplierSearchRequest, Task<ServiceResult<PagedResponseBase<SupplierDto>>>> getPage) : ISupplierApi
    {
        public List<int> RequestedPages { get; } = [];
        public List<int> RequestedPageSizes { get; } = [];

        public Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync(SupplierSearchRequest request)
        {
            RequestedPages.Add(request.PageIndex);
            RequestedPageSizes.Add(request.PageSize);
            return getPage(request);
        }

        public Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<SupplierDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(SupplierDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SupplierDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class WarehouseApiStub(
        Func<PagedRequestBase, Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>> getPage) : IWarehouseSiteApi
    {
        public List<int> RequestedPages { get; } = [];

        public Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
        {
            RequestedPages.Add(request.PageIndex);
            return getPage(request);
        }

        public Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> ActivateAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeleteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
