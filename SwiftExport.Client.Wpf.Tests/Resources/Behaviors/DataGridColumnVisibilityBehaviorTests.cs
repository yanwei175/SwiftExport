using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Resources.Behaviors;

/// <summary>验证列显隐独立用于普通 DataGrid，菜单拥有权、卸载重载及解绑不影响宿主。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DataGridColumnVisibilityBehaviorTests
{
    /// <summary>无需筛选库或业务 VM 即可使用；已有菜单保留，重复加载不重复注入，移除后恢复宿主。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Menu_ShouldRespectOwnershipAndReload(bool existingMenu) => StaTest.RunAsync(async () =>
    {
        var grid = new DataGrid();
        var column = new DataGridTextColumn { Header = "名称" };
        grid.Columns.Add(column);
        var hostItem = new MenuItem { Header = "宿主命令" };
        var original = existingMenu ? new ContextMenu() : null;
        if (original is not null) original.Items.Add(hostItem);
        grid.ContextMenu = original;
        var behavior = new DataGridColumnVisibilityBehavior();
        Interaction.GetBehaviors(grid).Add(behavior);
        var window = new Window { Content = grid, Width = 400, Height = 250, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Drain();
            for (var pass = 0; pass < 2; pass++)
            {
                var menu = grid.ContextMenu;
                Assert.IsNotNull(menu);
                if (existingMenu) Assert.AreSame(original, menu);
                Assert.AreEqual(existingMenu ? 2 : 1, menu.Items.Count);
                menu.PlacementTarget = grid;
                menu.IsOpen = true;
                await Drain();
                var columns = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "查看"));
                Assert.AreSame(columns, menu.Items[0], "查看必须位于已有或新建菜单首项。");
                Assert.AreSame(column, columns.Items[0]);
                menu.IsOpen = false;
                await Drain();
                Assert.IsEmpty(columns.Items);
                window.Content = null;
                await Drain();
                Assert.AreSame(original, grid.ContextMenu);
                window.Content = grid;
                await Drain();
            }
            Interaction.GetBehaviors(grid).Remove(behavior);
            Assert.AreSame(original, grid.ContextMenu);
            if (existingMenu) Assert.AreSame(hostItem, original!.Items.Cast<object>().Single());
        }
        finally { window.Close(); }
    });

    /// <summary>宿主更换菜单后只解除旧菜单自有项；新菜单继续获得列入口。</summary>
    [TestMethod]
    public void ReplacedMenu_ShouldReleaseOldMenu() => StaTest.RunAsync(async () =>
    {
        var grid = new DataGrid();
        var old = new ContextMenu();
        grid.ContextMenu = old;
        var behavior = new DataGridColumnVisibilityBehavior();
        Interaction.GetBehaviors(grid).Add(behavior);
        var window = new Window { Content = grid, Width = 400, Height = 250, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Drain();
            var replacement = new ContextMenu();
            grid.ContextMenu = replacement;
            window.Content = null;
            await Drain();
            Assert.IsEmpty(old.Items);
            window.Content = grid;
            await Drain();
            Assert.AreEqual(1, replacement.Items.Count);
            Interaction.GetBehaviors(grid).Clear();
            Assert.AreSame(replacement, grid.ContextMenu);
            Assert.IsEmpty(replacement.Items);
        }
        finally { window.Close(); }
    });

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task;
}
