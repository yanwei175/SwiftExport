using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExportClient.Resources.Controls;

public partial class PopupTextBox : UserControl
{
    private bool _isOpeningFromCompactEditor;
    private bool _isPopupOpenPending;
    private bool _suppressNextFocusOpen;

    public PopupTextBox()
    {
        InitializeComponent();
        Loaded += PopupTextBox_Loaded;
        IsVisibleChanged += PopupTextBox_IsVisibleChanged;
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
        }
        else
        {
            control._isPopupOpenPending = false;
        }
    }

    private void PopupTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResolvedPopupWidth();
        UpdateTextVisualState();
    }

    /// <summary>宿主页签隐藏时关闭脱离主视觉树的编辑 Popup，同时保留尚未提交的文本绑定状态。</summary>
    private void PopupTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            return;
        }

        _isPopupOpenPending = false;
        IsPopupOpen = false;
    }

    private void CompactTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressNextFocusOpen)
        {
            _suppressNextFocusOpen = false;
            return;
        }

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
        _isPopupOpenPending = false;
        _isOpeningFromCompactEditor = false;
        UpdateTextVisualState();
    }

    private void PopupEditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePopupAndRestoreCompactFocus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ClosePopupAndRestoreCompactFocus();
            e.Handled = true;
        }
    }

    private void PopupEditorTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (IsPopupOpen &&
                    !PopupEditorTextBox.IsKeyboardFocusWithin &&
                    !CompactTextBox.IsKeyboardFocusWithin)
                {
                    IsPopupOpen = false;
                }
            }),
            DispatcherPriority.Input);
    }

    private void OpenPopup()
    {
        if (IsReadOnly)
        {
            return;
        }

        if (IsPopupOpen)
        {
            FocusPopupEditor();
            return;
        }

        if (_isPopupOpenPending)
        {
            return;
        }

        _isPopupOpenPending = true;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!_isPopupOpenPending)
                {
                    return;
                }

                _isPopupOpenPending = false;
                if (!IsReadOnly)
                {
                    IsPopupOpen = true;
                }
            }),
            DispatcherPriority.Input);
    }

    private void ClosePopupAndRestoreCompactFocus()
    {
        _isPopupOpenPending = false;
        _suppressNextFocusOpen = true;
        IsPopupOpen = false;
        if (!CompactTextBox.Focus())
        {
            _suppressNextFocusOpen = false;
        }
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
