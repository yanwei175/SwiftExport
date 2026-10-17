using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Resources.Behaviors;

/// <summary>验证筛选全选适配解除后不再监听列表，重新挂接读取原生现状。</summary>
[TestClass]
[DoNotParallelize]
public sealed class FilterTemplateBehaviorTests
{
    /// <summary>单个行为可解绑再挂接；空候选显示未选，候选变化不会自动改变业务集合。</summary>
    [TestMethod]
    public void SelectAll_ShouldDetachAndReattachWithoutStaleSubscriptions() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        var list = new ListBox();
        list.Items.Add("甲");
        list.Items.Add("乙");
        var behavior = new FilterSelectAllBehavior();
        Interaction.GetBehaviors(list).Add(behavior);
        var window = new Window { Content = list, Width = 300, Height = 200, ShowInTaskbar = false };
        try
        {
            window.Show();
            await list.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            behavior.IsAllSelected = true;
            Assert.AreEqual(2, list.SelectedItems.Count);
            list.SelectedItems.Remove("甲");
            Assert.IsNull(behavior.IsAllSelected);
            Interaction.GetBehaviors(list).Remove(behavior);
            list.UnselectAll();
            list.Items.Clear();
            Assert.IsNull(behavior.IsAllSelected, "解绑后不应再响应原列表变化。");
            Interaction.GetBehaviors(list).Add(behavior);
            Assert.AreEqual(false, behavior.IsAllSelected);
            list.Items.Add("丙");
            behavior.IsAllSelected = true;
            Assert.AreEqual(1, list.SelectedItems.Count);
            Assert.AreEqual(true, behavior.IsAllSelected);
            Interaction.GetBehaviors(list).Remove(behavior);
        }
        finally { window.Close(); }
    });
}
