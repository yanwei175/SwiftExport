using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerManagement.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证客户产品加载及真实客户页当前行、开关和主题接线。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CustomerProductDrawerLoadingTests
{
    [TestMethod]
    public async Task FirstOpen_ShouldLoadSelectedCustomerWithoutProductPageInitialization()
    {
        var customerId = Guid.NewGuid();
        var api = new CustomerProductApiStub();
        var viewModel = new CustomerProductManagerViewModel(
            api,
            null!,
            new DirectApiCallRunner(),
            new MessageServiceStub(),
            new DialogServiceStub());
        var customer = new CustomerLineModel();
        customer.Load(new CustomerDto
        {
            Uid = customerId,
            Version = 1,
            CustomerCode = "C001",
            CustomerName = "客户一",
        });

        await viewModel.SelectCustomerAsync(customer);

        Assert.IsTrue(viewModel.IsOpen);
        Assert.AreSame(customer, viewModel.ActiveCustomer);
        Assert.IsNotNull(api.LastRequest);
        Assert.AreEqual(customerId, api.LastRequest.CustomerId);
        Assert.HasCount(1, viewModel.Products.Items);
        Assert.AreEqual("CUSTOM-1", viewModel.Products.Items[0].CustomerSku);
        Assert.AreEqual(1, viewModel.TotalCount);
    }

    [TestMethod]
    public async Task SelectCustomer_WhenCustomerChanges_ShouldReplaceResultsWithNewCustomer()
    {
        var firstCustomer = CreateCustomer(Guid.NewGuid(), "C001", "客户一");
        var secondCustomer = CreateCustomer(Guid.NewGuid(), "C002", "客户二");
        var api = new CustomerProductApiStub();
        var viewModel = new CustomerProductManagerViewModel(
            api,
            null!,
            new DirectApiCallRunner(),
            new MessageServiceStub(),
            new DialogServiceStub());

        await viewModel.SelectCustomerAsync(firstCustomer);
        await viewModel.SelectCustomerAsync(secondCustomer);
        await WaitForRequestCountAsync(api, 2);

        Assert.IsTrue(viewModel.IsOpen);
        Assert.AreSame(secondCustomer, viewModel.ActiveCustomer);
        Assert.HasCount(2, api.Requests);
        Assert.AreEqual(firstCustomer.Uid, api.Requests[0].CustomerId);
        Assert.AreEqual(secondCustomer.Uid, api.Requests[1].CustomerId);
        Assert.HasCount(1, viewModel.Products.Items);
        Assert.AreEqual("CUSTOM-C002", viewModel.Products.Items[0].CustomerSku);
        Assert.IsFalse(viewModel.Products.Items.Any(item => item.CustomerId == firstCustomer.Uid));
    }

    /// <summary>进入整行选择后，业务选中客户在工具栏获得焦点时仍保留；客户页选中项变化不再加载产品。</summary>
    [TestMethod]
    [DataRow(DataGridSelectionUnit.FullRow)]
    [DataRow(DataGridSelectionUnit.Cell)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader)]
    public void SelectedCustomer_ShouldSurviveToolbarFocusWithoutOpeningProducts(DataGridSelectionUnit unit) => WithPage(async (vm, view, grid, api) =>
    {
        var first = vm.Customers.Items[0];
        var second = vm.Customers.Items[1];
        grid.SetCurrentValue(DataGrid.SelectionUnitProperty, unit);
        grid.ScrollIntoView(second, grid.Columns[1]);
        grid.UpdateLayout();
        var cell = (DataGridCell)grid.Columns[1].GetCellContent(second).Parent;
        Assert.IsTrue(cell.Focus());
        await Drain();
        Assert.AreSame(second, grid.CurrentItem);
        Assert.IsEmpty(api.Requests, "选中客户本身不自动打开抽屉。");
        grid.SetCurrentValue(DataGrid.SelectionUnitProperty, DataGridSelectionUnit.FullRow);
        grid.SelectedItem = second;
        Assert.IsTrue(cell.Focus());
        await Drain();
        Assert.AreSame(second, vm.SelectedLine);
        Assert.AreSame(second, vm.Customers.SelectedItem);
        var button = Descendants(view).OfType<global::Wpf.Ui.Controls.Button>()
            .Single(item => ReferenceEquals(item.Command, vm.AddRowCommand));
        Assert.IsTrue(button.Focus());
        await Drain();
        Assert.IsNull(grid.CurrentItem);
        Assert.AreSame(second, grid.SelectedItem);
        Assert.AreSame(second, vm.SelectedLine);
        grid.SelectedItem = first;
        await Drain();
        Assert.AreSame(first, vm.SelectedLine);
        Assert.IsEmpty(api.Requests, "客户页选中项与产品工作区加载解耦。");
    });

    /// <summary>列头必须沿用 WPF-UI 的真实模板，库的筛选内容不能替换外层主题。</summary>
    [TestMethod]
    public void CustomerHeader_ShouldUseWpfUiTemplate() => WithPage((vm, view, grid, api) =>
    {
        var expected = (Style)view.FindResource("DefaultDataGridColumnHeaderStyle");
        var template = expected.Setters.OfType<Setter>().Single(setter => setter.Property == Control.TemplateProperty).Value;
        var headers = Descendants(grid).OfType<DataGridColumnHeader>().Where(item => item.Column is not null).ToArray();
        Assert.IsTrue(headers.Length > 1);
        foreach (var header in headers)
            Assert.AreSame(template, header.Template, "普通列和勾选列必须继承 WPF-UI 模板。");
        Assert.IsFalse(grid.Columns[0].CanUserResize);
        return Task.CompletedTask;
    });

    /// <summary>使用真实页面、资源和离线 API；卸载完成后才由宿主释放主题。</summary>
    private static void WithPage(Func<CustomerManagerViewModel, CustomerManagerView, DataGrid, CustomerProductApiStub, Task> test) =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var api = new CustomerProductApiStub();
            var runner = new DirectApiCallRunner();
            var messages = new MessageServiceStub();
            var dialogs = new DialogServiceStub();
            using var vm = new CustomerManagerViewModel(null!, runner, messages, dialogs,
                new CustomerDictionaryColumnsTests.ReferenceDataCacheStub([]));
            vm.Customers.LoadFromDtos([new CustomerDto { Uid = Guid.NewGuid(), Version = 1, CustomerCode = "C001", CustomerName = "客户一" },
                new CustomerDto { Uid = Guid.NewGuid(), Version = 1, CustomerCode = "C002", CustomerName = "客户二" }]);
            var view = new CustomerManagerView { DataContext = vm };
            var window = new Window { Content = view, Width = 1600, Height = 750, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.Activate();
                await Task.Delay(600);
                await Drain();
                var grid = Descendants(view).OfType<DataGrid>().Single(item => ReferenceEquals(item.ItemsSource, vm.Customers.Items));
                await test(vm, view, grid, api);
            }
            finally { window.Close(); await Drain(); }
        });

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static CustomerLineModel CreateCustomer(Guid uid, string code, string name)
    {
        var customer = new CustomerLineModel();
        customer.Load(new CustomerDto
        {
            Uid = uid,
            Version = 1,
            CustomerCode = code,
            CustomerName = name,
        });
        return customer;
    }

    private static async Task WaitForRequestCountAsync(CustomerProductApiStub api, int expectedCount)
    {
        for (var attempt = 0; attempt < 100 && api.Requests.Count < expectedCount; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.AreEqual(expectedCount, api.Requests.Count, "切换客户后没有发起新的客户产品查询。");
    }

    private sealed class CustomerProductApiStub : ICustomerProductApi
    {
        public List<CustomerProductSearchRequest> Requests { get; } = [];
        public CustomerProductSearchRequest? LastRequest => Requests.LastOrDefault();

        public Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest request)
        {
            Requests.Add(request);
            var customerCode = request.CustomerId == Requests[0].CustomerId && Requests.Count == 1
                ? "C001"
                : "C002";
            return Task.FromResult(ServiceResult<PagedResponseBase<CustomerProductDto>>.Ok(new PagedResponseBase<CustomerProductDto>
            {
                Items =
                [
                    new CustomerProductDto
                    {
                        Uid = Guid.NewGuid(),
                        Version = 1,
                        CustomerId = request.CustomerId,
                        CustomerCode = customerCode,
                        CustomerSku = Requests.Count == 1 ? "CUSTOM-1" : $"CUSTOM-{customerCode}",
                    },
                ],
                TotalCount = 1,
                PageIndex = request.PageIndex,
                PageSize = request.PageSize,
            }));
        }

        public Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid id) => NotSupported<CustomerProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(CustomerProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, CustomerProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<List<CustomerDto>>> GetAllHaveProductCustomerList() => NotSupported<List<CustomerDto>>();
        public Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(Guid customerId, string sku) => NotSupported<CustomerProductDto>();
        public Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(CustomerProductBatchSaveRequest request) => NotSupported<CustomerProductBatchSaveResponse>();

        private static Task<ServiceResult<T>> NotSupported<T>() =>
            Task.FromException<ServiceResult<T>>(new NotSupportedException());
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") =>
            ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) => Assert.Fail($"{title}: {message}");
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
