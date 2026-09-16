using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.UI;
using Wpf.Ui.Controls;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>使用真实 WPF-UI 模板验证禁用按钮仍显示，空文字按钮仍隐藏。</summary>
[TestClass]
public sealed class DialogDisabledButtonTests
{
    /// <summary>阻断、原因空白、有效原因和清空原因均更新点击资格，不让主按钮消失。</summary>
    [TestMethod]
    public void ReversalButtonRemainsVisibleAcrossEligibilityChanges() => StaTest.RunAsync(() =>
    {
        var preview = new ProcurementReversalPreviewDto { CanExecute = false };
        var state = new ProcurementReversalPreviewViewModel(preview);
        var dialog = new ContentDialog { PrimaryButtonText = "整单作废", CloseButtonText = "取消" };
        dialog.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        dialog.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        dialog.Style = (Style)dialog.FindResource(typeof(ContentDialog));
        dialog.ApplyTemplate();
        var apply = typeof(WpfUiDialogService).GetMethod("ApplyButtonState", BindingFlags.Static | BindingFlags.NonPublic)!;
        var button = (FrameworkElement)dialog.Template.FindName("PrimaryButton", dialog);
        var column = (ColumnDefinition)dialog.Template.FindName("PrimaryColumn", dialog);
        void Check(bool enabled)
        {
            apply.Invoke(null, [dialog, state]);
            Assert.AreEqual(Visibility.Visible, button.Visibility);
            Assert.IsTrue(column.Width.IsStar);
            Assert.AreEqual(enabled, button.IsEnabled);
            Assert.AreEqual(enabled, state.CanClose(DialogResult.Primary));
        }
        Check(false);
        preview.CanExecute = true;
        Check(false);
        state.Reason = "测试作废";
        Check(true);
        preview.Blockers.Add("存在未处理入库");
        Check(false);
        preview.Blockers.Clear();
        state.Reason = " ";
        Check(false);
        dialog.PrimaryButtonText = string.Empty;
        apply.Invoke(null, [dialog, state]);
        Assert.AreEqual(Visibility.Collapsed, button.Visibility);
        Assert.AreEqual(0d, column.Width.Value);
        return Task.CompletedTask;
    });
}
