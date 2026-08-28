using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>
/// 验证 DatePicker 占位文本注入与原生模板状态变化的兼容性。
/// </summary>
[TestClass]
public sealed class DatePickerAssistTests
{
    /// <summary>
    /// 验证自定义文本可覆盖并在清除属性后恢复原生水印。
    /// </summary>
    [TestMethod]
    public void PlaceholderText_ShouldReplaceAndRestoreNativeWatermark()
    {
        StaTest.RunAsync(async () =>
        {
            var datePicker = new DatePicker();
            var window = CreateWindow(datePicker);
            try
            {
                window.Show();
                datePicker.UpdateLayout();
                var originalWatermark = FindNativeWatermark(datePicker);
                var originalContent = originalWatermark?.Content;

                DatePickerAssist.SetPlaceholderText(datePicker, "原需求交期到");
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.AreEqual("原需求交期到", FindPlaceholderText(datePicker));

                DatePickerAssist.SetPlaceholderText(datePicker, null);
                if (originalWatermark is not null)
                {
                    Assert.AreEqual(originalContent, originalWatermark.Content);
                }
                else
                {
                    Assert.IsNull(FindFallbackPlaceholder(datePicker));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 验证日期选中状态变化不会覆盖宿主配置的占位文本。
    /// </summary>
    [TestMethod]
    public void PlaceholderText_ShouldRemainAfterSelectedDateChanges()
    {
        StaTest.RunAsync(async () =>
        {
            var datePicker = new DatePicker();
            DatePickerAssist.SetPlaceholderText(datePicker, "计划交期从");
            var window = CreateWindow(datePicker);
            try
            {
                window.Show();
                await DrainDispatcherAsync(datePicker.Dispatcher);

                datePicker.SelectedDate = new DateTime(2026, 8, 27);
                datePicker.SelectedDate = null;
                await DrainDispatcherAsync(datePicker.Dispatcher);

                Assert.AreEqual("计划交期从", FindPlaceholderText(datePicker));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateWindow(DatePicker datePicker) =>
        new()
        {
            Width = 320,
            Height = 120,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = datePicker,
        };

    private static string? FindPlaceholderText(DatePicker datePicker) =>
        FindNativeWatermark(datePicker)?.Content?.ToString() ??
        FindFallbackPlaceholder(datePicker)?.Text;

    private static ContentControl? FindNativeWatermark(DatePicker datePicker)
    {
        datePicker.ApplyTemplate();
        var textBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        Assert.IsNotNull(textBox);
        textBox.ApplyTemplate();
        return textBox.Template.FindName("PART_Watermark", textBox) as ContentControl ??
               FindWatermarkDescendant(textBox);
    }

    private static TextBlock? FindFallbackPlaceholder(DatePicker datePicker)
    {
        datePicker.ApplyTemplate();
        var textBox = datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        Assert.IsNotNull(textBox);
        var layer = AdornerLayer.GetAdornerLayer(textBox);
        return layer?.GetAdorners(textBox)?
            .SelectMany(adorner => FindVisualDescendants<TextBlock>(adorner))
            .FirstOrDefault();
    }

    private static ContentControl? FindWatermarkDescendant(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is ContentControl contentControl &&
                (contentControl.Name.IndexOf("watermark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 contentControl.Name.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return contentControl;
            }

            var nested = FindWatermarkDescendant(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static async Task DrainDispatcherAsync(Dispatcher dispatcher)
    {
        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
    }
}
