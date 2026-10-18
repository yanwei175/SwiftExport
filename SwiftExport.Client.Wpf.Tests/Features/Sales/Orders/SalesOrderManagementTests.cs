using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Sales.Orders.Components;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Sales.Orders.Views;
using SwiftExportClient.Features.SystemSettings.SystemParameters.Models;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

/// <summary>验证销售事实页面边界、客户多选及默认交期的真实编辑与异步保护。</summary>
[TestClass, DoNotParallelize]
public sealed class SalesOrderManagementTests
{
    /// <summary>使用服务端默认快照或配置值按自然日计算；0天不跳过，非午夜订单日期取Date。</summary>
    [TestMethod]
    [DataRow("60", false, "2027-02-18")] [DataRow("17", false, "2027-01-06")] [DataRow("45", true, "2027-02-03")] [DataRow("0", true, "2026-12-20")] [DataRow(" 60 ", true, "2027-02-18")]
    public void NewOrder_UsesFreshParameterSnapshot(string value, bool configured, string expectedDate) => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); f.Parameters.Parameter.Value = value; f.Parameters.Parameter.IsConfigured = configured;
        await f.Detail.InitializeDocumentAsync(null, default);
        f.Detail.Header.OrderDate = new DateTime(2026, 12, 20, 18, 30, 0);
        Assert.AreEqual(DateTime.ParseExact(expectedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), f.Detail.Header.DeliveryDate);
        Assert.AreEqual(1, f.Parameters.Refreshes);
        Assert.AreEqual(string.Empty, new SystemParameterLineModel { Key = SystemParameterKeys.SalesDefaultDeliveryDays, Value = value }.ValidationMessage);
        f.Detail.Header.DeliveryDate = new DateTime(2027, 6, 1);
        f.Detail.Header.OrderDate = new DateTime(2027, 1, 1);
        Assert.AreEqual(new DateTime(2027, 6, 1), f.Detail.Header.DeliveryDate);
    });

    /// <summary>严格整数规则与后端一致；已配置空值不得被TryGetValue过滤后变成默认60。</summary>
    [TestMethod]
    [DataRow("")] [DataRow("+60")] [DataRow("-1")] [DataRow("1.5")] [DataRow("2147483648")]
    public void InvalidParameter_BlocksImplicitDateSave(string value) => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); f.Parameters.Parameter.Value = value; f.Parameters.Parameter.IsConfigured = true;
        await f.Detail.InitializeDocumentAsync(null, default); f.FillDraft();
        Assert.IsNull(f.Detail.Header.DeliveryDate); await f.Detail.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Saves); Assert.IsTrue(f.Messages.Any(text => text.Contains("非负整数")));
        var parameterRow = new SystemParameterLineModel { Key = SystemParameterKeys.SalesDefaultDeliveryDays, Value = value };
        Assert.IsFalse(string.IsNullOrEmpty(parameterRow.ValidationMessage));
    });

    /// <summary>自动日期溢出时清除旧自动值并阻止保存，手工指定日期仍优先且不被后续订单日期覆盖。</summary>
    [TestMethod]
    public void AutomaticDateOverflow_DoesNotSaveStaleDate() => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); await f.Detail.InitializeDocumentAsync(null, default); f.FillDraft();
        Assert.IsNotNull(f.Detail.Header.DeliveryDate);
        f.Detail.Header.OrderDate = DateTime.MaxValue.Date; Assert.IsNull(f.Detail.Header.DeliveryDate);
        await f.Detail.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Saves);
        Assert.IsTrue(f.Messages.Any(text => text.Contains("超出")));
        f.Detail.Header.DeliveryDate = new DateTime(2027, 1, 1);
        f.Detail.Header.OrderDate = DateTime.MaxValue.Date.AddDays(-1);
        Assert.AreEqual(new DateTime(2027, 1, 1), f.Detail.Header.DeliveryDate);
    });

    /// <summary>异步参数返回不能覆盖用户输入，关闭成功后也不能给旧页面补交期。</summary>
    [TestMethod]
    public void PendingParameter_RespectsExplicitInputAndClose() => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); var pending = new TaskCompletionSource<SwiftExportClient.Services.Caching.CacheRefreshResult>();
        f.Parameters.Pending = pending.Task; var loading = f.Detail.InitializeDocumentAsync(null, default);
        var explicitDate = new DateTime(2027, 3, 8); f.Detail.Header.DeliveryDate = explicitDate;
        pending.SetResult(await new ReferenceDataCacheServiceStub().EnsureLoadedAsync()); await loading;
        Assert.AreEqual(explicitDate, f.Detail.Header.DeliveryDate);
        var closed = new Fixture(); pending = new(); closed.Parameters.Pending = pending.Task;
        loading = closed.Detail.InitializeDocumentAsync(null, default); Assert.IsTrue(await closed.Detail.CanCloseAsync(default));
        pending.SetResult(await new ReferenceDataCacheServiceStub().EnsureLoadedAsync()); await loading;
        Assert.IsNull(closed.Detail.Header.DeliveryDate);
    });

    /// <summary>首次保存后的日期成为事实；参数或订单日期变动不再触发重算，行空日期仍保持为空。</summary>
    [TestMethod]
    public void SavedOrder_FreezesDateAndPreservesSnapshots() => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); await f.Detail.InitializeDocumentAsync(null, default); f.FillDraft();
        var date = f.Detail.Header.DeliveryDate; await f.Detail.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Saves); Assert.IsTrue(f.Detail.IsPersisted); Assert.IsFalse(f.Detail.HasUnsavedChanges);
        Assert.IsNull(f.LastSave!.Lines.Single().ExpectedDeliveryDate);
        f.Parameters.Parameter.Value = "5"; f.Detail.Header.OrderDate = new DateTime(2030, 1, 1);
        Assert.AreEqual(date, f.Detail.Header.DeliveryDate); Assert.AreEqual(1, f.Parameters.Refreshes);
        var row = f.Detail.Lines.Items.Single();
        Assert.AreEqual("LINK-HISTORY", row.LinkSku); Assert.AreEqual("历史产品", row.DescriptionEn);
        Assert.AreEqual(6m, row.PcsOfCarton);
    });

    /// <summary>历史交期和历史空交期均原样加载及保存；不读取默认参数或静默补齐旧记录。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ExistingOrder_DoesNotBackfillDate(bool hasDate) => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); f.Document.Header.DeliveryDate = hasDate ? new DateTime(2026, 9, 1) : null;
        await f.Detail.InitializeDocumentAsync(new SalesOrderDocumentOpenRequest(f.Document.Header.Uid), default);
        var expected = f.Detail.Header.DeliveryDate; f.Detail.Header.OrderDate = new DateTime(2028, 4, 1);
        await f.Detail.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(expected, f.LastSave!.Header.DeliveryDate); Assert.AreEqual(expected, f.Detail.Header.DeliveryDate);
        Assert.AreEqual("历史图片", f.Detail.Lines.Items.Single().MainImage);
        Assert.AreEqual(0, f.Parameters.Refreshes);
    });

    /// <summary>参数仍在加载时保存，以服务器回执日期为事实；较晚返回的参数不能覆盖已保存订单。</summary>
    [TestMethod]
    public void LateParameter_AfterSaveCannotReplaceServerDate() => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); var pending = new TaskCompletionSource<SwiftExportClient.Services.Caching.CacheRefreshResult>();
        f.Parameters.Pending = pending.Task; f.ServerDefaultDate = new DateTime(2027, 8, 12);
        var loading = f.Detail.InitializeDocumentAsync(null, default); f.FillDraft();
        await f.Detail.SaveCommand.ExecuteAsync(null); Assert.IsTrue(f.Detail.IsPersisted);
        Assert.IsNull(f.LastSave!.Header.DeliveryDate); Assert.AreEqual(f.ServerDefaultDate, f.Detail.Header.DeliveryDate);
        f.Parameters.Parameter.Value = "5";
        pending.SetResult(await new ReferenceDataCacheServiceStub().EnsureLoadedAsync()); await loading;
        Assert.AreEqual(f.ServerDefaultDate, f.Detail.Header.DeliveryDate); Assert.AreEqual(1, f.Parameters.Refreshes);
    });

    /// <summary>编辑中的数量合计只解释当前订单单位，交期改变不能抹掉数量；多单位和缺单位分别提示。</summary>
    [TestMethod]
    public void DetailQuantity_UsesOrderLinesAndCompleteUnit() => StaTest.RunAsync(async () =>
    {
        var f = new Fixture(); await f.Detail.InitializeDocumentAsync(null, default);
        Assert.AreEqual(0m, f.Detail.TotalQty); Assert.IsFalse(f.Detail.HasMissingUnit);
        f.FillDraft(); Assert.AreEqual(2m, f.Detail.TotalQty);
        f.Detail.AddNonStandardLineCommand.Execute(null); var second = f.Detail.Lines.Items.Last();
        second.Qty = 3; second.UnitSnapshot = "BOX";
        Assert.IsNull(f.Detail.TotalQty); Assert.IsTrue(f.Detail.HasMixedUnits); Assert.IsFalse(f.Detail.HasMissingUnit);
        second.UnitSnapshot = string.Empty; Assert.IsTrue(f.Detail.HasMissingUnit); Assert.IsNull(f.Detail.TotalQty);
        second.UnitSnapshot = "pcs "; f.Detail.Header.DeliveryDate = new DateTime(2030, 1, 1);
        Assert.AreEqual(5m, f.Detail.TotalQty);
        second.UnitSnapshot = " PCS"; Assert.IsTrue(f.Detail.HasMixedUnits); Assert.IsNull(f.Detail.TotalQty);
    });

    /// <summary>实际详情绑定动态继承头交期，编辑行日期后独立保留，不把显示继承值写回行。</summary>
    [TestMethod]
    public void ActualDetail_EmptyLineDateFollowsHeader() => WithApplication(async () =>
    {
        var f = new Fixture(); await f.Detail.InitializeDocumentAsync(null, default); f.FillDraft();
        var view = new SalesOrderDetailView { DataContext = f.Detail };
        await InWindow(view, async () =>
        {
            var grid = Descendants(view).OfType<DataGrid>().Single(item => ReferenceEquals(item.ItemsSource, f.Detail.Lines.Items));
            var row = f.Detail.Lines.Items[0]; var column = grid.Columns.Single(item => Equals(item.Header, "预计交期"));
            grid.ScrollIntoView(row, column); grid.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            string Display() => Descendants(column.GetCellContent(row)).OfType<TextBlock>().Single().Text;
            f.Detail.Header.DeliveryDate = new DateTime(2027, 1, 2); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual("2027-01-02", Display()); Assert.IsNull(row.ExpectedDeliveryDate);
            f.Detail.Header.DeliveryDate = new DateTime(2027, 2, 3); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual("2027-02-03", Display()); Assert.IsNull(row.ToEditDto().ExpectedDeliveryDate);
            row.ExpectedDeliveryDate = new DateTime(2027, 3, 4); f.Detail.Header.DeliveryDate = new DateTime(2027, 4, 5);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.AreEqual("2027-03-04", Display());
        });
    });

    /// <summary>真实多选弹窗先暂存，确认后同时发送两个客户代码；订单单位异常与商业快照独立展示。</summary>
    [TestMethod]
    public void ActualManager_MultiSelectAndOrderSnapshots() => WithApplication(async () =>
    {
        var f = new Fixture(); f.Document.Header.DeliveryDate = new DateTime(2027, 8, 12); await f.Manager.InitializeAsync();
        var view = new SalesOrderManagerView { DataContext = f.Manager };
        await InWindow(view, async () =>
        {
            var selector = (MultiSelectLookupButton)view.FindName("CustomerCodesSelector");
            var open = (ToggleButton)selector.FindName("PART_Button");
            ((IToggleProvider)new ToggleButtonAutomationPeer(open).GetPattern(PatternInterface.Toggle)!).Toggle();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var popup = (Popup)selector.FindName("PART_Popup"); Assert.IsTrue(popup.IsOpen);
            var choices = Descendants(popup.Child).OfType<ToggleButton>().ToArray(); Assert.AreEqual(2, choices.Length);
            foreach (var choice in choices) ((IToggleProvider)new ToggleButtonAutomationPeer(choice).GetPattern(PatternInterface.Toggle)!).Toggle();
            Assert.IsEmpty(f.Manager.SelectedCustomerCodes);
            var apply = Descendants(popup.Child).OfType<Button>().Single(button => Equals(button.Content, "确定"));
            ((IInvokeProvider)new ButtonAutomationPeer(apply).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.IsFalse(popup.IsOpen);
            await f.Manager.SearchCommand.ExecuteAsync(null);
            CollectionAssert.AreEquivalent(new[] { "C1", "C2" }, f.Search!.CustomerCodes);
            Assert.AreSame(f.Manager.SelectedCustomerCodes, selector.SelectedItems);
            await f.Manager.ClearFiltersCommand.ExecuteAsync(null);
            Assert.IsEmpty(f.Manager.SelectedCustomerCodes); Assert.IsEmpty(f.Search!.CustomerCodes);
            ((IToggleProvider)new ToggleButtonAutomationPeer(open).GetPattern(PatternInterface.Toggle)!).Toggle();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            choices = Descendants(popup.Child).OfType<ToggleButton>().ToArray();
            Assert.IsTrue(choices.All(choice => choice.IsChecked == false));
            ((IToggleProvider)new ToggleButtonAutomationPeer(choices[0]).GetPattern(PatternInterface.Toggle)!).Toggle();
            popup.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsEmpty(f.Manager.SelectedCustomerCodes);
            var orderGrid = Descendants(view).OfType<DataGrid>().Single(grid => ReferenceEquals(grid.ItemsSource, f.Manager.Orders));
            var unit = orderGrid.Columns.Single(column => Equals(column.Header, "单位"));
            var order = f.Manager.Orders.Single(); orderGrid.ScrollIntoView(order, unit); orderGrid.UpdateLayout();
            var texts = Descendants(unit.GetCellContent(order)).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToArray();
            CollectionAssert.Contains(texts, "多单位"); CollectionAssert.Contains(texts, "缺少单位");
            Assert.IsNull(order.TotalQty); Assert.AreEqual("INV-FACT", order.InvoiceNo);
            var headers = orderGrid.Columns.Select(column => column.Header?.ToString()).ToArray();
            foreach (var header in new[] { "发票号", "订单交期", "数量", "单位", "原币金额", "确认", "单据" }) CollectionAssert.Contains(headers, header);
            Assert.IsFalse(Descendants(view).OfType<Button>().Any(button => button.Content?.ToString()?.Contains("履约") == true));
            var lines = Descendants(view).OfType<DataGrid>().Single(grid => ReferenceEquals(grid.ItemsSource, f.Manager.SelectedOrderLines));
            Assert.AreEqual(new DateTime(2027, 8, 12), f.Manager.SelectedOrderDeliveryDate);
            var lineDate = lines.Columns.Single(column => Equals(column.Header, "预计交期")); var lineRow = f.Manager.SelectedOrderLines.Single();
            lines.ScrollIntoView(lineRow, lineDate); lines.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual("2027-08-12", Descendants(lineDate.GetCellContent(lineRow)).OfType<TextBlock>().Single().Text);
            foreach (var header in new[] { "中性型号", "规格", "每箱数量", "每箱净重", "每箱毛重", "每箱体积", "USD金额", "CNY金额" })
                Assert.IsTrue(lines.Columns.Any(column => Equals(column.Header, header)));
        });
    });

    /// <summary>只替换API和参数传输，保留真实编辑模型、保存冻结、当前事实重读与权限判断。</summary>
    private sealed class Fixture
    {
        public SalesOrderTestParameters Parameters { get; } = new();
        public SalesOrderDetailViewModel Detail { get; }
        public SalesOrderManagerViewModel Manager { get; }
        public SalesOrderDetailDto Document { get; private set; } = new()
        {
            Header = new() { Uid = Guid.NewGuid(), Version = 1, CustomerId = Guid.NewGuid(), CustomerCode = "C1", OrderNo = "SO-FACT",
                OrderDate = new DateTime(2026, 10, 1), DocumentStatus = SalesOrderDocumentStatusEnum.Open, ConfirmationStatus = ConfirmationStatusEnum.待确认 },
            Lines = [new() { Uid = Guid.NewGuid(), Version = 1, IsNonStandard = true, Sku = "NS-0123456789AB", Qty = 2, DescriptionEn = "历史产品", UnitSnapshot = "PCS", LinkSku = "LINK-HISTORY", MainImage = "历史图片", PcsOfCarton = 6 }]
        };
        public int Saves { get; private set; }
        public DateTime? ServerDefaultDate { get; set; }
        public SaveSalesOrderRequest? LastSave { get; private set; }
        public SalesOrderManagerSearchRequest? Search { get; private set; }
        public List<string> Messages { get; } = [];
        /// <summary>模拟销售读取、保存回执和导航，参数刷新使用独立可延迟替身。</summary>
        public Fixture()
        {
            var user = new CurrentUserService(); user.SetCurrentUser(new MeDto { Permissions = PermissionCatalog.All.Select(item => item.PermissionKey).ToList() });
            var api = Proxy<ISalesOrderDetailApi>((method, args) =>
            {
                if (method.Name == "GetAsync") return Task.FromResult(ServiceResult<SalesOrderDetailDto>.Ok(Document));
                if (method.Name == "SaveAsync")
                {
                    Saves++; LastSave = (SaveSalesOrderRequest)args[0]!;
                    var images = Document.Lines.ToDictionary(line => line.Uid, line => line.MainImage);
                    Document = new() { Header = LastSave.Header.Adapt<SalesOrderEditHeaderDto>(), Lines = LastSave.Lines.Adapt<List<SalesOrderDetailLineDto>>() };
                    foreach (var line in Document.Lines) if (images.TryGetValue(line.Uid, out var image)) line.MainImage = image;
                    Document.Header.Uid = Document.Header.Uid == Guid.Empty ? Guid.NewGuid() : Document.Header.Uid;
                    if (LastSave.Header.Uid == Guid.Empty && LastSave.Header.DeliveryDate is null) Document.Header.DeliveryDate = ServerDefaultDate;
                    Document.Header.Version++; return Task.FromResult(ServiceResult<SaveSalesOrderResponse>.Ok(new() { Document = Document }));
                }
                throw new AssertFailedException(method.Name);
            });
            var manager = Proxy<ISalesOrderManagerApi>((method, args) =>
            {
                if (method.Name == "GetCustomerCodesAsync") return Task.FromResult(ServiceResult<List<string>>.Ok(["C1", "C2"]));
                Search = (SalesOrderManagerSearchRequest)args[0]!;
                return Task.FromResult(ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>.Ok(new()
                { TotalCount = 1, Items = [new() { Uid = Document.Header.Uid, InvoiceNo = "INV-FACT", HasMixedUnits = true, HasMissingUnit = true, DeliveryDate = new DateTime(2027, 1, 1) }] }));
            });
            var runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
            var messages = Proxy<IMessageService>((_, args) => { Messages.Add(string.Join(" ", args)); return null; });
            var dialogs = Proxy<IDialogService>((method, _) => method.Name == "ConfirmAsync" ? Task.FromResult(true) : Task.CompletedTask);
            var navigation = Proxy<INavigationService>((method, _) => method.ReturnType == typeof(Task) ? Task.CompletedTask : null);
            Detail = new(api, runner, navigation, dialogs, null!, messages, new ReferenceDataCacheServiceStub(), user, Parameters);
            Manager = new(manager, api, runner, navigation, dialogs, null!, messages, user);
        }
        /// <summary>通过实际新增行命令建立合法非标草稿，行交期保持未指定。</summary>
        public void FillDraft()
        {
            Detail.Header.CustomerId = Guid.NewGuid(); Detail.Header.CustomerCode = "C1";
            Detail.AddNonStandardLineCommand.Execute(null); var row = Detail.Lines.Items.Single();
            row.Qty = 2; row.DescriptionEn = "历史产品"; row.UnitSnapshot = "PCS"; row.LinkSku = "LINK-HISTORY"; row.MainImage = "历史图片"; row.PcsOfCarton = 6;
        }
    }

    /// <summary>在既有应用Dispatcher上运行实际页面，不启动业务客户端或连接数据库。</summary>
    private static void WithApplication(Func<Task> action) => DatePickerAssistTests.RunWithApplication(async () => { ApplicationResourceLoader.Load(); await action(); });
    /// <summary>窗口只属于当前用例，结束后卸载并清理Popup。</summary>
    private static async Task InWindow(UserControl view, Func<Task> action)
    {
        var window = new Window { Content = view, Width = 1600, Height = 900, ShowInTaskbar = false };
        try { window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await action(); }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    }
    /// <summary>遍历真实视觉树以验证模板和绑定的可观察结果。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    { var result = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)result).Call = call; return result; }
    /// <summary>将指定API调用交给当前夹具，拒绝未声明的真实传输。</summary>
    public class TestProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args ?? []);
    }
}
