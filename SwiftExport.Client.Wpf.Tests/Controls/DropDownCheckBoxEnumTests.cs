using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Resources.Converters;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>验证枚举描述转换只影响多选显示，不替换原始枚举值或选择集合。</summary>
[TestClass]
public sealed class DropDownCheckBoxEnumTests
{
    /// <summary>转换器的设置与移除均刷新显示，并保留现有选中值。</summary>
    [TestMethod]
    public void EnumDescriptionPreservesSelectionAndOriginalDisplayFallback()
    {
        StaTest.RunAsync(() =>
        {
            var selected = new ObservableCollection<PurchaseOrderLifecycleStatusEnum> { PurchaseOrderLifecycleStatusEnum.Active };
            var control = new DropDownCheckBox { ItemsSource = new[] { PurchaseOrderLifecycleStatusEnum.Active }, SelectedItems = selected };
            var items = (ItemsControl)control.FindName("OptionItemsControl");
            string Display() => (string)TypeDescriptor.GetProperties(items.Items[0])["DisplayText"]!.GetValue(items.Items[0])!;
            Assert.AreEqual("Active", Display());
            control.DisplayConverter = new EnumDescriptionConverter();
            Assert.AreEqual("活动", Display());
            Assert.AreSame(selected, control.SelectedItems);
            Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Active, selected.Single());
            control.DisplayConverter = null;
            Assert.AreEqual("Active", Display());
            return Task.CompletedTask;
        });
    }
}
