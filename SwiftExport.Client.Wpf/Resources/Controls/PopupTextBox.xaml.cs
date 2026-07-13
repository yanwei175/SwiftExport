using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Controls;

public partial class PopupTextBox : UserControl
{
    private bool _isOpeningFromCompactEditor;

    public PopupTextBox()
    {
        InitializeComponent();
        Loaded += PopupTextBox_Loaded;
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(PopupTextBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(PopupTextBox),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty PopupHeightProperty =
        DependencyProperty.Register(
            nameof(PopupHeight),
            typeof(double),
            typeof(PopupTextBox),
            new PropertyMetadata(180d));

    public double PopupHeight
    {
        get => (double)GetValue(PopupHeightProperty);
        set => SetValue(PopupHeightProperty, value);
    }

    public static readonly DependencyProperty PopupWidthProperty =
        DependencyProperty.Register(
            nameof(PopupWidth),
            typeof(double),
            typeof(PopupTextBox),
            new PropertyMetadata(double.NaN, OnPopupWidthChanged));

    public double PopupWidth
    {
        get => (double)GetValue(PopupWidthProperty);
        set => SetValue(PopupWidthProperty, value);
    }

    private static readonly DependencyPropertyKey ResolvedPopupWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ResolvedPopupWidth),
            typeof(double),
            typeof(PopupTextBox),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty ResolvedPopupWidthProperty =
        ResolvedPopupWidthPropertyKey.DependencyProperty;

    public double ResolvedPopupWidth
    {
        get => (double)GetValue(ResolvedPopupWidthProperty);
        private set => SetValue(ResolvedPopupWidthPropertyKey, value);
    }

    public static readonly DependencyProperty IsPopupOpenProperty =
        DependencyProperty.Register(
            nameof(IsPopupOpen),
            typeof(bool),
            typeof(PopupTextBox),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsPopupOpenChanged));

    public bool IsPopupOpen
    {
        get => (bool)GetValue(IsPopupOpenProperty);
        set => SetValue(IsPopupOpenProperty, value);
    }

    public static readonly DependencyProperty OpenOnFocusProperty =
        DependencyProperty.Register(
            nameof(OpenOnFocus),
            typeof(bool),
            typeof(PopupTextBox),
            new PropertyMetadata(true));

    public bool OpenOnFocus
    {
        get => (bool)GetValue(OpenOnFocusProperty);
        set => SetValue(OpenOnFocusProperty, value);
    }

    public static readonly DependencyProperty OpenOnClickProperty =
        DependencyProperty.Register(
            nameof(OpenOnClick),
            typeof(bool),
            typeof(PopupTextBox),
            new PropertyMetadata(true));

    public bool OpenOnClick
    {
        get => (bool)GetValue(OpenOnClickProperty);
        set => SetValue(OpenOnClickProperty, value);
    }

    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(
            nameof(MaxLength),
            typeof(int),
            typeof(PopupTextBox),
            new PropertyMetadata(0));

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(PopupTextBox),
            new PropertyMetadata(false));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PopupTextBox)d).UpdateTextVisualState();
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PopupTextBox)d).UpdateTextVisualState();
    }

    private static void OnPopupWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PopupTextBox)d).UpdateResolvedPopupWidth();
    }

    private static void OnIsPopupOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PopupTextBox)d;
        if ((bool)e.NewValue)
        {
            control.UpdateResolvedPopupWidth();
            control.FocusPopupEditor();
        }
    }

    private void PopupTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResolvedPopupWidth();
        UpdateTextVisualState();
    }

    private void CompactTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (OpenOnFocus)
        {
            OpenPopup();
        }
    }

    private void CompactTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.F2)
        {
            OpenPopup();
            e.Handled = true;
        }
    }

    private void EditorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!OpenOnClick)
        {
            return;
        }

        _isOpeningFromCompactEditor = true;
        OpenPopup();
        e.Handled = true;
    }

    private void EditorPopup_Opened(object sender, EventArgs e)
    {
        FocusPopupEditor();
    }

    private void EditorPopup_Closed(object sender, EventArgs e)
    {
        _isOpeningFromCompactEditor = false;
        UpdateTextVisualState();
    }

    private void PopupEditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsPopupOpen = false;
            CompactTextBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            IsPopupOpen = false;
            CompactTextBox.Focus();
            e.Handled = true;
        }
    }

    private void OpenPopup()
    {
        if (IsReadOnly)
        {
            return;
        }

        IsPopupOpen = true;
    }

    private void FocusPopupEditor()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!IsPopupOpen)
                {
                    return;
                }

                PopupEditorTextBox.Focus();
                Keyboard.Focus(PopupEditorTextBox);
                PopupEditorTextBox.CaretIndex = PopupEditorTextBox.Text.Length;
            }),
            DispatcherPriority.Input);
    }

    private void UpdateResolvedPopupWidth()
    {
        ResolvedPopupWidth = double.IsNaN(PopupWidth) || PopupWidth <= 0
            ? double.NaN
            : PopupWidth;
    }

    private void UpdateTextVisualState()
    {
        var hasText = !string.IsNullOrWhiteSpace(Text);
        PlaceholderTextBlock.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        PreviewTextBlock.Visibility = hasText && !CompactTextBox.IsKeyboardFocusWithin && !_isOpeningFromCompactEditor
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactTextBox.Visibility = PreviewTextBlock.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
