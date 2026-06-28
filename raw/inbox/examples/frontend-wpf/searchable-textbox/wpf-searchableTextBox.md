# SearchableTextBox 控件模板

这个控件用于 WPF 中“输入搜索、下拉选择、选中后显示指定字段”的场景。  
核心原则是：`TextBox` 只保存用户正在输入的搜索关键字，选中项的显示文本由单独的 `TextBlock` 维护，避免选中项回填到 `TextBox.Text` 后再次触发搜索。

## SearchableTextBox.xaml

```xaml
<UserControl x:Class="ProductPriceManager.App.Client.Wpf.UserControls.SearchableTextBox"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" 
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008" 
             xmlns:hc="https://handyorg.github.io/handycontrol"
             xmlns:local="clr-namespace:ProductPriceManager.App.Client.Wpf.UserControls"
             mc:Ignorable="d" 
             x:Name="uc" d:DesignWidth="220" d:Height="46">

    <Border x:Name="EditorBorder"
            Height="{Binding ActualHeight, ElementName=uc}"
            MinHeight="30"
            Background="White"
            BorderBrush="#DCDCDC"
            BorderThickness="1"
            CornerRadius="4"
            MouseLeftButtonDown="EditorBorder_MouseLeftButtonDown">
        <Grid>
            <Grid>
                <!-- 输入态：TextBox 没有自己的边框 -->
                <TextBox x:Name="InputTextBox"
                     BorderThickness="0"
                     Background="Transparent"
                     Padding="8,0,28,0"
                     VerticalContentAlignment="Center"
                     TextChanged="InputTextBox_TextChanged"
                     GotKeyboardFocus="InputTextBox_GotKeyboardFocus"
                     LostKeyboardFocus="InputTextBox_LostKeyboardFocus"
                     PreviewKeyDown="InputTextBox_PreviewKeyDown" />
                <!-- 选中态：TextBlock 显示选中的对象文本 -->
                <TextBlock x:Name="DisplayTextBlock"
                       Margin="8,0,28,0"
                       VerticalAlignment="Center"
                       TextTrimming="CharacterEllipsis"
                       IsHitTestVisible="False"
                       Visibility="Collapsed" />

                <!-- 占位文字 -->
                <TextBlock x:Name="PlaceholderTextBlock"
                       Margin="8,0,28,0"
                       VerticalAlignment="Center"
                       Foreground="#999999"
                       IsHitTestVisible="False"
                       Text="{Binding Placeholder, ElementName=uc}" />

                <!-- 清空按钮 -->
                <Button x:Name="BtnClear"
                        Style="{StaticResource UserButtonIconEdit}"
                        Width="22"
                        Height="22"
                        HorizontalAlignment="Right"
                        VerticalAlignment="Center"
                        Margin="0,0,4,0"
                        Content="×"
                        Click="BtnClear_Click" />
            </Grid>

            <Popup PlacementTarget="{Binding ElementName=EditorBorder}"
                   Placement="Bottom"
                   StaysOpen="False"
                   IsOpen="{Binding IsDropDownOpen, ElementName=uc, Mode=TwoWay}">
                <Border MinWidth="{Binding ActualWidth, ElementName=EditorBorder}"
                        MaxHeight="260"
                        Background="White"
                        BorderBrush="#DCDCDC"
                        BorderThickness="1">
                        <ScrollViewer CanContentScroll="True">
                            <ListBox x:Name="ResultList"
                                 BorderThickness="0"
                                 PreviewMouseLeftButtonUp="ResultList_PreviewMouseLeftButtonUp" />
                        </ScrollViewer>
                    </Border>
                </Popup>
        </Grid>
        </Border>


</UserControl>
```

## SearchableTextBox.xaml.cs

```csharp
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProductPriceManager.App.Client.Wpf.UserControls;

public enum SearchableTextBoxSearchMode
{
    /// <summary>
    /// 自动判断：
    /// Text 被外部绑定时，默认当成远程搜索；
    /// Text 没有外部绑定时，默认本地搜索。
    /// </summary>
    Auto,

    /// <summary>
    /// 使用 ItemsSource 做本地过滤。
    /// </summary>
    Local,

    /// <summary>
    /// 不做本地过滤。
    /// Text 变化只同步给外部 ViewModel，由外部自己 API 搜索并更新 ItemsSource。
    /// </summary>
    Remote
}

/// <summary>
/// 可搜索、可选择、可自定义下拉模板的 TextBox。
/// DisplayTextBlock 负责显示选中项文本，InputTextBox 只负责搜索输入，避免选中后重复触发搜索。
/// </summary>
public partial class SearchableTextBox : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _debounceTimer;
    private readonly Dictionary<Type, Dictionary<string, PropertyInfo?>> _propertyCache = new();
    private bool _isInternalChanging;
    private bool _isSelectionDisplayMode;
    private INotifyCollectionChanged? _itemsCollectionChangedSource;

    public SearchableTextBox()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };

        _debounceTimer.Tick += DebounceTimer_Tick;

        Loaded += (_, _) =>
        {
            ApplyItemTemplate();
            UpdateDisplayTextFromSelectedItem();
            UpdateTextVisualState();
        };

        Unloaded += (_, _) =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Tick -= DebounceTimer_Tick;
            DetachCollectionChanged();
        };
    }

    #region Placeholder

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SearchableTextBox),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SearchableTextBox)d).UpdateTextVisualState();
    }

    #endregion

    #region SearchMode

    public static readonly DependencyProperty SearchModeProperty =
        DependencyProperty.Register(
            nameof(SearchMode),
            typeof(SearchableTextBoxSearchMode),
            typeof(SearchableTextBox),
            new PropertyMetadata(SearchableTextBoxSearchMode.Auto));

    public SearchableTextBoxSearchMode SearchMode
    {
        get => (SearchableTextBoxSearchMode)GetValue(SearchModeProperty);
        set => SetValue(SearchModeProperty, value);
    }

    #endregion

    #region Text

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SearchableTextBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChangedCallback));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SearchableTextBox)d;

        if (control._isInternalChanging)
            return;

        control.SetInputTextSilently(e.NewValue?.ToString() ?? string.Empty);
        control.UpdateTextVisualState();
    }

    #endregion

    #region ItemsSource

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SearchableTextBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SearchableTextBox)d;

        control.DetachCollectionChanged();

        if (e.NewValue is IEnumerable source)
        {
            var cvs = new CollectionViewSource { Source = source };

            control.FilteredView = cvs.View;
            control.FilteredView.Filter = control.FilterItem;

            if (control.ResultList != null)
            {
                control.ResultList.ItemsSource = control.FilteredView;
            }

            control.AttachCollectionChanged(source);
            control.FilteredView.Refresh();

            if (!control.ShouldUseLocalFilter())
            {
                control.IsDropDownOpen = !control.FilteredView.IsEmpty;
            }
        }
        else
        {
            control.FilteredView = null;

            if (control.ResultList != null)
            {
                control.ResultList.ItemsSource = null;
            }

            control.SetSelectedItemSilently(null);
            control.SelectedValue = null;
            control.SetDisplayText(string.Empty);
            control.IsDropDownOpen = false;
        }

        control.UpdateTextVisualState();
    }

    private void AttachCollectionChanged(IEnumerable source)
    {
        _itemsCollectionChangedSource = source as INotifyCollectionChanged;

        if (_itemsCollectionChangedSource != null)
        {
            _itemsCollectionChangedSource.CollectionChanged += ItemsSource_CollectionChanged;
        }
    }

    private void DetachCollectionChanged()
    {
        if (_itemsCollectionChangedSource != null)
        {
            _itemsCollectionChangedSource.CollectionChanged -= ItemsSource_CollectionChanged;
            _itemsCollectionChangedSource = null;
        }
    }

    private void ItemsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ItemsSource_CollectionChanged(sender, e));
            return;
        }

        FilteredView?.Refresh();

        if (!ShouldUseLocalFilter() && InputTextBox.IsKeyboardFocusWithin)
        {
            var hasInput = !string.IsNullOrWhiteSpace(InputTextBox.Text);
            IsDropDownOpen = hasInput && FilteredView is { IsEmpty: false };
        }
    }

    #endregion

    #region DisplayMemberPath

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(SearchableTextBox),
            new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SearchableTextBox)d;

        control.ApplyItemTemplate();
        control.UpdateDisplayTextFromSelectedItem();
    }

    #endregion

    #region FilterMemberPaths

    public static readonly DependencyProperty FilterMemberPathsProperty =
        DependencyProperty.Register(
            nameof(FilterMemberPaths),
            typeof(string),
            typeof(SearchableTextBox),
            new PropertyMetadata(string.Empty));

    public string FilterMemberPaths
    {
        get => (string)GetValue(FilterMemberPathsProperty);
        set => SetValue(FilterMemberPathsProperty, value);
    }

    #endregion

    #region RequireItemMatch

    public static readonly DependencyProperty RequireItemMatchProperty =
        DependencyProperty.Register(
            nameof(RequireItemMatch),
            typeof(bool),
            typeof(SearchableTextBox),
            new PropertyMetadata(false));

    public bool RequireItemMatch
    {
        get => (bool)GetValue(RequireItemMatchProperty);
        set => SetValue(RequireItemMatchProperty, value);
    }

    #endregion

    #region SelectedItem

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(SearchableTextBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemChanged));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SearchableTextBox)d;

        if (control._isInternalChanging)
            return;

        if (e.NewValue == null)
        {
            control._isSelectionDisplayMode = false;

            control.SelectedValue = null;
            control.SetDisplayText(string.Empty);
            control.UpdateTextVisualState();
            return;
        }

        control.SelectedValue = control.GetSelectedValue(e.NewValue);
        control.SetDisplayText(control.GetDisplayText(e.NewValue));
        control.ClearInputTextSilently();

        // 关键：外部设置 SelectedItem 时，也进入显示模式
        control._isSelectionDisplayMode = true;

        control.UpdateTextVisualState();
    }

    #endregion

    #region SelectionCommittedCommand

    public static readonly DependencyProperty SelectionCommittedCommandProperty =
        DependencyProperty.Register(
            nameof(SelectionCommittedCommand),
            typeof(ICommand),
            typeof(SearchableTextBox),
            new PropertyMetadata(null));

    public ICommand? SelectionCommittedCommand
    {
        get => (ICommand?)GetValue(SelectionCommittedCommandProperty);
        set => SetValue(SelectionCommittedCommandProperty, value);
    }

    #endregion

    #region SelectedValuePath

    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register(
            nameof(SelectedValuePath),
            typeof(string),
            typeof(SearchableTextBox),
            new PropertyMetadata(string.Empty));

    public string SelectedValuePath
    {
        get => (string)GetValue(SelectedValuePathProperty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    #endregion

    #region SelectedValue

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(
            nameof(SelectedValue),
            typeof(object),
            typeof(SearchableTextBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    #endregion

    #region IsDropDownOpen

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(
            nameof(IsDropDownOpen),
            typeof(bool),
            typeof(SearchableTextBox),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    #endregion

    #region ItemTemplate

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(
            nameof(ItemTemplate),
            typeof(DataTemplate),
            typeof(SearchableTextBox),
            new PropertyMetadata(null, OnItemTemplateChanged));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SearchableTextBox)d).ApplyItemTemplate();
    }

    private void ApplyItemTemplate()
    {
        if (ResultList == null)
            return;

        if (ItemTemplate != null)
        {
            // 中文注释：WPF 不允许 ListBox 同时设置 DisplayMemberPath 和 ItemTemplate。
            // 中文注释：必须先清空 DisplayMemberPath，再设置 ItemTemplate。
            ResultList.DisplayMemberPath = string.Empty;
            ResultList.ItemTemplate = ItemTemplate;
        }
        else
        {
            // 中文注释：切回 DisplayMemberPath 时，也要先清空 ItemTemplate。
            ResultList.ItemTemplate = null;
            ResultList.DisplayMemberPath = DisplayMemberPath ?? string.Empty;
        }
    }

    #endregion

    #region FilteredView

    /// <summary>
    /// 给内部 ListBox 绑定的过滤视图。
    /// </summary>
    public ICollectionView? FilteredView { get; private set; }

    #endregion

    #region 输入与搜索

    private void InputTextBox_GotKeyboardFocus(object? sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateTextVisualState();
        OpenDropDownIfHasItems();
    }

    private void InputTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInternalChanging)
            return;

        var newText = InputTextBox.Text ?? string.Empty;

        // 用户开始输入，退出选中显示模式
        _isSelectionDisplayMode = false;

        // 中文注释：用户开始输入时，旧选中项已经不再可靠，先清掉选择但保留搜索输入。
        if (SelectedItem != null)
        {
            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);
        }

        SyncTextDependencySilently(newText);
        UpdateTextVisualState();

        if (ShouldUseLocalFilter())
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
        else
        {
            _debounceTimer.Stop();
            OpenDropDownIfHasItems();
        }
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        if (!ShouldUseLocalFilter())
            return;

        ResolveSelectionByText();
        RefreshAndUpdateDropDown();
    }

    private bool FilterItem(object item)
    {
        if (!ShouldUseLocalFilter())
            return true;

        var input = InputTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return true;

        foreach (var path in ParseFilterMemberPaths())
        {
            var value = GetMemberText(item, path);

            if (!string.IsNullOrEmpty(value) &&
                value.Contains(input, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshAndUpdateDropDown()
    {
        if (FilteredView == null)
        {
            IsDropDownOpen = false;
            return;
        }

        FilteredView.Refresh();
        IsDropDownOpen = !FilteredView.IsEmpty;
    }

    private void OpenDropDownIfHasItems()
    {
        if (FilteredView == null)
            return;

        FilteredView.Refresh();
        if (!ShouldUseLocalFilter() && string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            IsDropDownOpen = false;
            return;
        }

        IsDropDownOpen = !FilteredView.IsEmpty;
    }

    private bool ShouldUseLocalFilter()
    {
        if (SearchMode == SearchableTextBoxSearchMode.Local)
            return true;

        if (SearchMode == SearchableTextBoxSearchMode.Remote)
            return false;

        var bindingExpression = BindingOperations.GetBindingExpression(this, TextProperty);
        return bindingExpression == null;
    }

    #endregion

    #region 清空按钮

    private void BtnClear_Click(object? sender, RoutedEventArgs e)
    {
        _debounceTimer.Stop();

        _isSelectionDisplayMode = false;

        SetSelectedItemSilently(null);
        SelectedValue = null;
        SetDisplayText(string.Empty);

        SetInputTextSilently(string.Empty);
        SyncTextDependencySilently(string.Empty);

        FilteredView?.Refresh();
        IsDropDownOpen = FilteredView is { IsEmpty: false };

        UpdateTextVisualState();
        InputTextBox.Focus();
    }

    #endregion

    #region 键盘与鼠标选择

    private void InputTextBox_PreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Space && string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            FilteredView?.Refresh();

            if (FilteredView is { IsEmpty: false })
            {
                ResultList.SelectedIndex = 0;
                IsDropDownOpen = true;
            }

            e.Handled = true;
            return;
        }

        if (!IsDropDownOpen || ResultList.Items.Count == 0)
            return;

        if (e.Key == Key.Down)
        {
            ResultList.SelectedIndex = Math.Min(ResultList.SelectedIndex + 1, ResultList.Items.Count - 1);
            ResultList.ScrollIntoView(ResultList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
            ResultList.ScrollIntoView(ResultList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            CommitSelection(ResultList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    private async void InputTextBox_LostKeyboardFocus(object? sender, KeyboardFocusChangedEventArgs e)
    {
        await Task.Delay(120);

        if (!InputTextBox.IsKeyboardFocusWithin)
        {
            IsDropDownOpen = false;
        }

        if (!RequireItemMatch)
        {
            UpdateTextVisualState();
            return;
        }

        var currentText = (InputTextBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(currentText))
        {
            UpdateTextVisualState();
            return;
        }

        var hasMatch = ResolveSelectionByText();

        if (!hasMatch)
        {
            SetInputTextSilently(string.Empty);
            SyncTextDependencySilently(string.Empty);
            SetSelectedItemSilently(null);
            SelectedValue = null;
            SetDisplayText(string.Empty);
        }

        UpdateTextVisualState();
    }

    private void ResultList_PreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject originalSource)
            return;

        var clickedItem = FindAncestor<ListBoxItem>(originalSource);

        if (clickedItem == null)
            return;

        CommitSelection(clickedItem.DataContext);
        e.Handled = true;
    }

    private void CommitSelection(object? item)
    {
        if (item == null)
            return;

        _debounceTimer.Stop();

        SetSelectedItemSilently(item);
        SelectedValue = GetSelectedValue(item);
        SetDisplayText(GetDisplayText(item));

        // 中文注释：选中项显示交给 DisplayTextBlock，不能把显示文本写回 TextBox，
        // 否则远程搜索场景会把选中项文本再次同步给 SearchKeyword 并重复触发搜索。
        ClearInputTextSilently();
        // 关键：进入选中显示模式
        _isSelectionDisplayMode = true;

        IsDropDownOpen = false;
        UpdateTextVisualState();
        ExecuteSelectionCommittedCommand(item);
    }

    private void ExecuteSelectionCommittedCommand(object item)
    {
        if (SelectionCommittedCommand?.CanExecute(item) == true)
        {
            SelectionCommittedCommand.Execute(item);
        }
    }

    #endregion

    #region 选择同步

    private bool ResolveSelectionByText()
    {
        var text = (InputTextBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text) || ItemsSource == null)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        foreach (var item in ItemsSource)
        {
            if (item == null)
                continue;

            foreach (var path in ParseResolveMemberPaths())
            {
                var value = GetMemberText(item, path);

                if (string.Equals(
                        (value ?? string.Empty).Trim(),
                        text,
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedItemSilently(item);
                    SelectedValue = GetSelectedValue(item);
                    SetDisplayText(GetDisplayText(item));
                    return true;
                }
            }
        }

        SetSelectedItemSilently(null);
        SelectedValue = null;
        SetDisplayText(string.Empty);

        return false;
    }

    private void SetSelectedItemSilently(object? item)
    {
        if (Equals(SelectedItem, item))
            return;

        _isInternalChanging = true;

        try
        {
            SelectedItem = item;
        }
        finally
        {
            _isInternalChanging = false;
        }
    }

    #endregion

    #region 显示文本

    private void UpdateDisplayTextFromSelectedItem()
    {
        SetDisplayText(SelectedItem == null ? string.Empty : GetDisplayText(SelectedItem));
    }

    private void SetDisplayText(string? text)
    {
        if (DisplayTextBlock == null)
            return;

        DisplayTextBlock.Text = text ?? string.Empty;
        UpdateTextVisualState();
    }

    private void UpdateTextVisualState()
    {
        if (InputTextBox == null || DisplayTextBlock == null || PlaceholderTextBlock == null)
            return;

        var hasInput = !string.IsNullOrWhiteSpace(InputTextBox.Text);
        var hasDisplay = !string.IsNullOrWhiteSpace(DisplayTextBlock.Text);

        // 选中显示模式：
        // 显示 TextBlock，隐藏 TextBox。
        if (_isSelectionDisplayMode && hasDisplay)
        {
            InputTextBox.Visibility = Visibility.Collapsed;
            DisplayTextBlock.Visibility = Visibility.Visible;
            PlaceholderTextBlock.Visibility = Visibility.Collapsed;
            return;
        }

        // 输入模式：
        // 显示 TextBox，隐藏 TextBlock。
        InputTextBox.Visibility = Visibility.Visible;
        DisplayTextBlock.Visibility = Visibility.Collapsed;

        // 没有输入、没有选中显示文本时，显示 placeholder。
        PlaceholderTextBlock.Visibility = hasInput || hasDisplay
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    #endregion

    #region 成员读取与反射缓存

    private string GetDisplayText(object item)
    {
        if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
        {
            var prop = GetCachedProperty(item.GetType(), DisplayMemberPath);
            return prop?.GetValue(item)?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    private object? GetSelectedValue(object? item)
    {
        if (item == null)
            return null;

        if (string.IsNullOrWhiteSpace(SelectedValuePath))
            return item;

        var prop = GetCachedProperty(item.GetType(), SelectedValuePath);
        return prop?.GetValue(item);
    }

    private string GetMemberText(object item, string memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
            return item.ToString() ?? string.Empty;

        var prop = GetCachedProperty(item.GetType(), memberPath);
        return prop?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private PropertyInfo? GetCachedProperty(Type type, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return null;

        if (!_propertyCache.TryGetValue(type, out var typeProps))
        {
            typeProps = new Dictionary<string, PropertyInfo?>(StringComparer.OrdinalIgnoreCase);
            _propertyCache[type] = typeProps;
        }

        if (!typeProps.TryGetValue(propertyName, out var propInfo))
        {
            propInfo = type.GetProperty(
                propertyName,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            typeProps[propertyName] = propInfo;
        }

        return propInfo;
    }

    #endregion

    #region 字段路径解析

    private List<string> ParseFilterMemberPaths()
    {
        if (!string.IsNullOrWhiteSpace(FilterMemberPaths))
        {
            var paths = FilterMemberPaths
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count > 0)
                return paths;
        }

        if (!string.IsNullOrWhiteSpace(DisplayMemberPath))
        {
            return new List<string> { DisplayMemberPath };
        }

        return new List<string> { string.Empty };
    }

    private List<string> ParseResolveMemberPaths()
    {
        var paths = ParseFilterMemberPaths();

        if (!string.IsNullOrWhiteSpace(DisplayMemberPath) &&
            !paths.Contains(DisplayMemberPath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Insert(0, DisplayMemberPath);
        }

        return paths;
    }

    #endregion

    #region 静默同步

    private void SetInputTextSilently(string? text)
    {
        var target = text ?? string.Empty;
        var current = InputTextBox.Text ?? string.Empty;

        if (string.Equals(current, target, StringComparison.Ordinal))
            return;

        _isInternalChanging = true;

        try
        {
            InputTextBox.Text = target;
            InputTextBox.CaretIndex = target.Length;
        }
        finally
        {
            _isInternalChanging = false;
        }
    }

    private void ClearInputTextSilently()
    {
        SetInputTextSilently(string.Empty);
    }

    private void SyncTextDependencySilently(string? text)
    {
        var target = text ?? string.Empty;
        var current = Text ?? string.Empty;

        if (string.Equals(current, target, StringComparison.Ordinal))
            return;

        _isInternalChanging = true;

        try
        {
            Text = target;
        }
        finally
        {
            _isInternalChanging = false;
        }
    }

    #endregion

    #region VisualTree 辅助

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T ancestor)
                return ancestor;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }



    #endregion

    private void EditorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isSelectionDisplayMode)
        {
            BeginEditSelection();
            e.Handled = true;
            return;
        }

        InputTextBox.Focus();
    }
    private void BeginEditSelection()
    {
        _isSelectionDisplayMode = false;

        // 点击已选中的文本，表示用户准备重新搜索。
        SetSelectedItemSilently(null);
        SelectedValue = null;
        SetDisplayText(string.Empty);

        SetInputTextSilently(string.Empty);
        SyncTextDependencySilently(string.Empty);

        IsDropDownOpen = false;

        UpdateTextVisualState();

        InputTextBox.Focus();
    }
}



```

## 本地搜索用法

```xaml
<local:SearchableTextBox
    Height="32"
    Width="240"
    SearchMode="Local"
    ItemsSource="{Binding Products}"
    DisplayMemberPath="CompanyModel"
    FilterMemberPaths="CompanyModel,ChineseName,SupplierSku"
    SelectedItem="{Binding SelectedProduct, Mode=TwoWay}"
    SelectedValuePath="Uid"
    SelectedValue="{Binding SelectedProductId, Mode=TwoWay}"
    Placeholder="搜索产品" />
```

这种模式下，控件内部负责：

```text
输入文字 -> 防抖 -> 本地过滤 ItemsSource -> 打开 Popup -> 选择后用 DisplayTextBlock 显示 DisplayMemberPath
```

## 远程 API 搜索用法

```xaml
<local:SearchableTextBox
    Height="32"
    Width="280"
    SearchMode="Remote"
    Text="{Binding SearchKeyword, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
    ItemsSource="{Binding SearchResults}"
    DisplayMemberPath="CompanyModel"
    SelectedItem="{Binding SelectedProduct, Mode=TwoWay}"
    SelectedValuePath="Uid"
    SelectedValue="{Binding SelectedProductId, Mode=TwoWay}"
    Placeholder="远程搜索产品">

    <local:SearchableTextBox.ItemTemplate>
        <DataTemplate>
            <Grid Margin="2">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="42" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>

                <Border Width="36"
                        Height="36"
                        Background="#F2F3F5"
                        CornerRadius="4"
                        ClipToBounds="True">
                    <Image Source="{Binding MainImage}"
                           Stretch="UniformToFill" />
                </Border>

                <StackPanel Grid.Column="1"
                            Margin="8,0,0,0"
                            VerticalAlignment="Center">
                    <TextBlock Text="{Binding CompanyModel}"
                               FontWeight="SemiBold" />

                    <TextBlock Text="{Binding ChineseName}"
                               FontSize="12"
                               Foreground="#909399" />
                </StackPanel>
            </Grid>
        </DataTemplate>
    </local:SearchableTextBox.ItemTemplate>
</local:SearchableTextBox>
```

这种模式下，控件内部只负责：

```text
输入文字 -> 同步 Text 给外部 SearchKeyword
外部 ViewModel -> 防抖/API 分页搜索 -> 更新 SearchResults
控件收到 SearchResults -> 显示 Popup
选择结果 -> 设置 SelectedItem/SelectedValue -> DisplayTextBlock 显示选中项文本
```

注意：选择结果时不要把 `DisplayMemberPath` 对应的文本写回 `Text`，否则会再次更新 `SearchKeyword` 并触发远程搜索。

## ViewModel 远程防抖搜索示例

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;

public partial class ProductSearchViewModel : ObservableObject
{
        private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string searchKeyword = string.Empty;

    [ObservableProperty]
    private ProductDto? selectedProduct;

    [ObservableProperty]
    private Guid? selectedProductId;

    public ObservableCollection<ProductDto> SearchResults { get; } = new();

    partial void OnSearchKeywordChanged(string value)
    {
        _ = SearchProductsAsync(value);
    }

    private async Task SearchProductsAsync(string keyword)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            // 中文注释：外部远程搜索防抖。
            await Task.Delay(300, token);

            if (string.IsNullOrWhiteSpace(keyword))
            {
                SearchResults.Clear();
                return;
            }

            // 中文注释：这里调用你的 ApplicationService / API / Repository。
            var result = await productService.SearchProductsPageAsync(
                keyword,
                pageIndex: 0,
                pageSize: 20,
                cancellationToken: token);

            SearchResults.Clear();

            foreach (var item in result)
            {
                SearchResults.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            // 中文注释：用户继续输入导致上一次搜索取消，属于正常情况。
        }
    }
}
```

## 已修正的问题

- 选中项不再回填 `InputTextBox.Text`，改由 `DisplayTextBlock` 显示 `DisplayMemberPath`，避免重复触发远程搜索。
- `ItemTemplate` 和 `DisplayMemberPath` 互斥处理：设置 `ItemTemplate` 前先清空 `DisplayMemberPath`；切回 `DisplayMemberPath` 前先清空 `ItemTemplate`。
- `ItemsSource` 为 `ObservableCollection` 且集合实例不变时，监听 `CollectionChanged`，远程搜索结果追加/清空后也能刷新下拉状态。
- `RequireItemMatch` 在输入为空时不再误清除已经选中的项，避免失焦时把正常选择擦掉。
- 用户重新输入时主动清空旧 `SelectedItem` / `SelectedValue`，避免搜索文本和已选对象不一致。
