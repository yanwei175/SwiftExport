using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Controls;

public static class PopupTextBoxAssist
{
    private static readonly DependencyProperty PopupInstanceProperty =
        DependencyProperty.RegisterAttached(
            "PopupInstance",
            typeof(Popup),
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
            new PropertyMetadata(180d));

    public static readonly DependencyProperty PopupWidthProperty =
        DependencyProperty.RegisterAttached(
            "PopupWidth",
            typeof(double),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty PopupMinWidthProperty =
        DependencyProperty.RegisterAttached(
            "PopupMinWidth",
            typeof(double),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(240d));

    public static readonly DependencyProperty OpenOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "OpenOnFocus",
            typeof(bool),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(true));

    public static readonly DependencyProperty OpenOnClickProperty =
        DependencyProperty.RegisterAttached(
            "OpenOnClick",
            typeof(bool),
            typeof(PopupTextBoxAssist),
            new PropertyMetadata(true));

    public static bool GetIsEnabled(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject obj, bool value)
    {
        obj.SetValue(IsEnabledProperty, value);
    }

    public static double GetPopupHeight(DependencyObject obj)
    {
        return (double)obj.GetValue(PopupHeightProperty);
    }

    public static void SetPopupHeight(DependencyObject obj, double value)
    {
        obj.SetValue(PopupHeightProperty, value);
    }

    public static double GetPopupWidth(DependencyObject obj)
    {
        return (double)obj.GetValue(PopupWidthProperty);
    }

    public static void SetPopupWidth(DependencyObject obj, double value)
    {
        obj.SetValue(PopupWidthProperty, value);
    }

    public static double GetPopupMinWidth(DependencyObject obj)
    {
        return (double)obj.GetValue(PopupMinWidthProperty);
    }

    public static void SetPopupMinWidth(DependencyObject obj, double value)
    {
        obj.SetValue(PopupMinWidthProperty, value);
    }

    public static bool GetOpenOnFocus(DependencyObject obj)
    {
        return (bool)obj.GetValue(OpenOnFocusProperty);
    }

    public static void SetOpenOnFocus(DependencyObject obj, bool value)
    {
        obj.SetValue(OpenOnFocusProperty, value);
    }

    public static bool GetOpenOnClick(DependencyObject obj)
    {
        return (bool)obj.GetValue(OpenOnClickProperty);
    }

    public static void SetOpenOnClick(DependencyObject obj, bool value)
    {
        obj.SetValue(OpenOnClickProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            textBox.PreviewKeyDown += OnCompactTextBoxPreviewKeyDown;
            textBox.Unloaded += OnUnloaded;
            return;
        }

        textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
        textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        textBox.PreviewKeyDown -= OnCompactTextBoxPreviewKeyDown;
        textBox.Unloaded -= OnUnloaded;
        ClosePopup(textBox);
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox && GetOpenOnFocus(textBox) && !textBox.IsReadOnly)
        {
            OpenPopup(textBox);
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && GetOpenOnClick(textBox) && !textBox.IsReadOnly)
        {
            OpenPopup(textBox);
        }
    }

    private static void OnCompactTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && e.Key == Key.F4 && !textBox.IsReadOnly)
        {
            OpenPopup(textBox);
            e.Handled = true;
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ClosePopup(textBox);
        }
    }

    private static void OpenPopup(TextBox textBox)
    {
        var popup = GetPopupInstance(textBox);
        if (popup is null)
        {
            popup = CreatePopup(textBox);
            SetPopupInstance(textBox, popup);
        }

        UpdatePopupSize(textBox, popup);
        popup.IsOpen = true;

        if (popup.Child is Border { Child: TextBox editor })
        {
            editor.Dispatcher.BeginInvoke(() =>
            {
                editor.Focus();
                editor.CaretIndex = editor.Text?.Length ?? 0;
            }, DispatcherPriority.Input);
        }
    }

    private static Popup CreatePopup(TextBox textBox)
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
            Source = textBox,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        editor.SetBinding(TextBox.MaxLengthProperty, new Binding(nameof(TextBox.MaxLength))
        {
            Source = textBox,
            Mode = BindingMode.OneWay,
        });
        editor.SetBinding(TextBox.IsReadOnlyProperty, new Binding(nameof(TextBox.IsReadOnly))
        {
            Source = textBox,
            Mode = BindingMode.OneWay,
        });

        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                ClosePopup(textBox);
                textBox.Focus();
                e.Handled = true;
            }
        };

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
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

        var popup = new Popup
        {
            PlacementTarget = textBox,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = false,
            Child = border,
        };

        popup.Closed += (_, _) => textBox.ClearValue(PopupInstanceProperty);
        return popup;
    }

    private static void UpdatePopupSize(TextBox textBox, Popup popup)
    {
        if (popup.Child is not Border { Child: TextBox editor } border)
        {
            return;
        }

        var configuredWidth = GetPopupWidth(textBox);
        var width = double.IsNaN(configuredWidth)
            ? Math.Max(GetPopupMinWidth(textBox), textBox.ActualWidth)
            : configuredWidth;

        border.Width = width;
        border.Height = GetPopupHeight(textBox);
        editor.Height = Math.Max(0, GetPopupHeight(textBox) - 8);
    }

    private static Popup? GetPopupInstance(DependencyObject obj)
    {
        return (Popup?)obj.GetValue(PopupInstanceProperty);
    }

    private static void SetPopupInstance(DependencyObject obj, Popup? value)
    {
        obj.SetValue(PopupInstanceProperty, value);
    }

    private static void ClosePopup(TextBox textBox)
    {
        var popup = GetPopupInstance(textBox);
        if (popup is null)
        {
            return;
        }

        popup.IsOpen = false;
        SetPopupInstance(textBox, null);
    }
}
