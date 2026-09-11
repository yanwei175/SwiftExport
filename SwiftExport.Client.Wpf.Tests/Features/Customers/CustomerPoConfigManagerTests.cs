using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Mvvm;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证客户采购默认配置页面的契约、列表状态、组合校验和导航接线。</summary>
[TestClass]
public sealed class CustomerPoConfigManagerTests
{
    /// <summary>分页与保存路由应匹配 Controller，分页查询应支持取消。</summary>
    [TestMethod]
    public void Api_ShouldExposeRoutesAndQueryCancellation()
    {
        AssertRoute(nameof(ICustomerPoConfigApi.GetPagedAsync), "/page");
        AssertRoute(nameof(ICustomerPoConfigApi.BatchSaveAsync), "/batch-save");
        var query = typeof(ICustomerPoConfigApi).GetMethod(nameof(ICustomerPoConfigApi.GetPagedAsync))!;
        Assert.AreEqual(typeof(CancellationToken), query.GetParameters().Last().ParameterType);
    }

    /// <summary>列表应映射全部配置字段并把编辑形成版本化更新请求。</summary>
    [TestMethod]
    public void List_ShouldMapFieldsAndCreateUpdateRequest()
    {
        var uid = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var list = new CustomerPoConfigListModel();
        list.LoadFromDtos([new CustomerPoConfigDto
        {
            Uid = uid, Version = 3, CustomerId = customerId, CustomerCode = "C001", SupplierId = supplierId, SupplierCode = "S001",
            InletHoseType = "H1", Note = "原备注", Currency = "USD",
            DefaultDiscountRate = 0.95m, DiscountRemark = "季度折扣"
        }]);

        var line = list.Items.Single();
        Assert.AreEqual(LineState.Unchanged, line.State);
        line.Note = "新备注";
        var request = list.CreateSaveRequest();

        Assert.AreEqual(LineState.Modified, line.State);
        Assert.AreEqual(uid, request.UpdateItems.Single().Uid);
        Assert.AreEqual(3, request.UpdateItems.Single().Version);
        Assert.AreEqual("新备注", request.UpdateItems.Single().Data.Note);
        Assert.AreEqual("USD", request.UpdateItems.Single().Data.Currency);
        Assert.AreEqual(0.95m, request.UpdateItems.Single().Data.DefaultDiscountRate);
        Assert.AreEqual("季度折扣", request.UpdateItems.Single().Data.DiscountRemark);
        Assert.AreEqual(customerId, request.UpdateItems.Single().Data.CustomerId);
        Assert.AreEqual(supplierId, request.UpdateItems.Single().Data.SupplierId);
    }

    /// <summary>新增行默认使用 CNY，历史空币种加载后仍保持未配置。</summary>
    [TestMethod]
    public void List_ShouldDefaultOnlyNewRowsToCny()
    {
        var list = new CustomerPoConfigListModel();
        Assert.AreEqual(CurrencyEnum.CNY, list.AddNewConfig().Currency);

        list.LoadFromDtos([new CustomerPoConfigDto { Uid = Guid.NewGuid(), Version = 1, CustomerCode = "C001", SupplierCode = "S001", Currency = null }]);
        Assert.AreEqual(string.Empty, list.Items.Single().Currency);
        Assert.AreEqual(LineState.Unchanged, list.Items.Single().State);
    }

    /// <summary>新增字段应参与脏跟踪、保存与放弃修改，折扣备注不依赖折扣率。</summary>
    [TestMethod]
    public void Line_ShouldTrackSaveAndRejectIndependentDefaults()
    {
        var list = new CustomerPoConfigListModel();
        list.LoadFromDtos([new CustomerPoConfigDto
        {
            Uid = Guid.NewGuid(), Version = 2, CustomerCode = "C001", SupplierCode = "S001",
            Currency = "USD", DefaultDiscountRate = null, DiscountRemark = "仅备注"
        }]);
        var line = list.Items.Single();
        line.Currency = "CNY";
        line.DefaultDiscountRate = 0.95m;
        line.DiscountRemark = string.Empty;

        var dto = list.CreateSaveRequest().UpdateItems.Single().Data;
        Assert.AreEqual("CNY", dto.Currency);
        Assert.AreEqual(0.95m, dto.DefaultDiscountRate);
        Assert.AreEqual(string.Empty, dto.DiscountRemark);

        list.RejectChanges();
        Assert.AreEqual("USD", line.Currency);
        Assert.IsNull(line.DefaultDiscountRate);
        Assert.AreEqual("仅备注", line.DiscountRemark);
    }

    /// <summary>折扣率应接受涨价倍率，拒绝负数及超过六位有效小数。</summary>
    [TestMethod]
    public void Line_ShouldValidateDiscountRateAndNewFieldLengths()
    {
        foreach (var value in new decimal?[] { null, 0m, 1m, 0.95m, 1.05m, 12.5m, 0.123456m, 0.9500000m })
        {
            var valid = ValidLine();
            valid.DefaultDiscountRate = value;
            Assert.IsNull(valid.ValidateRow(), $"应接受 {value?.ToString() ?? "NULL"}");
        }

        var negative = ValidLine();
        negative.DefaultDiscountRate = -0.000001m;
        StringAssert.Contains(negative.ValidateRow(), "不能小于 0");
        var overScale = ValidLine();
        overScale.DefaultDiscountRate = 0.1234567m;
        StringAssert.Contains(overScale.ValidateRow(), "6 位小数");

        var lengths = ValidLine();
        lengths.Currency = new string('币', 11);
        StringAssert.Contains(lengths.ValidateRow(), "10 个字符");
        lengths.Currency = "CNY";
        lengths.DiscountRemark = new string('注', 501);
        StringAssert.Contains(lengths.ValidateRow(), "500 个字符");
    }

    /// <summary>同一客户和供应商组合应忽略大小写并在保存前被阻止。</summary>
    [TestMethod]
    public async Task List_ShouldRejectDuplicateCustomerSupplierPair()
    {
        var list = new CustomerPoConfigListModel();
        var first = list.AddNewConfig();
        first.CustomerCode = "C001";
        first.SupplierCode = "S001";
        var second = list.AddNewConfig();
        second.CustomerCode = "c001";
        second.SupplierCode = "s001";

        var error = await list.ValidateListAsync();

        StringAssert.Contains(error, "客户+供应商组合重复");
    }

    /// <summary>行校验应遵守真实数据库的人员字段长度。</summary>
    [TestMethod]
    public void Line_ShouldRejectTextBeyondDatabaseLength()
    {
        var line = new CustomerPoConfigLineModel
        {
            CustomerCode = "C001",
            SupplierCode = "S001",
            SalesPerson = new string('销', 11)
        };

        Assert.AreEqual("销售人员不能超过 10 个字符。", line.ValidateRow());
    }

    /// <summary>客户模块菜单和 Shell 应公开独立管理页面。</summary>
    [TestMethod]
    public void NavigationAndView_ShouldRegisterManagerPage()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(CustomerPoConfigManagerViewModel.RouteKey, out var route));
        Assert.AreEqual(typeof(CustomerPoConfigManagerViewModel), route.ViewModelType);
        var customers = registry.MenuItems.Single(item => item.RouteKey == "customers");
        Assert.IsTrue(customers.Children!.Any(item => item.RouteKey == CustomerPoConfigManagerViewModel.RouteKey));

        var view = Read("SwiftExport.Client.Wpf", "Features", "Customers", "CustomerPoConfigs", "Views", "CustomerPoConfigManagerView.xaml");
        StringAssert.Contains(view, "CustomerAutoSuggestComboBox");
        StringAssert.Contains(view, "SupplierAutoSuggestComboBox");
        StringAssert.Contains(view, "CustomerPoConfigCurrencyOptionTemplate");
        StringAssert.Contains(view, "SelectedValuePath=\"CurrencyCode\"");
        StringAssert.Contains(view, "FieldPath=\"DefaultDiscountRate\"");
        StringAssert.Contains(view, "FieldPath=\"DiscountRemark\"");
        StringAssert.Contains(view, "gridValidation:GridValidation.IsEnabled=\"True\"");
        StringAssert.Contains(view, "GridSelectionUnitConverter");
        StringAssert.Contains(view, "gridNumeric:NumericDataGridColumn");
        StringAssert.Contains(view, "MaximumDecimalPlaces=\"6\"");
        StringAssert.Contains(view, "PageNavigator");

        var viewModel = Read("SwiftExport.Client.Wpf", "Features", "Customers", "CustomerPoConfigs", "ViewModels", "CustomerPoConfigManagerViewModel.cs");
        StringAssert.Contains(viewModel, "CollectionChangedEventManager.AddHandler(_cache.Currencies");
        StringAssert.Contains(viewModel, "CurrencyNameZh = \"（历史值）\"");
        StringAssert.Contains(viewModel, "CollectionChangedEventManager.RemoveHandler(_cache.Currencies");
    }

    private static CustomerPoConfigLineModel ValidLine() => new()
    {
        CustomerCode = "C001",
        SupplierCode = "S001",
        Currency = "CNY"
    };

    private static void AssertRoute(string methodName, string path)
    {
        var method = typeof(ICustomerPoConfigApi).GetMethod(methodName)!;
        var post = method.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single();
        Assert.AreEqual(path, post.Path);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".git"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("未找到仓库根目录。");
    }
}
