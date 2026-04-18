using MateWpfThemes.FW.Converters;
using SwiftExport.Contracts.DTOs.Products;
using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SwiftExport.View.FW.ProductViews.Assists
{
    /// <summary>
    /// BOM 子件代码列的产品快速搜索弹窗助手。
    /// 单例 Popup + 富 ListView 模板，防抖调用 ViewModel API 命令后自动展示结果。
    /// 整个列共用一个 Popup 实例，节省内存，与 DataGridDropdownAssist 设计模式一致。
    /// </summary>
    public static class BomSubSkuSearchAssist
    {
        #region 附加属性 (Attached Properties)

        // SearchCommand — 绑定 ViewModel 的搜索命令，参数为关键字字符串（防抖后调用）
        public static readonly DependencyProperty SearchCommandProperty =
            DependencyProperty.RegisterAttached("SearchCommand", typeof(ICommand), typeof(BomSubSkuSearchAssist),
                new PropertyMetadata(null, OnSearchCommandChanged));

        public static ICommand GetSearchCommand(DependencyObject obj) => (ICommand)obj.GetValue(SearchCommandProperty);
        public static void SetSearchCommand(DependencyObject obj, ICommand value) => obj.SetValue(SearchCommandProperty, value);

        // SearchResults — 绑定 ViewModel 的搜索结果集合，集合引用变更时弹窗自动刷新
        public static readonly DependencyProperty SearchResultsProperty =
            DependencyProperty.RegisterAttached("SearchResults", typeof(IEnumerable), typeof(BomSubSkuSearchAssist),
                new PropertyMetadata(null, OnSearchResultsChanged));

        public static IEnumerable GetSearchResults(DependencyObject obj) => (IEnumerable)obj.GetValue(SearchResultsProperty);
        public static void SetSearchResults(DependencyObject obj, IEnumerable value) => obj.SetValue(SearchResultsProperty, value);

        // ItemSelectedCommand — 用户从弹窗选中某项后触发，参数为 ProductDto，供 ViewModel 回填描述/图片等字段
        public static readonly DependencyProperty ItemSelectedCommandProperty =
            DependencyProperty.RegisterAttached("ItemSelectedCommand", typeof(ICommand), typeof(BomSubSkuSearchAssist),
                new PropertyMetadata(null));

        public static ICommand GetItemSelectedCommand(DependencyObject obj) => (ICommand)obj.GetValue(ItemSelectedCommandProperty);
        public static void SetItemSelectedCommand(DependencyObject obj, ICommand value) => obj.SetValue(ItemSelectedCommandProperty, value);

        #endregion

        #region 全局单例状态 (Shared Singleton State)

        private static Popup _sharedPopup;
        private static ListView _sharedListView;
        private static TextBox _currentTextBox;
        private static DispatcherTimer _debounceTimer;

        // 防止代码回填文本时再次触发搜索
        private static bool _isSetting;

        #endregion

        #region 附加属性变更回调 (Property Change Callbacks)

        private static void OnSearchCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBox tb)) return;
            EnsurePopup();

            // 避免重复注册
            tb.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
            tb.LostKeyboardFocus -= TextBox_LostKeyboardFocus;
            tb.TextChanged -= TextBox_TextChanged;
            tb.PreviewKeyDown -= TextBox_PreviewKeyDown;

            if (e.NewValue != null)
            {
                tb.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                tb.LostKeyboardFocus += TextBox_LostKeyboardFocus;
                tb.TextChanged += TextBox_TextChanged;
                tb.PreviewKeyDown += TextBox_PreviewKeyDown;
            }
        }

        private static void OnSearchResultsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 仅响应当前正在编辑的 TextBox，避免其他行的绑定干扰弹窗
            if (!(d is TextBox tb) || _currentTextBox != tb || _sharedListView == null) return;

            _sharedListView.ItemsSource = e.NewValue as IEnumerable;
            if (_sharedPopup != null)
                _sharedPopup.IsOpen = _sharedListView.Items.Count > 0;
        }

        #endregion

        #region TextBox 事件处理 (TextBox Event Handlers)

        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            _currentTextBox = tb;
            _sharedPopup.PlacementTarget = tb;
            // 弹窗宽度至少 420，确保多字段展示不拥挤
            _sharedPopup.Width = Math.Max(tb.ActualWidth, 420);
        }

        private static void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // ListView Focusable=false，点击列表不会触发此事件，因此直接关闭
            if (_sharedPopup != null && _sharedPopup.IsOpen)
                _sharedPopup.IsOpen = false;
        }

        private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 代码回填时跳过，防止 ConfirmSelection 触发重复搜索
            if (_isSetting) return;
            if (!(sender is TextBox tb) || _currentTextBox != tb) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private static void Debounce_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (_currentTextBox == null) return;
            var cmd = GetSearchCommand(_currentTextBox);
            var keyword = _currentTextBox.Text;
            if (cmd != null && cmd.CanExecute(keyword))
                cmd.Execute(keyword);
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_sharedPopup == null || !_sharedPopup.IsOpen || _sharedListView.Items.Count == 0) return;

            switch (e.Key)
            {
                case Key.Down:
                    if (_sharedListView.SelectedIndex < _sharedListView.Items.Count - 1)
                        _sharedListView.SelectedIndex++;
                    _sharedListView.ScrollIntoView(_sharedListView.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (_sharedListView.SelectedIndex > 0)
                        _sharedListView.SelectedIndex--;
                    _sharedListView.ScrollIntoView(_sharedListView.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_sharedListView.SelectedItem != null)
                    {
                        ConfirmSelection(_sharedListView.SelectedItem as ProductDto);
                        e.Handled = true;
                    }
                    break;
                case Key.Escape:
                    _sharedPopup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private static void SharedListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentTextBox == null) return;
            var container = ItemsControl.ContainerFromElement(_sharedListView, e.OriginalSource as DependencyObject) as ListViewItem;
            if (container != null)
            {
                ConfirmSelection(container.DataContext as ProductDto);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 确认选中：回填 SubSku 到 TextBox，通知 ViewModel 更新其他字段
        /// </summary>
        private static void ConfirmSelection(ProductDto product)
        {
            if (product == null || _currentTextBox == null) return;

            // _isSetting=true 阻止 TextChanged 触发防抖搜索
            _isSetting = true;
            _currentTextBox.Text = product.Sku;
            _currentTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _currentTextBox.CaretIndex = _currentTextBox.Text.Length;
            _isSetting = false;

            // 通知 ViewModel 用完整 ProductDto 更新描述/图片等字段
            var cmd = GetItemSelectedCommand(_currentTextBox);
            if (cmd != null && cmd.CanExecute(product))
                cmd.Execute(product);

            _sharedPopup.IsOpen = false;
        }

        #endregion

        #region 初始化单例弹窗 (Initialize Shared Popup)

        private static void EnsurePopup()
        {
            if (_sharedPopup != null) return;

            // 防抖计时器 500ms，与 ProductSelectorViewModel 保持一致
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _debounceTimer.Tick += Debounce_Tick;

            // 复用主表图片列已有的 LRU 磁盘缓存转换器
            var imgConverter = new ImgPathToImgConverter();

            // 构建富模板：DockPanel[ 图片(左) | 颜色(右) | SKU+描述(填充) ]
            var itemTemplate = new DataTemplate();
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);
            dock.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 2));

            // 图片（左侧固定 38px）
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetValue(DockPanel.DockProperty, Dock.Left);
            img.SetValue(FrameworkElement.WidthProperty, 38.0);
            img.SetValue(FrameworkElement.HeightProperty, 38.0);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            img.SetValue(Image.StretchProperty, Stretch.Uniform);
            img.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            img.SetBinding(Image.SourceProperty, new Binding("MainImage") { Converter = imgConverter });
            dock.AppendChild(img);

            // 颜色/Finish（右侧固定 78px，蓝色）
            var tbFinish = new FrameworkElementFactory(typeof(TextBlock));
            tbFinish.SetValue(DockPanel.DockProperty, Dock.Right);
            tbFinish.SetValue(FrameworkElement.WidthProperty, 78.0);
            tbFinish.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tbFinish.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            tbFinish.SetValue(TextBlock.FontSizeProperty, 11.0);
            tbFinish.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(22, 119, 255)));
            tbFinish.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tbFinish.SetBinding(TextBlock.TextProperty, new Binding("Finish"));
            dock.AppendChild(tbFinish);

            // SKU + 描述（垂直堆叠，填充剩余空间）
            var textStack = new FrameworkElementFactory(typeof(StackPanel));
            textStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            textStack.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var tbSku = new FrameworkElementFactory(typeof(TextBlock));
            tbSku.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            tbSku.SetValue(TextBlock.FontSizeProperty, 12.0);
            tbSku.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
            tbSku.SetBinding(TextBlock.TextProperty, new Binding("Sku"));
            textStack.AppendChild(tbSku);

            var tbDesc = new FrameworkElementFactory(typeof(TextBlock));
            tbDesc.SetValue(TextBlock.FontSizeProperty, 11.0);
            tbDesc.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(110, 110, 110)));
            tbDesc.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tbDesc.SetBinding(TextBlock.TextProperty, new Binding("Description"));
            textStack.AppendChild(tbDesc);

            dock.AppendChild(textStack);
            itemTemplate.VisualTree = dock;

            // ListView（Focusable=false 防止抢夺 DataGrid 焦点，与 DataGridDropdownAssist 一致）
            _sharedListView = new ListView
            {
                Focusable = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MaxHeight = 340,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = itemTemplate
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_sharedListView, ScrollBarVisibility.Disabled);

            // ListViewItem 样式：禁焦点、悬停高亮、底部分割线
            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            itemStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(25, 0, 0, 0))));
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 245, 255))));
            itemStyle.Triggers.Add(hoverTrigger);
            _sharedListView.ItemContainerStyle = itemStyle;

            _sharedListView.PreviewMouseLeftButtonUp += SharedListView_PreviewMouseLeftButtonUp;

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 2, 0, 0),
                Child = _sharedListView,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.12,
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Direction = 270
                }
            };

            _sharedPopup = new Popup
            {
                Child = border,
                AllowsTransparency = true,
                StaysOpen = true,
                Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.Fade
            };
        }

        #endregion
    }
}
