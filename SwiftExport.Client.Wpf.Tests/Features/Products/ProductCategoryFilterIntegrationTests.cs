using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证产品管理页分类筛选和主表分页的真实绑定、模式切换及查询边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductCategoryFilterIntegrationTests
{
    /// <summary>列表和卡片共享可见分页栏，实际页码和页容量操作应查询正确页，切换模式不隐藏或覆盖分页。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MasterPager_ShouldRemainUsableInListAndCardModes(bool isCardView) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var api = new ProductMasterApiStub { TotalCount = 100 };
        using var model = CreateProductMasterViewModel(api, cache: new ReferenceDataCacheServiceStub());
        model.IsCardView = isCardView;
        await model.SearchCommand.ExecuteAsync(null);
        var view = new ProductMasterManagerView { DataContext = model };
        var window = new Window { Content = view, Width = 1280, Height = 760, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var pager = Descendants(view).OfType<PageNavigator>().Single();
            Assert.IsTrue(pager.IsVisible && pager.ActualHeight > 0, "当前显示模式必须为分页栏分配可见空间。");
            var secondPage = Descendants(pager).OfType<Button>().Single(button => Equals(button.Content, "2"));
            Assert.IsTrue(secondPage.IsEnabled);
            var pageButtonCenter = secondPage.TransformToAncestor(view).Transform(new Point(secondPage.ActualWidth / 2, secondPage.ActualHeight / 2));
            Assert.IsTrue(view.InputHitTest(pageButtonCenter) is DependencyObject hit && pager.IsAncestorOf(hit),
                "当前显示模式的页码必须真实可点击，不能被卡片覆盖。");
            secondPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(2, api.Requests.Last().PageIndex);
            Assert.AreEqual(2, model.PageIndex);
            var pageSize = (ComboBox)pager.FindName("CboPageSize");
            pageSize.SelectedItem = 50;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(50, api.Requests.Last().PageSize);
            Assert.AreEqual(1, api.Requests.Last().PageIndex);
            model.IsCardView = !isCardView;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(pager.IsVisible && pager.ActualHeight > 0, "模式切换后共用分页栏仍应可见。");
            var cards = Descendants(view).OfType<ListBox>().Single(list => ReferenceEquals(list.ItemsSource, model.Masters.Items));
            if (model.IsCardView)
            {
                var cardBottom = cards.TransformToAncestor(view).Transform(new Point(0, cards.ActualHeight)).Y;
                var pagerTop = pager.TransformToAncestor(view).Transform(new Point()).Y;
                Assert.IsTrue(cardBottom <= pagerTop, "卡片区域不能遮挡底部分页栏。");
            }
            SavePreview(window, model.IsCardView ? "主产品卡片模式分页.png" : "主产品列表模式分页.png");
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>两个管理页顶部多选确定后查询父分类及后代，列表使用整页宽度；关闭取消不提前查询。</summary>
    [TestMethod]
    [DataRow("ProductMasterManagerView.xaml")]
    [DataRow("ProductNeutralManagerView.xaml")]
    public void Views_ShouldApplyTopCategoryPickerAndUseFullWidthGrid(string fileName) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var rootId = Guid.NewGuid(); var childId = Guid.NewGuid();
        var cache = new ReferenceDataCacheServiceStub(productCategories:
        [new() { Uid = rootId, CategoryName = "ROOT", FakeName = "卫浴" },
         new() { Uid = childId, ParentId = rootId, CategoryName = "SHOWER", FakeName = "花洒" }]);
        cache.ProductCategories.Single(item => item.Uid == rootId).AddChild(cache.ProductCategories.Single(item => item.Uid == childId));
        var masterApi = new ProductMasterApiStub(); var neutralApi = new ProductNeutralApiStub();
        using var master = CreateProductMasterViewModel(masterApi, cache: cache);
        using var neutral = new ProductNeutralManagerViewModel(neutralApi, masterApi, new Runner(), cache,
            new Messages(), new DialogStub(), new ImageAddressResolverStub());
        var isNeutral = fileName == "ProductNeutralManagerView.xaml";
        if (isNeutral)
            neutralApi.Items.Add(new() { Uid = Guid.NewGuid(), Version = 1, CategoryId = childId, CategoryName = "SHOWER",
                Data = new() { ProductMasterId = Guid.NewGuid(), ProductCode = "N-001", ColorId = Guid.NewGuid() } });
        UserControl view = isNeutral ? new ProductNeutralManagerView { DataContext = neutral }
            : new ProductMasterManagerView { DataContext = master };
        var window = new Window { Content = view, Width = 1280, Height = 760, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var category = Descendants(view).OfType<ProductCategoryMutiSelectButton>().Single();
            var grid = (DataGrid)view.FindName(isNeutral ? "NeutralGrid" : "MasterGrid");
            Assert.IsNotNull(grid);
            Assert.IsTrue(category.TransformToAncestor(view).Transform(new Point()).Y < grid.TransformToAncestor(view).Transform(new Point()).Y);
            Assert.IsTrue(grid.ActualWidth > view.ActualWidth * 0.8, "移除左侧筛选后主列表应使用整页宽度。");
            Assert.IsEmpty(Descendants(view).OfType<ProductCategoryFilterTreeView>().ToArray());
            Assert.IsEmpty(Descendants(view).OfType<CheckBox>().Where(check => Equals(check.Content, "未分类")).ToArray(),
                "未分类只能放在分类弹层内，不再占用顶部独立位置。");
            var picker = (MultiSelectLookupButton)category.FindName("CategoryPicker");
            var popup = (Popup)picker.FindName("PART_Popup");
            popup.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.Loaded);
            var tree = (TreeView)picker.FindName("PART_TreeView"); tree.UpdateLayout();
            CategoryCheck(tree, "未分类").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            popup.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsEmpty(isNeutral ? neutralApi.Requests.Cast<object>().ToArray() : masterApi.Requests.Cast<object>().ToArray());
            popup.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.Loaded); tree.UpdateLayout();
            Assert.AreEqual(false, CategoryCheck(tree, "未分类").IsChecked);
            CategoryCheck(tree, "卫浴").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            CategoryCheck(tree, "未分类").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Descendants(popup.Child).OfType<Button>().Single(button => Equals(button.Content, "确定"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var ids = isNeutral ? neutralApi.Requests.Single().CategoryIds : masterApi.Requests.Single().CategoryIds;
            CollectionAssert.AreEquivalent(new[] { rootId, childId }, ids);
            Assert.IsTrue(isNeutral ? neutralApi.Requests.Single().IncludeUncategorized : masterApi.Requests.Single().IncludeUncategorized);
            Assert.AreEqual("分类(2)", picker.DisplayText);
            Assert.IsFalse(popup.IsOpen);
            if (isNeutral)
            {
                var column = grid.Columns.OfType<DataGridTextColumn>().Single(item => Equals(item.Header, "产品分类"));
                grid.ScrollIntoView(neutral.Lines.Items.Single(), column); grid.UpdateLayout();
                Assert.AreEqual("花洒", ((TextBlock)column.GetCellContent(neutral.Lines.Items.Single())).Text,
                    "真实分类列应显示缓存中文名称。");
            }
            SavePreview(window, isNeutral ? "中性产品顶部分类筛选.png" : "主产品顶部分类筛选.png");
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>通过弹层选项的可访问名称执行分类勾选，不依赖内部索引。</summary>
    private static CheckBox CategoryCheck(TreeView tree, string name) => Descendants(tree).OfType<CheckBox>()
        .Single(check => AutomationProperties.GetName(check) == name);

    /// <summary>仅测试遍历实际控件以操作弹层，产品代码不缓存视觉容器。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>保存真实测试页面渲染，供检查顶部筛选和表格宽度，不读取其他应用屏幕。</summary>
    private static void SavePreview(FrameworkElement view, string fileName)
    {
        var directory = Path.Combine(FindRepositoryRoot(), ".tmp", "product-category-toolbar");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, fileName)); encoder.Save(output);
    }

    /// <summary>主型号默认不限制分类，组合筛选只发送有效且去重后的分类标识。</summary>
    [TestMethod]
    public async Task ProductMaster_ShouldSendAllAndCombinedCategoryFilters()
    {
        var api = new ProductMasterApiStub();
        using var viewModel = CreateProductMasterViewModel(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        var allRequest = api.Requests.Single();
        Assert.AreEqual(0, allRequest.CategoryIds.Count);
        Assert.IsFalse(allRequest.IncludeUncategorized);

        var firstCategoryId = Guid.NewGuid();
        var secondCategoryId = Guid.NewGuid();
        var selection = new ProductCategoryFilterSelection(
            false,
            true,
            [firstCategoryId, Guid.Empty, firstCategoryId, secondCategoryId]);
        viewModel.CategoryFilter = selection;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(selection);

        Assert.AreEqual(2, api.Requests.Count);
        var combinedRequest = api.Requests[1];
        CollectionAssert.AreEqual(
            new[] { firstCategoryId, secondCategoryId },
            combinedRequest.CategoryIds);
        Assert.IsTrue(combinedRequest.IncludeUncategorized);
        Assert.AreEqual(1, combinedRequest.PageIndex);
    }

    /// <summary>未确认放弃主型号编辑时恢复已应用选择，且不发出新的分页请求。</summary>
    [TestMethod]
    public async Task ProductMaster_ShouldRestoreAppliedFilterWhenDiscardIsCancelled()
    {
        var api = new ProductMasterApiStub();
        var dialogs = new DialogStub { Result = false };
        using var viewModel = CreateProductMasterViewModel(api, dialogs);
        viewModel.Masters.LoadFromDtos([new ProductMasterListItemDto
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            Data = new ProductMasterWriteDto { MasterSku = "MASTER-1" }
        }]);
        viewModel.Masters.Items[0].ProductSeriesId = Guid.NewGuid();
        var requested = new ProductCategoryFilterSelection(false, false, [Guid.NewGuid()]);
        viewModel.CategoryFilter = requested;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(requested);

        Assert.AreSame(ProductCategoryFilterSelection.AllCategories, viewModel.CategoryFilter);
        Assert.AreEqual(0, api.Requests.Count);
        Assert.AreEqual(1, dialogs.Count);
    }

    /// <summary>中性产品分别传递“仅未分类”和“未分类加真实分类”的筛选条件。</summary>
    [TestMethod]
    public async Task ProductNeutral_ShouldSendUncategorizedAndCombinedCategoryFilters()
    {
        var api = new ProductNeutralApiStub();
        using var viewModel = new ProductNeutralManagerViewModel(
            api,
            null!,
            new Runner(),
            null!,
            new Messages(),
            new DialogStub(),
            new ImageAddressResolverStub());
        var uncategorized = new ProductCategoryFilterSelection(false, true, []);
        viewModel.CategoryFilter = uncategorized;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(uncategorized);

        Assert.AreEqual(1, api.Requests.Count);
        Assert.AreEqual(0, api.Requests[0].CategoryIds.Count);
        Assert.IsTrue(api.Requests[0].IncludeUncategorized);

        var categoryIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var combined = new ProductCategoryFilterSelection(false, true, categoryIds);
        viewModel.CategoryFilter = combined;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(combined);

        Assert.AreEqual(2, api.Requests.Count);
        CollectionAssert.AreEqual(categoryIds, api.Requests[1].CategoryIds);
        Assert.IsTrue(api.Requests[1].IncludeUncategorized);
    }

    private static ProductMasterManagerViewModel CreateProductMasterViewModel(
        IProductMasterApi api,
        IDialogService? dialogs = null,
        IReferenceDataCacheService? cache = null) => new(
        api,
        new Runner(),
        cache!,
        new Messages(),
        dialogs ?? new DialogStub(),
        null!,
        null!,
        new ImageAddressResolverStub(),
        null!);

    /// <summary>从测试运行目录向上定位仓库根目录。</summary>
    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    /// <summary>记录主型号分页请求，并返回稳定空页。</summary>
    private sealed class ProductMasterApiStub : IProductMasterApi
    {
        public Task<ServiceResult<BatchSetProductMasterMainImagesResponse>> SetMainImagesAsync(BatchSetProductMasterMainImagesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<ProductMasterSearchRequest> Requests { get; } = [];
        public int TotalCount { get; set; }

        public Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(
            ProductMasterSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new()
            { TotalCount = TotalCount, PageIndex = request.PageIndex, PageSize = request.PageSize }));
        }

        public Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(
            AssignProductMastersToCategoryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应执行主型号归类。");

        public Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应读取主型号详情。");

        public Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应保存主型号。");

        public Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应保存主型号详情。");
    }

    /// <summary>记录中性产品分页请求，并返回稳定空页。</summary>
    private sealed class ProductNeutralApiStub : IProductNeutralManagerApi
    {
        /// <inheritdoc />
        public Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import.ProductNeutralImportRowDto> rows,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("分类筛选不应提交导入。");
        public List<ProductNeutralManagerSearchRequest> Requests { get; } = [];
        public List<ProductNeutralManagerListItemDto> Items { get; } = [];

        public Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
            ProductNeutralManagerSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>.Ok(new()
            { Items = Items, TotalCount = Items.Count, PageIndex = request.PageIndex, PageSize = request.PageSize }));
        }

        public Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
            ProductNeutralManagerBatchSaveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应保存中性产品。");
    }

    /// <summary>直接执行 API 委托，避免测试引入网络与 UI 错误转换。</summary>
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    /// <summary>记录未保存变更确认结果。</summary>
    private sealed class DialogStub : IDialogService
    {
        public bool Result { get; set; } = true;
        public int Count { get; private set; }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.FromResult(Result);
        }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>隔离消息提示，测试只断言状态和请求。</summary>
    private sealed class Messages : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
