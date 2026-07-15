using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SwiftExportClient.Resources.Controls;

public enum DrawerDisplayMode
{
    Push,
    Overlay,
}

/// <summary>承载任意右侧业务内容的轻量宿主；业务关闭确认由绑定命令负责。</summary>
public partial class RightDrawerHost : UserControl
{
    public static readonly DependencyProperty MainContentProperty = DependencyProperty.Register(
        nameof(MainContent), typeof(object), typeof(RightDrawerHost));

    public static readonly DependencyProperty MainContentTemplateProperty = DependencyProperty.Register(
        nameof(MainContentTemplate), typeof(DataTemplate), typeof(RightDrawerHost));

    public static readonly DependencyProperty DrawerContentProperty = DependencyProperty.Register(
        nameof(DrawerContent), typeof(object), typeof(RightDrawerHost));

    public static readonly DependencyProperty DrawerContentTemplateProperty = DependencyProperty.Register(
        nameof(DrawerContentTemplate), typeof(DataTemplate), typeof(RightDrawerHost));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen),
        typeof(bool),
        typeof(RightDrawerHost),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MinDrawerWidthProperty = DependencyProperty.Register(
        nameof(MinDrawerWidth),
        typeof(double),
        typeof(RightDrawerHost),
        new PropertyMetadata(360d, OnDrawerConstraintChanged),
        value => value is double width && double.IsFinite(width) && width >= 0);

    public static readonly DependencyProperty MaxDrawerWidthProperty = DependencyProperty.Register(
        nameof(MaxDrawerWidth),
        typeof(double),
        typeof(RightDrawerHost),
        new PropertyMetadata(double.PositiveInfinity, OnDrawerConstraintChanged),
        value => value is double width && (double.IsPositiveInfinity(width) || double.IsFinite(width) && width >= 0));

    public static readonly DependencyProperty DrawerWidthProperty = DependencyProperty.Register(
        nameof(DrawerWidth),
        typeof(double),
        typeof(RightDrawerHost),
        new FrameworkPropertyMetadata(520d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutPropertyChanged, CoerceDrawerWidth),
        value => value is double width && double.IsFinite(width) && width >= 0);

    public static readonly DependencyProperty IsResizableProperty = DependencyProperty.Register(
        nameof(IsResizable),
        typeof(bool),
        typeof(RightDrawerHost),
        new PropertyMetadata(true, OnLayoutPropertyChanged));

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode),
        typeof(DrawerDisplayMode),
        typeof(RightDrawerHost),
        new PropertyMetadata(DrawerDisplayMode.Overlay, OnLayoutPropertyChanged));

    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(
        nameof(CloseCommand), typeof(ICommand), typeof(RightDrawerHost));

    public static readonly DependencyProperty CloseOnEscapeProperty = DependencyProperty.Register(
        nameof(CloseOnEscape),
        typeof(bool),
        typeof(RightDrawerHost),
        new PropertyMetadata(false));

    public RightDrawerHost()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateLayoutState();
        SizeChanged += (_, _) => CoerceValue(DrawerWidthProperty);
    }

    public object? MainContent { get => GetValue(MainContentProperty); set => SetValue(MainContentProperty, value); }
    public DataTemplate? MainContentTemplate { get => (DataTemplate?)GetValue(MainContentTemplateProperty); set => SetValue(MainContentTemplateProperty, value); }
    public object? DrawerContent { get => GetValue(DrawerContentProperty); set => SetValue(DrawerContentProperty, value); }
    public DataTemplate? DrawerContentTemplate { get => (DataTemplate?)GetValue(DrawerContentTemplateProperty); set => SetValue(DrawerContentTemplateProperty, value); }
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public double DrawerWidth { get => (double)GetValue(DrawerWidthProperty); set => SetValue(DrawerWidthProperty, value); }
    public double MinDrawerWidth { get => (double)GetValue(MinDrawerWidthProperty); set => SetValue(MinDrawerWidthProperty, value); }
    public double MaxDrawerWidth { get => (double)GetValue(MaxDrawerWidthProperty); set => SetValue(MaxDrawerWidthProperty, value); }
    public bool IsResizable { get => (bool)GetValue(IsResizableProperty); set => SetValue(IsResizableProperty, value); }
    public DrawerDisplayMode DisplayMode { get => (DrawerDisplayMode)GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }
    public ICommand? CloseCommand { get => (ICommand?)GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public bool CloseOnEscape { get => (bool)GetValue(CloseOnEscapeProperty); set => SetValue(CloseOnEscapeProperty, value); }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RightDrawerHost)d).UpdateLayoutState();

    private static void OnDrawerConstraintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        d.CoerceValue(DrawerWidthProperty);
        ((RightDrawerHost)d).UpdateLayoutState();
    }

    private static object CoerceDrawerWidth(DependencyObject d, object baseValue)
    {
        var host = (RightDrawerHost)d;
        var minimum = host.MinDrawerWidth;
        var configuredMaximum = Math.Max(minimum, host.MaxDrawerWidth);
        var availableMaximum = host.ActualWidth > 24
            ? Math.Max(minimum, host.ActualWidth - 24)
            : configuredMaximum;
        return Math.Clamp((double)baseValue, minimum, Math.Min(configuredMaximum, availableMaximum));
    }

    private void UpdateLayoutState()
    {
        if (DrawerColumn is null)
        {
            return;
        }

        DrawerBorder.Width = DrawerWidth;
        DrawerBorder.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        ResizeThumb.Visibility = IsResizable ? Visibility.Visible : Visibility.Collapsed;
        var isPush = DisplayMode == DrawerDisplayMode.Push;
        DrawerColumn.Width = IsOpen && isPush ? new GridLength(DrawerWidth) : new GridLength(0);
        Grid.SetColumn(DrawerBorder, isPush ? 1 : 0);
        Grid.SetColumnSpan(DrawerBorder, isPush ? 1 : 2);
        Grid.SetColumnSpan(MainPresenter, isPush ? 1 : 2);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsOpen || !CloseOnEscape || e.Key != Key.Escape || !DrawerBorder.IsKeyboardFocusWithin)
        {
            return;
        }

        if (CloseCommand is not null)
        {
            if (CloseCommand.CanExecute(null))
            {
                CloseCommand.Execute(null);
            }
        }
        else
        {
            SetCurrentValue(IsOpenProperty, false);
        }

        e.Handled = true;
    }

    private void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsOpen || !IsResizable)
        {
            return;
        }

        SetCurrentValue(DrawerWidthProperty, DrawerWidth - e.HorizontalChange);
    }
}
