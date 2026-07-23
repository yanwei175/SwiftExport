using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 为紧凑 TextBox 注入一个覆盖宿主的多行编辑 Popup。
/// </summary>
public static class PopupTextBoxAssist
{
    private static readonly DependencyProperty SessionProperty =
        DependencyProperty.RegisterAttached(
            "Session",
            typeof(PopupSession),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty PopupHeightProperty =
        DependencyProperty.RegisterAttached(
            "PopupHeight",
            typeof(double),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(180d, OnPopupHeightChanged),
            IsValidPopupHeight);

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    public static double GetPopupHeight(DependencyObject obj) =>
        (double)obj.GetValue(PopupHeightProperty);

    public static void SetPopupHeight(DependencyObject obj, double value) =>
        obj.SetValue(PopupHeightProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            textBox.GotKeyboardFocus += OnHostGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += OnHostPreviewMouseLeftButtonDown;
            textBox.Loaded += OnHostLoaded;
            textBox.SizeChanged += OnHostSizeChanged;
            textBox.Unloaded += OnHostUnloaded;
            RequestOpen(textBox);

            return;
        }

        textBox.GotKeyboardFocus -= OnHostGotKeyboardFocus;
        textBox.PreviewMouseLeftButtonDown -= OnHostPreviewMouseLeftButtonDown;
        textBox.Loaded -= OnHostLoaded;
        textBox.SizeChanged -= OnHostSizeChanged;
        textBox.Unloaded -= OnHostUnloaded;
        DisposeSession(textBox);
    }

    private static void OnPopupHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            GetSession(textBox)?.UpdateSize();
        }
    }

    private static bool IsValidPopupHeight(object value) =>
        value is double height && double.IsFinite(height) && height > 0;

    private static void OnHostGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !GetIsEnabled(textBox) ||
            textBox.IsReadOnly ||
            !textBox.IsEnabled)
        {
            return;
        }

        if (GetOrCreateSession(textBox).ConsumeSuppressNextOpen())
        {
            return;
        }

        RequestOpen(textBox);
    }

    private static void OnHostPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox &&
            GetIsEnabled(textBox) &&
            !textBox.IsReadOnly &&
            textBox.IsEnabled)
        {
            RequestOpen(textBox);
        }
    }

    private static void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            GetSession(textBox)?.UpdateSize();
        }
    }

    private static void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.IsKeyboardFocusWithin)
        {
            RequestOpen(textBox);
        }
    }

    private static void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            DisposeSession(textBox);
        }
    }

    private static void RequestOpen(TextBox textBox)
    {
        textBox.Dispatcher.BeginInvoke(
            () => OpenPopup(textBox),
            DispatcherPriority.Input);
    }

    private static void OpenPopup(TextBox textBox)
    {
        if (!GetIsEnabled(textBox) ||
            !textBox.IsLoaded ||
            textBox.IsReadOnly ||
            !textBox.IsEnabled ||
            !textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        GetOrCreateSession(textBox).Open();
    }

    private static PopupSession GetOrCreateSession(TextBox textBox)
    {
        var session = GetSession(textBox);
        if (session is not null)
        {
            return session;
        }

        session = new PopupSession(textBox);
        textBox.SetValue(SessionProperty, session);
        return session;
    }

    private static PopupSession? GetSession(DependencyObject obj) =>
        (PopupSession?)obj.GetValue(SessionProperty);

    private static void DisposeSession(TextBox textBox)
    {
        var session = GetSession(textBox);
        if (session is null)
        {
            return;
        }

        session.Dispose();
        textBox.ClearValue(SessionProperty);
    }

    private sealed class PopupSession : IDisposable
    {
        private readonly TextBox _host;
        private readonly TextBox _editor;
        private readonly Border _border;
        private readonly Popup _popup;
        private bool _isDisposed;
        private bool _suppressNextOpen;

        public PopupSession(TextBox host)
        {
            _host = host;
            _editor = CreateEditor(host);
            _border = CreateBorder(_editor);
            _popup = new Popup
            {
                PlacementTarget = host,
                Placement = PlacementMode.Relative,
                HorizontalOffset = 0,
                VerticalOffset = 0,
                StaysOpen = false,
                AllowsTransparency = true,
                Focusable = false,
                Child = _border,
            };

            _editor.PreviewKeyDown += OnEditorPreviewKeyDown;
            _editor.LostKeyboardFocus += OnEditorLostKeyboardFocus;
            _popup.Closed += OnPopupClosed;
        }

        public bool ConsumeSuppressNextOpen()
        {
            if (!_suppressNextOpen)
            {
                return false;
            }

            _suppressNextOpen = false;
            return true;
        }

        public void Open()
        {
            if (_isDisposed || _host.IsReadOnly || !_host.IsEnabled)
            {
                return;
            }

            UpdateSize();
            _popup.IsOpen = true;
            _editor.Dispatcher.BeginInvoke(
                () =>
                {
                    if (_isDisposed || !_popup.IsOpen)
                    {
                        return;
                    }

                    _editor.Focus();
                    _editor.CaretIndex = _editor.Text?.Length ?? 0;
                },
                DispatcherPriority.Input);
        }

        public void UpdateSize()
        {
            if (_isDisposed)
            {
                return;
            }

            _border.Width = Math.Max(0, _host.ActualWidth);
            _border.Height = GetPopupHeight(_host);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _editor.PreviewKeyDown -= OnEditorPreviewKeyDown;
            _editor.LostKeyboardFocus -= OnEditorLostKeyboardFocus;
            _popup.Closed -= OnPopupClosed;
            _popup.IsOpen = false;
            BindingOperations.ClearAllBindings(_editor);
            _popup.Child = null;
            _popup.PlacementTarget = null;
        }

        private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape &&
                (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                return;
            }

            CloseAndRestoreHostFocus();
            e.Handled = true;
        }

        private void OnEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is DependencyObject newFocus &&
                IsDescendantOrSelf(newFocus, _border))
            {
                return;
            }

            _popup.IsOpen = false;
        }

        private void OnPopupClosed(object? sender, EventArgs e)
        {
            if (!_host.IsLoaded)
            {
                DisposeSession(_host);
            }
        }

        private void CloseAndRestoreHostFocus()
        {
            if (_isDisposed)
            {
                return;
            }

            _suppressNextOpen = true;
            _popup.IsOpen = false;
            if (!_host.Focus())
            {
                _suppressNextOpen = false;
            }
        }

        private static TextBox CreateEditor(TextBox host)
        {
            var editor = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(8),
                MinHeight = 0,
            };

            editor.SetBinding(TextBox.TextProperty, new Binding(nameof(TextBox.Text))
            {
                Source = host,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            editor.SetBinding(TextBox.MaxLengthProperty, new Binding(nameof(TextBox.MaxLength))
            {
                Source = host,
                Mode = BindingMode.OneWay,
            });
            editor.SetBinding(TextBox.IsReadOnlyProperty, new Binding(nameof(TextBox.IsReadOnly))
            {
                Source = host,
                Mode = BindingMode.OneWay,
            });

            return editor;
        }

        private static Border CreateBorder(TextBox editor)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Child = editor,
                SnapsToDevicePixels = true,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.18,
                    Color = Colors.Black,
                },
            };

            border.SetResourceReference(Border.BackgroundProperty, "SwiftExportBrush.Surface");
            border.SetResourceReference(Border.BorderBrushProperty, "SwiftExportBrush.Border");
            border.SetResourceReference(Border.CornerRadiusProperty, "SwiftExportCornerRadius");
            return border;
        }

        private static bool IsDescendantOrSelf(DependencyObject current, DependencyObject ancestor)
        {
            for (DependencyObject? node = current; node is not null; node = GetParent(node))
            {
                if (ReferenceEquals(node, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        private static DependencyObject? GetParent(DependencyObject current)
        {
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }

            return LogicalTreeHelper.GetParent(current);
        }
    }
}
