using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Converters;
using SwiftExportClient.Services.UI;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>在真实 STA 视觉树验证关联页面绑定、选择与编辑提交，不通过源码文本推断功能。</summary>
[TestClass, DoNotParallelize]
public sealed class PurchaseDemandAssociationViewTests
{
    /// <summary>真实页面可选行查询；格式错误阻断按钮，网格编辑在预览前提交并撤销旧预览。</summary>
    [TestMethod]
    public void RealViewSelectionEditingAndButtonActivationUseCurrentValues()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            LoadResources();
            using var fixture = new PurchaseDemandAssociationViewModelTests.Fixture();
            await fixture.OpenAsync();
            var errors = new BindingErrorListener();
            PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
            var view = new PurchaseDemandAssociationView { DataContext = fixture.Vm };
            var window = new Window { Content = view, Width = 1600, Height = 1050, Left = -5000, Top = -5000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show(); view.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var grids = Descendants(view).OfType<DataGrid>().ToArray();
                var lines = grids.Single(x => AutomationProperties.GetName(x) == "采购关联行");
                lines.SelectedItem = fixture.Vm.Lines[0];
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.HasCount(1, fixture.Vm.Candidates);
                var candidates = grids.Single(x => AutomationProperties.GetName(x) == "可分配物料需求");
                candidates.SelectedItem = fixture.Vm.Candidates.Single();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreSame(fixture.Vm.Candidates.Single(), fixture.Vm.SelectedCandidate);
                Assert.AreEqual(string.Empty, errors.Text, "初始页面绑定不能出现路径或类型错误。");
                var quantity = Descendants(view).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "本次分配数量");
                quantity.Text = "invalid";
                quantity.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Invoke((Button)view.FindName("AddButton"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.HasCount(0, fixture.Vm.Plan); StringAssert.Contains(fixture.Vm.StatusText, "数量格式错误");
                quantity.Text = "3"; quantity.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Invoke((Button)view.FindName("AddButton"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.HasCount(1, fixture.Vm.Plan);
                Invoke((Button)view.FindName("PreviewButton"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.IsNotNull(fixture.Vm.Preview);
                fixture.Vm.ReviewTabIndex = 0;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var plan = (DataGrid)view.FindName("PlanGrid");
                plan.SelectedItem = fixture.Vm.Plan[0]; plan.CurrentCell = new(fixture.Vm.Plan[0], plan.Columns[6]);
                Assert.IsTrue(plan.BeginEdit()); plan.UpdateLayout();
                var editor = Descendants(plan).OfType<TextBox>().Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Quantity");
                editor.Text = "4"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsNull(fixture.Vm.Preview);
                Invoke((Button)view.FindName("PreviewButton"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(4m, fixture.Previews.Last().Items.Single().Quantity);
                Assert.IsNotNull(fixture.Vm.Preview);
            }
            finally { window.Close(); PresentationTraceSources.DataBindingSource.Listeners.Remove(errors); }
        });
    }
    /// <summary>功能模板、Shell 菜单和详情入口共用固定路由，详情不传递当前单据身份。</summary>
    [TestMethod]
    public void NavigationAndFeatureTemplateExposeIndependentWorkspace()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(PurchaseDemandAssociationViewModel.RouteKey, out var route));
        Assert.AreEqual(typeof(PurchaseDemandAssociationViewModel), route.ViewModelType);
        Assert.AreEqual("Procurement.Order.Edit", route.RequiredPermission);
        Assert.IsTrue(registry.MenuItems.Single(x => x.RouteKey == "procurement").Children.Any(x => x.RouteKey == route.Key));
        DatePickerAssistTests.RunWithApplication(() =>
        {
            LoadResources();
            var templates = new ResourceDictionary { Source = new Uri("pack://application:,,,/SwiftExport.Client.Wpf;component/Features/Procurement/页面模板.xaml") };
            var template = (DataTemplate)templates[new DataTemplateKey(typeof(PurchaseDemandAssociationViewModel))];
            Assert.IsInstanceOfType<PurchaseDemandAssociationView>(template.LoadContent());
            return Task.CompletedTask;
        });
    }
    /// <summary>真实导航重复打开及详情入口激活同一页签，保留计划，不传入详情当前采购。</summary>
    [TestMethod]
    public async Task DetailEntryReusesWorkspaceAndDirtyCloseIsExplicit()
    {
        using var fixture = new PurchaseDemandAssociationViewModelTests.Fixture();
        using var provider = new ServiceCollection().AddTransient(_ => fixture.Vm).BuildServiceProvider();
        var registry = new NavigationRouteRegistry();
        using var navigation = new NavigationService(new NavigationTabFactory(provider.GetRequiredService<IServiceScopeFactory>()),
            registry, new NavigationAccessService(registry, fixture.User), fixture.User, new StatusMessageService(), NullLogger<NavigationService>.Instance);
        await navigation.OpenAsync(PurchaseDemandAssociationViewModel.RouteKey);
        fixture.Vm.OrderPicker.SelectedOrder = fixture.Vm.OrderPicker.Orders.Single();
        await fixture.Vm.ChooseOrderCommand.ExecuteAsync(null);
        await fixture.SelectAsync(0); fixture.Vm.AddAllocationCommand.Execute(null);
        using var detail = new PurchaseOrderDetailViewModel(null!, null!, fixture.Dialogs, null!, navigation, fixture.User)
            { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-OTHER" };
        await detail.OpenDemandAssociationCommand.ExecuteAsync(null);
        Assert.HasCount(1, navigation.OpenTabs);
        Assert.AreSame(fixture.Vm, navigation.SelectedTab!.ContentViewModel);
        Assert.HasCount(1, fixture.Searches); Assert.HasCount(1, fixture.Vm.Plan);
        Assert.AreEqual(fixture.Context.PurchaseOrderId, fixture.Vm.Context!.PurchaseOrderId);
        fixture.Dialogs.Confirm = false; await navigation.CloseAsync(navigation.SelectedTab);
        Assert.HasCount(1, navigation.OpenTabs);
        fixture.Dialogs.Confirm = true; await navigation.CloseAsync(navigation.SelectedTab);
        Assert.HasCount(0, navigation.OpenTabs);
    }
    /// <summary>只有读取或只有编辑权限都不能进入路由，两个权限同时满足才可进入。</summary>
    [TestMethod]
    public void RouteRequiresBothReadAndEditPermissions()
    {
        var registry = new NavigationRouteRegistry(); var user = new CurrentUserService();
        var access = new NavigationAccessService(registry, user);
        foreach (var permission in new[] { PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit })
        {
            user.SetCurrentUser(new() { Permissions = [permission] });
            Assert.IsFalse(access.CanAccess(PurchaseDemandAssociationViewModel.RouteKey));
        }
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit] });
        Assert.IsTrue(access.CanAccess(PurchaseDemandAssociationViewModel.RouteKey));
    }
    /// <summary>载入项目实际字体、主题和枚举转换器，避免用伪样式遮掩资源错误。</summary>
    private static void LoadResources()
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        foreach (var name in new[] { "Variables", "Typography" }) Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/{name}.xaml") });
        Application.Current.Resources["EnumDescriptionConverter"] = new EnumDescriptionConverter();
    }
    /// <summary>采用按钮自动化激活路径，同键盘激活一样进入 Click 桥接。</summary>
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class BindingErrorListener : TraceListener
    {
        public string Text { get; private set; } = string.Empty;
        public override void Write(string? message) => Text += message;
        public override void WriteLine(string? message) => Text += message;
    }
}
