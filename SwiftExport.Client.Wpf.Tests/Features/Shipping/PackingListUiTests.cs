using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.Views;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Converters;
using SwiftExportClient.Services.Api;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证路由注册与真实 XAML 实例化，不启动网络或后端。</summary>
[TestClass, DoNotParallelize]
public sealed class PackingListUiTests
{
    /// <summary>Manager 和 Detail 路由使用出货模块权限，视图模型在 DI 中可发现。</summary>
    [TestMethod]
    public void RoutesAndRegistrationsArePresent()
    {
        var routes = new NavigationRouteRegistry();
        Assert.IsTrue(routes.TryGetRoute(PackingListManagerViewModel.RouteKey, out var manager));
        Assert.IsTrue(routes.TryGetRoute(PackingListDetailViewModel.RouteKey, out var detail));
        Assert.AreEqual(PermissionKeys.ModuleShippingAccess, manager.RequiredPermission);
        Assert.AreEqual(PermissionKeys.ModuleShippingAccess, detail.RequiredPermission);
        var services = new ServiceCollection(); services.AddSwiftExportClientViews();
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(PackingListManagerViewModel)));
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(PackingListDetailViewModel)));
    }
    /// <summary>真实主题下实例化三张页面；管理表始终只读，详情提交回调可接入。</summary>
    [TestMethod]
    public void ViewsLoadWithRealResourcesAndReadonlyManagerGrids() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        var resources = Application.Current.Resources;
        resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        foreach (var path in new[] { "Resources/Tokens.xaml", "Resources/Colors.xaml", "Resources/Typography.xaml", "Resources/Spacing.xaml",
            "Resources/Grids.xaml", "Resources/Borders.xaml", "Resources/Button.xaml", "Resources/日期选择器.xaml", "Resources/DataGrid.xaml",
            "Resources/Image.xaml", "Features/Shipping/页面模板.xaml" })
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/{path}") });
        resources["EnumDescriptionConverter"] = new EnumDescriptionConverter();
        resources["InverseBooleanConverter"] = new InverseBooleanConverter();
        var api = new PackingListApiStub(); var ui = new PackingListUiStub();
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        using var manager = new PackingListManagerViewModel(api, runner, new PackingListNavigationStub(), ui, ui, new ReferenceDataCacheServiceStub());
        var managerView = new PackingListManagerView { DataContext = manager }; Layout(managerView);
        var grids = Descendants(managerView).OfType<DataGrid>().ToArray();
        Assert.AreEqual(2, grids.Length); Assert.IsTrue(grids.All(x => x.IsReadOnly));
        using var detail = new PackingListDetailViewModel(api, runner, new ReferenceDataCacheServiceStub(), new PackingListSourceStub(),
            new PackingListWarehouseStub(), new PackingListNavigationStub(), ui, ui);
        await detail.InitializeDocumentAsync(new PackingListOpenRequest { Uid = Guid.NewGuid(), IsNew = true }, default);
        var detailView = new PackingListDetailView { DataContext = detail }; Layout(detailView);
        detailView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.IsNotNull(detail.CommitEdits); Assert.IsTrue(detail.CommitEdits());
        detailView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Assert.IsNull(detail.CommitEdits);
        using var picker = new PackingListSourcePickerViewModel(api, runner);
        var pickerView = new PackingListSourcePickerView { DataContext = picker }; Layout(pickerView);
        Assert.AreEqual(1, Descendants(pickerView).OfType<DataGrid>().Count());
    });
    private static void Layout(FrameworkElement view)
    { view.Measure(new Size(1600, 900)); view.Arrange(new Rect(0, 0, 1600, 900)); view.UpdateLayout(); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
