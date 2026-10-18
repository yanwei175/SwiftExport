using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Navigation;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Fulfillment;

/// <summary>验证销售事实页面已退出履约入口，同时保留需求追踪及既有确认命令身份。</summary>
[TestClass]
public sealed class SalesFulfillmentContractsTests
{
    /// <summary>退出的履约路由不可通过隐藏键继续打开，销售事实入口及独立需求追踪仍注册。</summary>
    [TestMethod]
    public void RetiredFulfillmentRoutesAreAbsentWhileOrderAndDemandRoutesRemain()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsFalse(registry.TryGetRoute("sales-fulfillment-manager", out _));
        Assert.IsFalse(registry.TryGetRoute("sales-fulfillment-detail", out _));
        Assert.IsTrue(registry.TryGetRoute("sales-orders", out var orders));
        Assert.AreEqual("Sales.Order.View", orders.RequiredPermission);
        Assert.IsTrue(registry.Routes.Any(route => route.ViewModelType.Name == "GrossDemandTraceViewModel"));
        Assert.IsTrue(registry.Routes.Any(route => route.ViewModelType.Name == "MaterialDemandTraceViewModel"));
    }

    [TestMethod]
    public void SalesManagerAndDetailCreateNonEmptyConfirmCommandIds()
    {
        var manager = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Orders", "ViewModels", "SalesOrderManagerViewModel.cs");
        var detail = Read("SwiftExport.Client.Wpf", "Features", "Sales", "Orders", "ViewModels", "SalesOrderDetailViewModel.cs");

        StringAssert.Contains(manager, "ConfirmSalesOrderCommand");
        StringAssert.Contains(manager, "CommandId = Guid.NewGuid()");
        StringAssert.Contains(detail, "ConfirmSalesOrderCommand");
        StringAssert.Contains(detail, "CommandId = Guid.NewGuid()");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    /// <summary>从测试输出目录向上定位仓库根目录，以读取页面和路由源码契约。</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
