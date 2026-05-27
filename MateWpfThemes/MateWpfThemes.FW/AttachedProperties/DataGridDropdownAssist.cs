using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// DataGrid 内联 TextBox 专用的独立 Popup 下拉联动助手
    /// 场景 A：纯本地数据
    /// 不设置 SearchCommand
    /// ItemsSource 绑定静态集合
    /// TextChanged → 本地过滤 → Popup 展示
    /// 场景 B：实时 API 搜索
    /// SearchCommand 绑定 ViewModel 的异步命令
    /// TextChanged → 防抖 300ms → 触发 SearchCommand(keyword)
    /// → ViewModel 请求 API → 更新 ProductSuggestions
    /// → ItemsSource 绑定的是 ProductSuggestions → Popup 自动刷新
    /// 
    /// public ObservableCollection<Product> ProductSuggestions { get; } = new();
    /// private CancellationTokenSource _cts;
    ///
    /// // ✅ 和你现有代码完全一样的写法
    /// public ICommand SearchProductCommand { get; }
    ///
    /// public OrderViewModel()
    /// {
    ///     SearchProductCommand = new RelayCommand<string>(
    ///         async keyword => await SearchProductAsync(keyword));
    /// }
    ///
    /// private async Task SearchProductAsync(string keyword)
    /// {
    ///     _cts?.Cancel();
    ///     _cts = new CancellationTokenSource();
    ///
    ///     try
    ///     {
    ///         如果要做缓存到这里做.
    ///         var results = await _apiService.SearchAsync(keyword, _cts.Token);
    ///
    ///         ProductSuggestions.Clear();
    ///         foreach (var item in results.Take(20))
    ///             ProductSuggestions.Add(item);
    ///     }
    ///     catch (OperationCanceledException) { /* 正常取消 */ }
    ///     catch (Exception ex)
    ///     {
    ///         // 内部兜底，不让异常逃出 async void
    ///         _dialogService.ShowError(ex.Message);
    ///     }
    /// }
    /// 使用方式: 资源文件里
    /// <Style x:Key="ProductCellStyle" TargetType="TextBox">
    ///    <!-- 数据源,如果是本地数据就不需要绑定SearchCommand-->
    ///    <Setter Property = "mt:DataGridDropdownAssist.ItemsSource" Value="{Binding DataContext.Products, RelativeSource={RelativeSource AncestorType = DataGrid}}" />
    ///    <!--异步命令,Api数据,如果是本地数据就不需要绑定SearchCommand-->
    ///    <Setter Property="mt:DataGridDropdownAssist.SearchCommand"  Value="{Binding DataContext.SearchProductCommand, RelativeSource={RelativeSource AncestorType = DataGrid}}" />
    ///    <Setter Property = "mt:DataGridDropdownAssist.SearchDelay" Value = "400" />
    ///   
    ///    < !-- ★ 双向绑定完整对象,如果不设置就走DisplayMemberPath-->
    ///    < Setter Property = "mt:DataGridDropdownAssist.SelectedItem" Value = "{Binding SelectedProduct, Mode=TwoWay}" /> 
    ///    < !--显示字段-->
    ///    < Setter Property = "mt:DataGridDropdownAssist.DisplayMemberPath"  Value = "Name" />
    ///    < Setter Property = "mt:DataGridDropdownAssist.FilterMemberPaths"  Value = "Name,Code" />
    ///    < Setter Property = "mt:DataGridDropdownAssist.RequireItemMatch"   Value = "True" />
    ///    < Setter Property = "mt:DataGridDropdownAssist.PopupWidth"         Value = "320" />
    ///
    ///    < !--自定义候选模板(可选)-->
    ///    <Setter Property="mt:DataGridDropdownAssist.ItemTemplate">
    ///        <Setter.Value>
    ///            <DataTemplate>
    ///                <Grid>
    ///                    <Grid.ColumnDefinitions>
    ///                        <ColumnDefinition Width="70"/>
    ///                        <ColumnDefinition Width="*"/>
    ///                        <ColumnDefinition Width="60"/>
    ///                    </Grid.ColumnDefinitions>
    ///                    <TextBlock Grid.Column="0" Text="{Binding Code}"
    ///                               FontFamily="Consolas" Foreground="#0078D4"/>
    ///                    <TextBlock Grid.Column="1" Text="{Binding Name}"/>
    ///                    <TextBlock Grid.Column="2"
    ///                               Text="{Binding Price, StringFormat=¥{0:N2}}"
    ///                               Foreground="#388E3C" HorizontalAlignment="Right"/>
    ///                </Grid>
    ///            </DataTemplate>
    ///        </Setter.Value>
    ///    </Setter>
    /// </Style>
    ///DataGrid列
    ///<DataGridTextColumn Header="物料"
    ///                Binding="{Binding SelectedProduct.Name}"
    ///             EditingElementStyle="{StaticResource ProductCellStyle}" />
    /// </summary>
    public static class DataGridDropdownAssist
    {
        // =======================
        // 1. ItemsSource
        // =======================
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.RegisterAttached("ItemsSource", typeof(IEnumerable), typeof(DataGridDropdownAssist),
                new PropertyMetadata(null, OnItemsSourceChanged));
        public static IEnumerable GetItemsSource(DependencyObject obj) => (IEnumerable)obj.GetValue(ItemsSourceProperty);
        public static void SetItemsSource(DependencyObject obj, IEnumerable value) => obj.SetValue(ItemsSourceProperty, value);

        // =======================
        // 2. DisplayMemberPath
        // =======================
        public static readonly DependencyProperty DisplayMemberPathProperty =
            DependencyProperty.RegisterAttached("DisplayMemberPath", typeof(string), typeof(DataGridDropdownAssist),
                new PropertyMetadata(string.Empty));
        public static string GetDisplayMemberPath(DependencyObject obj) => (string)obj.GetValue(DisplayMemberPathProperty);
        public static void SetDisplayMemberPath(DependencyObject obj, string value) => obj.SetValue(DisplayMemberPathProperty, value);

        // =======================
        // 3. FilterMemberPaths
        // =======================
        public static readonly DependencyProperty FilterMemberPathsProperty =
            DependencyProperty.RegisterAttached("FilterMemberPaths", typeof(string), typeof(DataGridDropdownAssist),
                new PropertyMetadata(string.Empty));
        public static string GetFilterMemberPaths(DependencyObject obj) => (string)obj.GetValue(FilterMemberPathsProperty);
        public static void SetFilterMemberPaths(DependencyObject obj, string value) => obj.SetValue(FilterMemberPathsProperty, value);

        // =======================
        // 4. RequireItemMatch
        // =======================
        public static readonly DependencyProperty RequireItemMatchProperty =
            DependencyProperty.RegisterAttached("RequireItemMatch", typeof(bool), typeof(DataGridDropdownAssist),
                new PropertyMetadata(false));
        public static bool GetRequireItemMatch(DependencyObject obj) => (bool)obj.GetValue(RequireItemMatchProperty);
        public static void SetRequireItemMatch(DependencyObject obj, bool value) => obj.SetValue(RequireItemMatchProperty, value);

        // =======================
        // 5. SelectedValuePath
        // =======================
        public static readonly DependencyProperty SelectedValuePathProperty =
            DependencyProperty.RegisterAttached("SelectedValuePath", typeof(string), typeof(DataGridDropdownAssist),
                new PropertyMetadata(string.Empty));
        public static string GetSelectedValuePath(DependencyObject obj) => (string)obj.GetValue(SelectedValuePathProperty);
        public static void SetSelectedValuePath(DependencyObject obj, string value) => obj.SetValue(SelectedValuePathProperty, value);

        // =======================
        // 6. TargetValuePath
        // =======================
        public static readonly DependencyProperty TargetValuePathProperty =
            DependencyProperty.RegisterAttached("TargetValuePath", typeof(string), typeof(DataGridDropdownAssist),
                new PropertyMetadata(string.Empty));
        public static string GetTargetValuePath(DependencyObject obj) => (string)obj.GetValue(TargetValuePathProperty);
        public static void SetTargetValuePath(DependencyObject obj, string value) => obj.SetValue(TargetValuePathProperty, value);

        // =======================
        // 7. ★ ItemTemplate（新增）
        // =======================
        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.RegisterAttached("ItemTemplate", typeof(DataTemplate), typeof(DataGridDropdownAssist),
                new PropertyMetadata(null));
        public static DataTemplate GetItemTemplate(DependencyObject obj) => (DataTemplate)obj.GetValue(ItemTemplateProperty);
        public static void SetItemTemplate(DependencyObject obj, DataTemplate value) => obj.SetValue(ItemTemplateProperty, value);

        // =======================
        // 8. ★ PopupWidth（新增）
        // =======================
        public static readonly DependencyProperty PopupWidthProperty =
            DependencyProperty.RegisterAttached("PopupWidth", typeof(double), typeof(DataGridDropdownAssist),
                new PropertyMetadata(double.NaN));
        public static double GetPopupWidth(DependencyObject obj) => (double)obj.GetValue(PopupWidthProperty);
        public static void SetPopupWidth(DependencyObject obj, double value) => obj.SetValue(PopupWidthProperty, value);

        // =======================
        // 9. ★ SelectedItem（新增，双向绑定完整对象）
        // =======================
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItem",
                typeof(object),
                typeof(DataGridDropdownAssist),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));
        // =======================
        // 10. ★ SearchCommand（新增）
        // 触发时机：文字变化防抖后，CommandParameter = 当前输入的 keyword
        // =======================
        public static readonly DependencyProperty SearchCommandProperty =
            DependencyProperty.RegisterAttached("SearchCommand", typeof(ICommand), typeof(DataGridDropdownAssist),
                new PropertyMetadata(null));
        public static ICommand GetSearchCommand(DependencyObject obj) => (ICommand)obj.GetValue(SearchCommandProperty);
        public static void SetSearchCommand(DependencyObject obj, ICommand value) => obj.SetValue(SearchCommandProperty, value);

        // =======================
        // 11. ★ SearchDelay（新增，防抖毫秒数，默认 300ms）
        // =======================
        public static readonly DependencyProperty SearchDelayProperty =
            DependencyProperty.RegisterAttached("SearchDelay", typeof(int), typeof(DataGridDropdownAssist),
                new PropertyMetadata(300));
        public static int GetSearchDelay(DependencyObject obj) => (int)obj.GetValue(SearchDelayProperty);
        public static void SetSearchDelay(DependencyObject obj, int value) => obj.SetValue(SearchDelayProperty, value);

        public static object GetSelectedItem(DependencyObject obj) => obj.GetValue(SelectedItemProperty);
        public static void SetSelectedItem(DependencyObject obj, object value) => obj.SetValue(SelectedItemProperty, value);

        // -------------------------------------------------------
        // 外部/ViewModel 赋值 SelectedItem 时，同步文字到 TextBox
        // -------------------------------------------------------
        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox tb)
            {
                // SelectValue 内部触发的赋值，跳过，避免死循环
                if (_isSelecting) return;

                var displayPath = GetDisplayMemberPath(tb);
                var newText = e.NewValue == null ? string.Empty : GetMemberText(e.NewValue, displayPath);

                if (!string.Equals(tb.Text, newText, StringComparison.Ordinal))
                    tb.Text = newText;
            }
        }

        // -------------------------------------------------------
        // 防止 OnSelectedItemChanged 与 SelectValue 互相触发
        // -------------------------------------------------------
        private static bool _isSelecting = false;

        // -------------------------------------------------------
        // 按 Window 缓存的 Popup 上下文（替代原有三个静态字段）
        // -------------------------------------------------------
        private class PopupContext
        {
            public Popup Popup;
            public ListBox ListBox;
            public TextBox CurrentTextBox;

            /// <summary>当前订阅的 ObservableCollection（API 模式），用于取消旧订阅</summary>
            public System.Collections.Specialized.INotifyCollectionChanged TrackedCollection;

            /// <summary>CollectionChanged 委托引用，取消订阅时使用</summary>
            public System.Collections.Specialized.NotifyCollectionChangedEventHandler CollectionChangedHandler;

            /// <summary>取消对旧集合的订阅</summary>
            public void UnsubscribeCollection()
            {
                if (TrackedCollection != null && CollectionChangedHandler != null)
                {
                    TrackedCollection.CollectionChanged -= CollectionChangedHandler;
                }
                TrackedCollection = null;
                CollectionChangedHandler = null;
            }
        }

        /// <summary>每个 Window 对应一套独立的 Popup 实例，避免跨窗口视觉树导致 Popup 不可见</summary>
        private static readonly Dictionary<Window, PopupContext> _popupCache =
            new Dictionary<Window, PopupContext>();

        // -------------------------------------------------------
        // 获取当前 TextBox 所在 Window 的 PopupContext，不存在则创建
        // -------------------------------------------------------
        private static PopupContext GetOrCreateContext(TextBox tb)
        {
            var window = Window.GetWindow(tb);
            if (window == null) return null;

            if (_popupCache.TryGetValue(window, out var ctx)) return ctx;

            // 创建新的 ListBox 和 Popup
            var listBox = new ListBox
            {
                Focusable = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MaxHeight = 250,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);

            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            itemStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"))));
            itemStyle.Triggers.Add(hoverTrigger);
            listBox.ItemContainerStyle = itemStyle;

            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 2, 0, 0),
                Child = listBox
            };

            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.15,
                BlurRadius = 15,
                ShadowDepth = 4,
                Direction = 270
            };

            var popup = new Popup
            {
                Child = border,
                AllowsTransparency = true,
                StaysOpen = true,
                Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.Fade
            };

            ctx = new PopupContext { Popup = popup, ListBox = listBox };
            listBox.PreviewMouseLeftButtonUp += (s, e) => SharedListBox_PreviewMouseLeftButtonUp(ctx, s, e);

            _popupCache[window] = ctx;

            // Window 关闭时清理对应条目，防止内存泄漏
            window.Closed += (s, e) =>
            {
                if (s is Window w) _popupCache.Remove(w);
            };

            return ctx;
        }

        // -------------------------------------------------------
        // API 模式：将 ListBox.ItemsSource 直接指向 ObservableCollection，
        // 并订阅 CollectionChanged 以在数据到来时驱动 Popup.IsOpen。
        // 切换 TextBox 时先取消旧订阅，避免重复监听。
        // -------------------------------------------------------
        private static void SubscribeToItemsSource(PopupContext ctx, TextBox tb)
        {
            // 取消上一次的订阅
            ctx.UnsubscribeCollection();

            var source = GetItemsSource(tb);
            if (source == null) return;

            // 直接让 ListBox 指向同一个 ObservableCollection，WPF 自动响应 CollectionChanged
            ctx.ListBox.ItemsSource = source;

            // 仅对 INotifyCollectionChanged 订阅，驱动 Popup 开关
            if (source is System.Collections.Specialized.INotifyCollectionChanged observable)
            {
                System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = (s, e) =>
                {
                    // CollectionChanged 可能在后台线程触发，切回 UI 线程
                    ctx.ListBox.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(() =>
                        {
                            if (ctx.CurrentTextBox == null) return;
                            if (ctx.ListBox.Items.Count > 0)
                            {
                                if (!ctx.Popup.IsOpen)
                                    ctx.Popup.IsOpen = true;
                            }
                            else
                            {
                                ctx.Popup.IsOpen = false;
                            }
                        }));
                };

                observable.CollectionChanged += handler;
                ctx.TrackedCollection = observable;
                ctx.CollectionChangedHandler = handler;
            }
        }

        // -------------------------------------------------------

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBox tb)) return;

            // 先全部解绑，防止重复注册
            tb.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
            tb.LostKeyboardFocus -= TextBox_LostKeyboardFocus;
            tb.TextChanged -= TextBox_TextChanged;
            tb.PreviewKeyDown -= TextBox_PreviewKeyDown;
            tb.Loaded -= TextBox_Loaded;

            // ItemsSource 变为 null（行退出编辑）时取消集合订阅
            if (e.NewValue == null)
            {
                var ctx = GetOrCreateContext(tb);
                ctx?.UnsubscribeCollection();
                if (ctx != null) ctx.CurrentTextBox = null;
            }

            if (e.NewValue != null)
            {
                tb.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                tb.LostKeyboardFocus += TextBox_LostKeyboardFocus;
                tb.TextChanged += TextBox_TextChanged;
                tb.PreviewKeyDown += TextBox_PreviewKeyDown;

                // ★ 关键修复：DataGrid 的 EditingElementStyle 在 GotKeyboardFocus 之后才被应用，
                //   导致首次聚焦时事件处理器尚未注册，ctx.CurrentTextBox 始终为 null，
                //   Popup 永远不会打开。
                //   修复：注册完事件后立即检查 TextBox 是否已持有键盘焦点，是则补调初始化；
                //   若 TextBox 尚未进入视觉树（IsLoaded=false），挂 Loaded 事件后再检查一次。
                if (tb.IsLoaded)
                {
                    if (tb.IsKeyboardFocused)
                        InitializePopupForTextBox(tb);
                }
                else
                {
                    tb.Loaded += TextBox_Loaded;
                }
            }
        }

        /// <summary>
        /// TextBox 首次进入视觉树后触发（仅一次），检查是否已持有键盘焦点并补初始化 Popup
        /// </summary>
        private static void TextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            tb.Loaded -= TextBox_Loaded; // 只触发一次，立即解绑
            if (tb.IsKeyboardFocused)
                InitializePopupForTextBox(tb);
        }

        // -------------------------------------------------------
        // 聚焦时把当前 TextBox 的模板/DisplayMemberPath 同步到共享 ListBox
        // -------------------------------------------------------
        private static void ApplyTemplateToListBox(TextBox tb, ListBox listBox)
        {
            var itemTemplate = GetItemTemplate(tb);
            var displayPath = GetDisplayMemberPath(tb);

            if (itemTemplate != null)
            {
                // 外部注入了模板 → 优先使用，清掉 DisplayMemberPath（两者互斥）
                listBox.ItemTemplate = itemTemplate;
                listBox.DisplayMemberPath = string.Empty;
            }
            else
            {
                // 没有模板 → 退回纯文字 DisplayMemberPath 模式
                listBox.ItemTemplate = null;
                listBox.DisplayMemberPath = displayPath;
            }
        }

        /// <summary>
        /// 核心初始化逻辑：设置 ctx.CurrentTextBox、PlacementTarget、宽度、模板、ItemsSource，
        /// 并决定是否打开 Popup。
        /// 由 TextBox_GotKeyboardFocus 和 OnItemsSourceChanged 补触发两处共同调用，
        /// 解决 EditingElementStyle 应用时序晚于 GotKeyboardFocus 的竞争问题。
        /// API 模式（绑定了 SearchCommand）：直接将 ListBox.ItemsSource 指向 ObservableCollection，
        /// 订阅 CollectionChanged 驱动 Popup，不走 FilterItems 快照，否则数据返回前列表始终为空。
        /// 本地模式：沿用 FilterItems 快照过滤，立即打开 Popup。
        /// </summary>
        private static void InitializePopupForTextBox(TextBox tb)
        {
            var ctx = GetOrCreateContext(tb);
            if (ctx == null) return;

            ctx.CurrentTextBox = tb;
            ctx.Popup.PlacementTarget = tb;

            var explicitWidth = GetPopupWidth(tb);
            ctx.Popup.Width = double.IsNaN(explicitWidth) ? Math.Max(tb.ActualWidth, 150) : explicitWidth;
            ctx.Popup.MinWidth = 150;

            ApplyTemplateToListBox(tb, ctx.ListBox);

            var hasSearchCommand = GetSearchCommand(tb) != null;
            if (hasSearchCommand)
            {
                // API 模式：直接绑定 ObservableCollection，由 CollectionChanged 驱动 Popup.IsOpen
                // 不走 FilterItems，避免拿到空快照后 Popup 被立即关闭
                SubscribeToItemsSource(ctx, tb);
            }
            else
            {
                // 本地模式：快照过滤，结果不为空时立即打开
                ctx.ListBox.ItemsSource = FilterItems(
                    GetItemsSource(tb), tb.Text, GetDisplayMemberPath(tb), GetFilterMemberPaths(tb));

                if (ctx.ListBox.Items.Count > 0)
                    ctx.Popup.IsOpen = true;
            }
        }

        /// <summary>TextBox 获得键盘焦点时触发，委托给核心初始化方法</summary>
        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
                InitializePopupForTextBox(tb);
        }

        private static void TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                EnsureTextInItems(tb);
                var ctx = GetOrCreateContext(tb);
                if (ctx != null)
                {
                    if (ctx.Popup?.IsOpen == true) ctx.Popup.IsOpen = false;
                    // 失焦时清空 CurrentTextBox，取消集合订阅，避免 Popup 在非聚焦状态被意外打开
                    ctx.CurrentTextBox = null;
                    ctx.UnsubscribeCollection();
                }
            }
        }


        private static System.Windows.Threading.DispatcherTimer _searchDebounceTimer;
        private static string _pendingKeyword;

        private static void EnsureDebounceTimer()
        {
            if (_searchDebounceTimer != null) return;
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer();
            _searchDebounceTimer.Tick += OnSearchDebounceTimerTick;
        }

        private static void OnSearchDebounceTimerTick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            // 从所有缓存的 context 中找到当前聚焦的 TextBox
            TextBox currentTb = null;
            foreach (var ctx in _popupCache.Values)
            {
                if (ctx.CurrentTextBox != null)
                {
                    currentTb = ctx.CurrentTextBox;
                    break;
                }
            }

            if (currentTb == null) return;

            var command = GetSearchCommand(currentTb);
            if (command != null && command.CanExecute(_pendingKeyword))
                command.Execute(_pendingKeyword);
        }



        private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is TextBox tb)) return;

            // ★ 修复回填死循环：SelectValue 回填文字时 _isSelecting=true，
            //   此时直接退出，不触发防抖计时器，不重新搜索
            if (_isSelecting) return;

            var ctx = GetOrCreateContext(tb);
            if (ctx == null || ctx.CurrentTextBox != tb) return;

            var command = GetSearchCommand(tb);
            if (command != null)
            {
                // API 模式：不走 FilterItems 快照（防止空快照关闭 Popup）
                // Popup 的开关完全由 CollectionChanged 驱动
                // 关键词为空时直接关闭并停止计时器
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    _searchDebounceTimer?.Stop();
                    ctx.Popup.IsOpen = false;
                    return;
                }

                // ★ 触发 SearchCommand（防抖）
                EnsureDebounceTimer();
                _pendingKeyword = tb.Text;
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(GetSearchDelay(tb));
                _searchDebounceTimer.Start();
            }
            else
            {
                // 本地模式：快照过滤，即时刷新
                ctx.ListBox.ItemsSource = FilterItems(
                    GetItemsSource(tb), tb.Text, GetDisplayMemberPath(tb), GetFilterMemberPaths(tb));

                if (ctx.ListBox.Items.Count > 0)
                {
                    if (!ctx.Popup.IsOpen) ctx.Popup.IsOpen = true;
                }
                else
                {
                    ctx.Popup.IsOpen = false;
                }
            }
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox tb)) return;
            var ctx = GetOrCreateContext(tb);
            if (ctx == null || ctx.Popup?.IsOpen != true || ctx.ListBox.Items.Count == 0) return;

            switch (e.Key)
            {
                case Key.Down:
                    if (ctx.ListBox.SelectedIndex < ctx.ListBox.Items.Count - 1)
                        ctx.ListBox.SelectedIndex++;
                    ctx.ListBox.ScrollIntoView(ctx.ListBox.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (ctx.ListBox.SelectedIndex > 0)
                        ctx.ListBox.SelectedIndex--;
                    ctx.ListBox.ScrollIntoView(ctx.ListBox.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Enter:
                case Key.Tab:
                    if (ctx.ListBox.SelectedItem != null)
                    {
                        SelectValue(ctx, ctx.ListBox.SelectedItem);
                        e.Handled = true;
                    }
                    break;

                case Key.Escape:
                    ctx.Popup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private static void SharedListBox_PreviewMouseLeftButtonUp(PopupContext ctx, object sender, MouseButtonEventArgs e)
        {
            if (ctx.CurrentTextBox == null) return;
            if (ItemsControl.ContainerFromElement(ctx.ListBox, e.OriginalSource as DependencyObject)
                is ListBoxItem item)
            {
                SelectValue(ctx, item.DataContext);
                e.Handled = true;
            }
        }

        private static void SelectValue(PopupContext ctx, object selectedItem)
        {
            var tb = ctx.CurrentTextBox;
            if (tb == null || selectedItem == null) return;

            _isSelecting = true;
            try
            {
                // 1. 回填显示文字
                var displayPath = GetDisplayMemberPath(tb);
                var textValue = string.IsNullOrEmpty(displayPath)
                    ? selectedItem.ToString()
                    : selectedItem.GetType().GetProperty(displayPath)
                                  ?.GetValue(selectedItem)?.ToString() ?? string.Empty;

                tb.Text = textValue;
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

                // 2. ★ 回填完整对象到 SelectedItem（有绑定时触发 ViewModel，没绑定时无副作用）
                tb.SetValue(SelectedItemProperty, selectedItem);

                // 3. 兼容旧的 TargetValuePath / SelectedValuePath（没设置时直接跳过）
                SyncTargetValue(tb, selectedItem);

                tb.CaretIndex = tb.Text.Length;
                ctx.Popup.IsOpen = false;
            }
            finally
            {
                _isSelecting = false;
            }
        }

        private static void EnsureTextInItems(TextBox tb)
        {
            if (tb == null || !GetRequireItemMatch(tb)) return;

            var text = (tb.Text ?? string.Empty).Trim();
            var bindingExpr = tb.GetBindingExpression(TextBox.TextProperty);

            if (string.IsNullOrEmpty(text))
            {
                bindingExpr?.UpdateSource();
                ClearTargetValue(tb);
                ClearSelectedItem(tb);
                return;
            }

            var displayPath = GetDisplayMemberPath(tb);
            var filterPaths = ParseFilterPaths(GetFilterMemberPaths(tb), displayPath);

            if (TryFindExactMatch(GetItemsSource(tb), text, filterPaths, out var matchedItem))
            {
                var canonical = GetMemberText(matchedItem, displayPath);
                if (!string.IsNullOrEmpty(canonical) &&
                    !string.Equals(tb.Text, canonical, StringComparison.Ordinal))
                    tb.Text = canonical;

                _isSelecting = true;
                try
                {
                    tb.SetValue(SelectedItemProperty, matchedItem);
                    SyncTargetValue(tb, matchedItem);
                }
                finally
                {
                    _isSelecting = false;
                }
            }
            else
            {
                tb.Text = string.Empty;
                ClearTargetValue(tb);
                ClearSelectedItem(tb);
            }

            bindingExpr?.UpdateSource();
        }

        // ★ 清空 SelectedItem（文字清空或不匹配时）
        private static void ClearSelectedItem(TextBox tb)
        {
            _isSelecting = true;
            try { tb.SetValue(SelectedItemProperty, null); }
            finally { _isSelecting = false; }
        }

        private static void SyncTargetValue(TextBox tb, object selectedItem)
        {
            if (tb == null || selectedItem == null) return;

            var sourcePath = GetSelectedValuePath(tb);
            var targetPath = GetTargetValuePath(tb);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath)) return;

            var sourceProp = selectedItem.GetType().GetProperty(sourcePath);
            if (sourceProp == null) return;

            var dataContext = tb.DataContext;
            var targetProp = dataContext?.GetType().GetProperty(targetPath);
            if (targetProp == null || !targetProp.CanWrite) return;

            try
            {
                var sourceValue = sourceProp.GetValue(selectedItem);
                var targetType = Nullable.GetUnderlyingType(targetProp.PropertyType) ?? targetProp.PropertyType;
                var safeValue = sourceValue == null ? null : Convert.ChangeType(sourceValue, targetType);
                targetProp.SetValue(dataContext, safeValue);
            }
            catch { }
        }

        private static void ClearTargetValue(TextBox tb)
        {
            if (tb == null) return;
            var targetPath = GetTargetValuePath(tb);
            if (string.IsNullOrEmpty(targetPath)) return;

            var dataContext = tb.DataContext;
            var targetProp = dataContext?.GetType().GetProperty(targetPath);
            if (targetProp == null || !targetProp.CanWrite) return;

            try
            {
                var defaultValue = targetProp.PropertyType.IsValueType
                    ? Activator.CreateInstance(targetProp.PropertyType)
                    : null;
                targetProp.SetValue(dataContext, defaultValue);
            }
            catch { }
        }

        // ======================== 辅助方法 ========================

        private static IList FilterItems(IEnumerable source, string keyword,
                                          string displayPath, string filterMemberPaths)
        {
            var list = new List<object>();
            if (source == null) return list;
            var filterPaths = ParseFilterPaths(filterMemberPaths, displayPath);

            foreach (var item in source)
            {
                if (item == null) continue;
                bool matched = string.IsNullOrEmpty(keyword);
                if (!matched)
                {
                    foreach (var path in filterPaths)
                    {
                        var text = GetMemberText(item, path);
                        if (!string.IsNullOrEmpty(text) &&
                            text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matched = true;
                            break;
                        }
                    }
                }
                if (matched) list.Add(item);
            }
            return list;
        }

        private static List<string> ParseFilterPaths(string filterMemberPaths, string displayPath)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(filterMemberPaths))
            {
                result.AddRange(
                    filterMemberPaths
                        .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
            }
            if (result.Count == 0)
                result.Add(!string.IsNullOrWhiteSpace(displayPath) ? displayPath : string.Empty);
            return result;
        }

        private static string GetMemberText(object item, string memberPath)
        {
            if (item == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(memberPath)) return item.ToString() ?? string.Empty;
            return item.GetType().GetProperty(memberPath)?.GetValue(item)?.ToString() ?? string.Empty;
        }

        private static bool TryFindExactMatch(IEnumerable source, string input,
                                               IEnumerable<string> paths, out object matchedItem)
        {
            matchedItem = null;
            if (source == null) return false;
            var normalized = (input ?? string.Empty).Trim();

            foreach (var item in source)
            {
                if (item == null) continue;
                foreach (var path in paths)
                {
                    if (string.Equals((GetMemberText(item, path) ?? string.Empty).Trim(),
                                      normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedItem = item;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}