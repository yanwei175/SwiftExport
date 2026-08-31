using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>
/// 验证 DatePicker 占位文本注入与原生模板状态变化的兼容性。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DatePickerAssistTests
{
    // WPF 每个进程只允许一个 Application；使用持续存活的 STA，避免资源归属已退出的测试线程。
    private static readonly Lazy<Dispatcher> ApplicationDispatcher = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            try
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    /// <summary>在同一 STA Application 上解析跨字典资源，每次测试后清空资源以免污染其他控件测试。</summary>
    private static void RunWithApplication(Func<Task> action)
    {
        ApplicationDispatcher.Value.InvokeAsync(async () =>
        {
            try
            {
                await action();
            }
            finally
            {
                Application.Current.Resources = new ResourceDictionary();
            }
        }).Task.Unwrap().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 验证自定义文本可覆盖并在清除属性后恢复原生水印。
    /// </summary>
    [TestMethod]
    [DataRow("SwiftExportDatePicker")]
    [DataRow("SwiftExportDataGridDatePicker")]
    public void PlaceholderText_ShouldReplaceAndRestoreNativeWatermark(string styleKey)
    {
        RunWithApplication(async () =>
        {
            var datePicker = new DatePicker();
            var window = CreateWindow(datePicker, styleKey);
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
    [DataRow("SwiftExportDatePicker")]
    [DataRow("SwiftExportDataGridDatePicker")]
    public void PlaceholderText_ShouldRemainAfterSelectedDateChanges(string styleKey)
    {
        RunWithApplication(async () =>
        {
            var datePicker = new DatePicker();
            DatePickerAssist.SetPlaceholderText(datePicker, "计划交期从");
            var window = CreateWindow(datePicker, styleKey);
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

    /// <summary>
    /// 验证混合主题中日期框的按下状态、弹出日历、日期输入和禁用状态，不允许产生绑定错误。
    /// </summary>
    [TestMethod]
    [DataRow("SwiftExportDatePicker")]
    [DataRow("SwiftExportDataGridDatePicker")]
    public void FluentDatePicker_ShouldSupportInteractionWithoutBindingErrors(string styleKey)
    {
        RunWithApplication(async () =>
        {
            var datePicker = new DatePicker();
            var window = CreateWindow(datePicker, styleKey);
            using var output = new StringWriter();
            using var listener = new TextWriterTraceListener(output);
            var trace = PresentationTraceSources.DataBindingSource;
            var previousLevel = trace.Switch.Level;
            trace.Switch.Level = SourceLevels.Error;
            trace.Listeners.Add(listener);
            try
            {
                window.Show();
                await DrainDispatcherAsync(datePicker.Dispatcher);
                var button = (Button)datePicker.Template.FindName("PART_Button", datePicker);
                var textBox = (DatePickerTextBox)datePicker.Template.FindName("PART_TextBox", datePicker);

                // 直接驱动只读输入状态以覆盖鼠标悬停且按下的模板触发器，不移动用户的真实鼠标。
                SetInputState(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
                SetInputState(button, typeof(ButtonBase), "IsPressedPropertyKey", true);
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.IsTrue(button.IsPressed);
                SetInputState(button, typeof(ButtonBase), "IsPressedPropertyKey", false);
                SetInputState(button, typeof(UIElement), "IsMouseOverPropertyKey", false);

                var peer = new ButtonAutomationPeer(button);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.IsTrue(datePicker.IsDropDownOpen);
                var popup = (Popup)datePicker.Template.FindName("PART_Popup", datePicker);
                var calendar = (System.Windows.Controls.Calendar)popup.Child;
                var selectedDate = new DateTime(2026, 8, 27);
                calendar.SelectedDate = selectedDate;
                Assert.AreEqual(selectedDate, datePicker.SelectedDate);
                Assert.IsNotNull(calendar.Template);
                datePicker.IsDropDownOpen = false;
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.IsFalse(popup.IsOpen);

                button.Focus();
                var source = PresentationSource.FromVisual(button)!;
                button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                });
                button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space)
                {
                    RoutedEvent = Keyboard.KeyUpEvent
                });
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.IsTrue(datePicker.IsDropDownOpen, "日历按钮应支持空格键打开日历。");
                var dayButton = FindVisualDescendants<CalendarDayButton>(calendar)
                    .First(day => day.IsVisible && day.IsEnabled);
                dayButton.Focus();
                dayButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(dayButton)!, Environment.TickCount, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.IsFalse(datePicker.IsDropDownOpen, "Esc 应关闭日历。");

                var typedDate = selectedDate.AddDays(1);
                datePicker.Text = typedDate.ToString("d", CultureInfo.CurrentCulture);
                await DrainDispatcherAsync(datePicker.Dispatcher);
                Assert.AreEqual(typedDate, datePicker.SelectedDate);
                Assert.IsFalse(string.IsNullOrWhiteSpace(textBox.Text));
                datePicker.IsEnabled = false;
                Assert.IsFalse(button.IsEnabled);
                Assert.IsFalse(textBox.IsEnabled);
                listener.Flush();
                Assert.AreEqual(string.Empty, output.ToString(), "日期控件交互不应产生绑定错误。");
            }
            finally
            {
                window.Close();
                trace.Listeners.Remove(listener);
                trace.Switch.Level = previousLevel;
            }
        });
    }

    /// <summary>
    /// 验证日期控件内的微软资源不会覆盖同级普通按钮或 WPF-UI 按钮的主题。
    /// </summary>
    [TestMethod]
    public void FluentDatePicker_ShouldNotChangeSiblingButtonStyles()
    {
        RunWithApplication(async () =>
        {
            var datePicker = new DatePicker();
            var window = CreateWindow(datePicker, "SwiftExportDatePicker");
            var nativeButton = new Button { Content = "普通按钮" };
            var uiButton = new global::Wpf.Ui.Controls.Button { Content = "WPF-UI 按钮" };
            var panel = new StackPanel();
            panel.Children.Add(nativeButton);
            panel.Children.Add(uiButton);
            window.Content = panel;
            try
            {
                window.Show();
                await DrainDispatcherAsync(window.Dispatcher);
                var nativeStyle = nativeButton.Style;
                var uiStyle = uiButton.Style;
                var nativeTemplate = nativeButton.Template;
                var uiTemplate = uiButton.Template;
                Assert.IsNotNull(nativeTemplate);
                Assert.IsNotNull(uiTemplate);
                panel.Children.Add(datePicker);
                await DrainDispatcherAsync(window.Dispatcher);
                Assert.AreSame(nativeStyle, nativeButton.Style);
                Assert.AreSame(uiStyle, uiButton.Style);
                Assert.AreSame(nativeTemplate, nativeButton.Template);
                Assert.AreSame(uiTemplate, uiButton.Template);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>使用 WPF 的只读依赖属性键模拟输入状态，仅用于验证真实模板的状态触发器。</summary>
    private static void SetInputState(Button button, Type owner, string fieldName, bool value)
    {
        var field = owner.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        button.SetValue((DependencyPropertyKey)field.GetValue(null)!, value);
    }

    /// <summary>从实际 App.xaml 提取资源，避免测试另写一份主题顺序或启动应用的网络服务。</summary>
    private static Window CreateWindow(DatePicker datePicker, string styleKey)
    {
        using var stream = typeof(DatePickerAssistTests).Assembly
            .GetManifestResourceStream("DatePickerTests.App.xaml");
        Assert.IsNotNull(stream);
        var application = XDocument.Load(stream).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(application.Element(presentation + "Application.Resources")!.Elements().Single());
        foreach (var element in dictionary.DescendantsAndSelf())
        {
            var namespaceName = element.Name.NamespaceName;
            if (namespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal) &&
                !namespaceName.Contains(";assembly=", StringComparison.Ordinal))
            {
                element.Name = XName.Get(element.Name.LocalName, namespaceName + ";assembly=SwiftExport.Client.Wpf");
            }
        }

        foreach (var attribute in application.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            var namespaceValue = attribute.Value;
            if (namespaceValue.StartsWith("clr-namespace:", StringComparison.Ordinal) &&
                !namespaceValue.Contains(";assembly=", StringComparison.Ordinal))
            {
                namespaceValue += ";assembly=SwiftExport.Client.Wpf";
            }

            dictionary.SetAttributeValue(attribute.Name, namespaceValue);
        }

        var resources = new ResourceDictionary();
        Application.Current.Resources = resources;
        // 与应用启动一致，按顺序立即注册字典，使后续 BAML 的 StaticResource 能找到前面的命名样式。
        foreach (var mergedDictionary in dictionary.Element(presentation + "ResourceDictionary.MergedDictionaries")!.Elements())
        {
            var wrapper = new XElement(presentation + "ResourceDictionary", dictionary.Attributes(),
                new XElement(presentation + "ResourceDictionary.MergedDictionaries", new XElement(mergedDictionary)));
            var loaded = (ResourceDictionary)XamlReader.Parse(wrapper.ToString(), new ParserContext
            {
                BaseUri = new Uri("pack://application:,,,/SwiftExport.Client.Wpf;component/")
            });
            resources.MergedDictionaries.Add(loaded.MergedDictionaries.Single());
        }

        dictionary.Element(presentation + "ResourceDictionary.MergedDictionaries")!.Remove();
        var directResources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        foreach (var key in directResources.Keys)
        {
            resources.Add(key, directResources[key]);
        }

        var window = new Window
        {
            Width = 320,
            Height = 120,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Resources = resources,
            Content = datePicker,
        };
        datePicker.Style = (Style)window.FindResource(styleKey);
        return window;
    }

    /// <summary>读取模板水印或回退层中的实际占位文本，兼容独立着色的 TextBlock 内容。</summary>
    private static string? FindPlaceholderText(DatePicker datePicker) =>
        FindNativeWatermark(datePicker)?.Content is TextBlock textBlock
            ? textBlock.Text
            : FindNativeWatermark(datePicker)?.Content?.ToString() ?? FindFallbackPlaceholder(datePicker)?.Text;

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

    /// <summary>等待 Loaded、数据绑定及 Automation 的输入优先级回调完成。</summary>
    private static async Task DrainDispatcherAsync(Dispatcher dispatcher)
    {
        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }
}
