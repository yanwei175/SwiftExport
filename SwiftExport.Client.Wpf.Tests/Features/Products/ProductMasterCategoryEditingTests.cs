using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证管理页文本绑定隔离及分类提交、清空与回滚命令；不覆盖 Popup 焦点路由。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterCategoryEditingTests
{
    /// <summary>详情树的临时选择不直接改头部，确认及清空命令同步写入可保存字段。</summary>
    [TestMethod]
    public void DetailCategory_ShouldCommitAndClearWithoutAcceptingOtherChanges()
    {
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, null!, null!, null!);
        var originalId = Guid.NewGuid();
        vm.Header.Load(new ProductMasterDetailDto
        {
            Uid = Guid.NewGuid(), CategoryName = "原分类",
            Master = new ProductMasterWriteDto { MasterSku = "MASTER-TREE", CategoryId = originalId }
        });
        // 缓存节点构造器为内部成员，固定测试数据无需调用共享缓存 API。
        var category = (ProductCategoryCacheNode)Activator.CreateInstance(typeof(ProductCategoryCacheNode),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { new ProductCategoryDto { Uid = Guid.NewGuid(), CategoryName = "子分类" } }, null)!;
        var unsavedSeriesId = Guid.NewGuid();
        vm.Header.ProductSeriesId = unsavedSeriesId;
        vm.SelectedCategory = category;
        Assert.AreEqual(originalId, vm.Header.CategoryId);

        vm.CommitCategoryCommand.Execute(null);
        Assert.AreEqual(category.Uid, vm.Header.ToWriteDto().CategoryId);
        Assert.AreEqual("子分类", vm.Header.CategoryName);
        vm.ClearCategoryCommand.Execute(null);
        Assert.AreEqual(Guid.Empty, vm.Header.ToWriteDto().CategoryId);
        Assert.AreEqual(string.Empty, vm.Header.CategoryName);
        Assert.IsNull(vm.SelectedCategory);
        Assert.AreEqual(unsavedSeriesId, vm.Header.ProductSeriesId);
        Assert.IsTrue(vm.HasUnsavedChanges);
        vm.Header.RejectChanges();
        Assert.AreEqual(originalId, vm.Header.CategoryId);
        Assert.AreEqual("原分类", vm.Header.CategoryName);
    }

    /// <summary>搜索不污染业务名称，提交与清空均同步标识、名称和保存变更集。</summary>
    [TestMethod]
    public void CategoryEditor_ShouldKeepSearchSeparateAndCommitOrClearRow()
    {
        StaTest.RunAsync(async () =>
        {
            // 本场景只执行本地编辑命令，不初始化页面、不访问 API 或弹窗服务。
            using var vm = new ProductMasterManagerViewModel(null!, null!, null!, null!, null!, null!, null!, new ImageAddressResolverStub());
            var originalId = Guid.NewGuid();
            vm.Masters.LoadFromDtos([new ProductMasterListItemDto
            {
                Uid = Guid.NewGuid(), Version = 1, CategoryName = "原分类",
                Data = new ProductMasterWriteDto { MasterSku = "MASTER-LOOKUP", CategoryId = originalId }
            }]);
            var row = vm.Masters.Items.Single();
            // 缓存节点只允许缓存服务创建；测试构造固定节点，不访问远程基础资料。
            var category = (ProductCategoryCacheNode)Activator.CreateInstance(typeof(ProductCategoryCacheNode),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { new ProductCategoryDto { Uid = Guid.NewGuid(), CategoryName = "英文分类", FakeName = "新分类" } }, null)!;
            // 用原生文本宿主执行页面声明的 Text 绑定；不把此检查作为 Popup 交互证据。
            var lookup = LoadCategoryTextBindingHost();
            lookup.DataContext = row;
            await Dispatcher.Yield(DispatcherPriority.Background);
            lookup.SetCurrentValue(TextBox.TextProperty, "新");
            lookup.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.AreEqual("原分类", row.CategoryName, "搜索词不得覆盖分类名称。");
            Assert.AreEqual(originalId, row.CategoryId);
            Assert.AreEqual(LineState.Unchanged, row.State);

            vm.EditCategory = category;
            vm.CommitCategoryCommand.Execute(row);
            Assert.AreEqual(category.Uid, row.CategoryId);
            Assert.AreEqual("新分类", row.CategoryName);
            Assert.AreEqual(category.Uid, vm.Masters.CreateSaveRequest().UpdateItems.Single().Data.CategoryId);
            Assert.IsNull(vm.EditCategory);

            vm.EditCategory = category;
            vm.ClearCategoryCommand.Execute(row);
            Assert.AreEqual(Guid.Empty, row.CategoryId);
            Assert.AreEqual(string.Empty, row.CategoryName);
            Assert.AreEqual(Guid.Empty, vm.Masters.CreateSaveRequest().UpdateItems.Single().Data.CategoryId);
            Assert.IsNull(vm.EditCategory);

            row.RejectChanges();
            Assert.AreEqual(originalId, row.CategoryId);
            Assert.AreEqual("原分类", row.CategoryName);
            Assert.AreEqual(LineState.Unchanged, row.State);
        });
    }

    /// <summary>分页英文分类名由缓存中文名显示，缓存更新仅接纳无草稿行的展示基线，中文名缺失回退原名。</summary>
    [TestMethod]
    public async Task ManagerCategoryCache_ShouldProjectChineseNameAndProtectDirtyRowBaseline()
    {
        var categoryId = Guid.NewGuid();
        var fallbackId = Guid.NewGuid();
        var cache = new ReferenceDataCacheServiceStub(productCategories:
        [new ProductCategoryDto { Uid = categoryId, CategoryName = "English Category", FakeName = "中文分类" },
            new ProductCategoryDto { Uid = fallbackId, CategoryName = "Fallback Category", FakeName = string.Empty }]);
        var pageReads = 0;
        var api = Proxy<IProductMasterApi>((method, _) =>
        {
            Assert.AreEqual(nameof(IProductMasterApi.GetPagedAsync), method.Name);
            pageReads++;
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new()
            {
                Items = [new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, CategoryName = "English Category",
                        Data = new() { MasterSku = "A", CategoryId = categoryId } },
                    new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, CategoryName = "English Category",
                        Data = new() { MasterSku = "B", CategoryId = categoryId } },
                    new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, CategoryName = "Fallback Category",
                        Data = new() { MasterSku = "C", CategoryId = fallbackId } }],
                TotalCount = 3, PageIndex = 1, PageSize = 20
            }));
        });
        using var vm = new ProductMasterManagerViewModel(api,
            Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()), cache,
            Proxy<IMessageService>((_, _) => null), null!, null!, null!, new ImageAddressResolverStub())
        { IsCardView = true };
        await vm.InitializeAsync();
        Assert.AreEqual(1, pageReads);
        var clean = vm.Masters.Items[0];
        var dirty = vm.Masters.Items[1];
        var fallback = vm.Masters.Items[2];
        Assert.AreEqual("中文分类", clean.CategoryName);
        Assert.AreEqual("中文分类", dirty.CategoryName);
        Assert.AreEqual("Fallback Category", fallback.CategoryName);
        Assert.IsFalse(vm.Masters.HasChanges, "分页显示名称转换不得产生保存变更。");
        dirty.DefaultDescriptionZh = "未保存描述";
        cache.SetProductCategories(
        [new ProductCategoryDto { Uid = categoryId, CategoryName = "English Category", FakeName = "分类中文更名" },
            new ProductCategoryDto { Uid = fallbackId, CategoryName = "Fallback Category", FakeName = " " }]);
        Assert.AreEqual("分类中文更名", clean.CategoryName);
        Assert.IsFalse(clean.IsDirty);
        Assert.AreEqual("中文分类", dirty.CategoryName);
        Assert.AreEqual("未保存描述", dirty.DefaultDescriptionZh);
        Assert.IsTrue(dirty.IsDirty);
        Assert.AreEqual("Fallback Category", fallback.CategoryName);
        Assert.IsFalse(fallback.IsDirty);
        dirty.RejectChanges();
        Assert.AreEqual("中文分类", dirty.CategoryName, "缓存更名不能改写正在编辑行的回滚基线。");
        Assert.IsFalse(dirty.IsDirty);
    }

    /// <summary>只提供本次分页和基础资料的离线接口调用，不访问真实网络。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    /// <summary>从真实页面提取文本绑定并在原生文本宿主中执行，避免复制绑定导致测试脱节。</summary>
    private static TextBox LoadCategoryTextBindingHost()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "SwiftExport.Client.Wpf", "Features",
            "Products", "ProductList", "Views", "ProductMasterManagerView.xaml"));
        var element = document.Descendants().Single(node => node.Name.LocalName == "ProductCategoryFlatLookupTextBox");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var host = new XElement(presentation + "TextBox",
            new XAttribute("xmlns", presentation.NamespaceName),
            new XAttribute("Text", element.Attribute("Text")!.Value));
        return (TextBox)XamlReader.Parse(host.ToString());
    }
}
