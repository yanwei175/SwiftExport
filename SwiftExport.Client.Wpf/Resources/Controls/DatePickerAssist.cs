using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 为原生 <see cref="DatePicker"/> 提供可绑定的占位文本，不替换其 Fluent 控件模板。
/// </summary>
public static class DatePickerAssist
{
    private static readonly DependencyProperty PlaceholderSessionProperty =
        DependencyProperty.RegisterAttached(
            "PlaceholderSession",
            typeof(PlaceholderSession),
            typeof(DatePickerAssist),
            new PropertyMetadata(null));

    /// <summary>
    /// 标识原生 DatePicker 在未选择日期时显示的占位文本。
    /// </summary>
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.RegisterAttached(
            "PlaceholderText",
            typeof(string),
            typeof(DatePickerAssist),
            new PropertyMetadata(null, OnPlaceholderTextChanged));

    /// <summary>
    /// 获取 DatePicker 的占位文本。
    /// </summary>
    /// <param name="element">目标 DatePicker。</param>
    /// <returns>自定义占位文本；未设置时为 <see langword="null"/>。</returns>
    public static string? GetPlaceholderText(DependencyObject element) =>
        (string?)element.GetValue(PlaceholderTextProperty);

    /// <summary>
    /// 设置 DatePicker 的占位文本；传入 <see langword="null"/> 时恢复原生本地化文本。
    /// </summary>
    /// <param name="element">目标 DatePicker。</param>
    /// <param name="value">要显示的占位文本。</param>
    public static void SetPlaceholderText(DependencyObject element, string? value) =>
        element.SetValue(PlaceholderTextProperty, value);

    private static void OnPlaceholderTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DatePicker datePicker)
        {
            return;
        }

        var session = (PlaceholderSession?)datePicker.GetValue(PlaceholderSessionProperty);
        if (e.NewValue is null)
        {
            session?.Dispose();
            datePicker.ClearValue(PlaceholderSessionProperty);
            return;
        }

        if (session is null)
        {
            session = new PlaceholderSession(datePicker);
            datePicker.SetValue(PlaceholderSessionProperty, session);
        }

        session.ScheduleApply();
    }

    /// <summary>
    /// 管理 DatePicker 模板水印的生命周期，并在日期状态变化后重新应用自定义文本。
    /// </summary>
    private sealed class PlaceholderSession : IDisposable
    {
        private readonly DatePicker _datePicker;
        private DispatcherOperation? _pendingApply;
        private ContentControl? _watermark;
        private DatePickerTextBox? _fallbackTextBox;
        private AdornerLayer? _fallbackAdornerLayer;
        private PlaceholderAdorner? _fallbackAdorner;
        private object? _originalContent;
        private bool _hasOriginalContent;

        /// <summary>
        /// 创建会跟随目标 DatePicker 加载与日期变化的水印会话。
        /// </summary>
        public PlaceholderSession(DatePicker datePicker)
        {
            _datePicker = datePicker;
            _datePicker.Loaded += OnLoaded;
            _datePicker.Unloaded += OnUnloaded;
            _datePicker.SelectedDateChanged += OnSelectedDateChanged;
        }

        /// <summary>
        /// 在原生模板完成当前一轮更新后应用占位文本。
        /// </summary>
        public void ScheduleApply()
        {
            if (!_datePicker.IsLoaded)
            {
                return;
            }

            if (_pendingApply is { Status: DispatcherOperationStatus.Pending })
            {
                _pendingApply.Abort();
            }

            // 等原生 DatePicker 完成模板和 Watermark 更新后再覆盖显示文本。
            _pendingApply = _datePicker.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(ApplyPlaceholder));
        }

        /// <summary>
        /// 解除事件并恢复创建会话前的原生水印文本。
        /// </summary>
        public void Dispose()
        {
            _datePicker.Loaded -= OnLoaded;
            _datePicker.Unloaded -= OnUnloaded;
            _datePicker.SelectedDateChanged -= OnSelectedDateChanged;
            CancelPendingApply();
            RestoreOriginalContent();
            _watermark = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => ScheduleApply();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CancelPendingApply();
            RemoveFallbackAdorner();
            _watermark = null;
            _originalContent = null;
            _hasOriginalContent = false;
        }

        private void OnSelectedDateChanged(
            object? sender,
            SelectionChangedEventArgs e) => ScheduleApply();

        private void ApplyPlaceholder()
        {
            _pendingApply = null;
            var placeholderText = GetPlaceholderText(_datePicker);
            if (placeholderText is null)
            {
                return;
            }

            var textBox = FindDatePickerTextBox(_datePicker);
            if (textBox is null)
            {
                return;
            }

            var watermark = FindWatermark(textBox);
            if (watermark is null)
            {
                ApplyFallbackPlaceholder(textBox, placeholderText);
                return;
            }

            RemoveFallbackAdorner();

            if (!ReferenceEquals(_watermark, watermark))
            {
                _watermark = watermark;
                _originalContent = watermark.Content;
                _hasOriginalContent = true;
            }

            // SetCurrentValue 保留官方模板建立的值来源，避免替换整个 DatePickerTextBox 模板。
            watermark.SetCurrentValue(ContentControl.ContentProperty, placeholderText);
        }

        private void RestoreOriginalContent()
        {
            if (_watermark is not null && _hasOriginalContent)
            {
                _watermark.SetCurrentValue(ContentControl.ContentProperty, _originalContent);
            }

            RemoveFallbackAdorner();
        }

        private void CancelPendingApply()
        {
            if (_pendingApply is { Status: DispatcherOperationStatus.Pending })
            {
                _pendingApply.Abort();
            }

            _pendingApply = null;
        }

        private static DatePickerTextBox? FindDatePickerTextBox(DatePicker datePicker)
        {
            datePicker.ApplyTemplate();
            return datePicker.Template.FindName("PART_TextBox", datePicker) as DatePickerTextBox;
        }

        private static ContentControl? FindWatermark(DatePickerTextBox textBox)
        {
            textBox.ApplyTemplate();
            if (textBox.Template.FindName("PART_Watermark", textBox) is ContentControl watermark)
            {
                return watermark;
            }

            return FindWatermarkDescendant(textBox);
        }

        /// <summary>当 Framework Fluent 模板不提供 Watermark 节点时，以只读 Adorner 呈现占位文本。</summary>
        private void ApplyFallbackPlaceholder(DatePickerTextBox textBox, string placeholderText)
        {
            if (!ReferenceEquals(_fallbackTextBox, textBox))
            {
                RemoveFallbackAdorner();
                _fallbackTextBox = textBox;
                _fallbackTextBox.TextChanged += OnFallbackTextChanged;
            }

            var adornerLayer = AdornerLayer.GetAdornerLayer(textBox);
            if (adornerLayer is null)
            {
                return;
            }

            if (_fallbackAdorner is null || !ReferenceEquals(_fallbackAdornerLayer, adornerLayer))
            {
                RemoveFallbackAdorner(keepTextBox: true);
                _fallbackAdornerLayer = adornerLayer;
                _fallbackAdorner = new PlaceholderAdorner(textBox);
                adornerLayer.Add(_fallbackAdorner);
            }

            _fallbackAdorner.Text = placeholderText;
            UpdateFallbackVisibility();
        }

        private void OnFallbackTextChanged(object sender, TextChangedEventArgs e) => UpdateFallbackVisibility();

        private void UpdateFallbackVisibility()
        {
            if (_fallbackAdorner is null || _fallbackTextBox is null)
            {
                return;
            }

            _fallbackAdorner.Visibility = _datePicker.SelectedDate is null && string.IsNullOrEmpty(_fallbackTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>移除 Framework 模板的回退占位层并解除 TextChanged 订阅。</summary>
        private void RemoveFallbackAdorner(bool keepTextBox = false)
        {
            if (_fallbackAdorner is not null && _fallbackAdornerLayer is not null)
            {
                _fallbackAdornerLayer.Remove(_fallbackAdorner);
            }

            _fallbackAdorner = null;
            _fallbackAdornerLayer = null;
            if (!keepTextBox && _fallbackTextBox is not null)
            {
                _fallbackTextBox.TextChanged -= OnFallbackTextChanged;
                _fallbackTextBox = null;
            }
        }

        /// <summary>兼容 .NET Framework 与不同 Fluent 模板中的水印命名差异。</summary>
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

        /// <summary>在不接管 DatePickerTextBox 模板的情况下绘制回退占位文本。</summary>
        private sealed class PlaceholderAdorner : Adorner
        {
            private readonly TextBlock _textBlock;

            public PlaceholderAdorner(UIElement adornedElement)
                : base(adornedElement)
            {
                IsHitTestVisible = false;
                _textBlock = new TextBlock
                {
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = SystemColors.GrayTextBrush
                };
                AddVisualChild(_textBlock);
            }

            public string Text
            {
                get => _textBlock.Text;
                set => _textBlock.Text = value;
            }

            protected override int VisualChildrenCount => 1;

            protected override Visual GetVisualChild(int index) =>
                index == 0 ? _textBlock : throw new ArgumentOutOfRangeException(nameof(index));

            protected override Size MeasureOverride(Size constraint)
            {
                _textBlock.Measure(constraint);
                // 可用空间允许为无限值，但 WPF 要求元素返回有限的期望尺寸。
                return _textBlock.DesiredSize;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                _textBlock.Arrange(new Rect(finalSize));
                return finalSize;
            }
        }
    }
}
