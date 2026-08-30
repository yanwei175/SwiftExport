xaml



```xaml
    <!-- 
    ========================================================================
    组件使用说明 (PageNavigator):

    1. 绑定模式:
       该组件设计为“命令驱动（Command-Driven）”，必须使用单向绑定（Mode=OneWay）。
       用户的一切翻页操作（点击页码、修改每页条数）都会被转换为一个 PageChangeRequest 对象，
       并通过 PageChangeCommand 发送给 ViewModel。

    2. 拦截机制:
       因为使用的是 Mode=OneWay，UI 组件自身的值只有在 ViewModel 中实际修改了绑定的属性后才会改变。
       这使得我们可以在 ViewModel 的 Command 处理函数中，优先检查数据是否“未保存（IsDirty）”。
       - 如果用户放弃翻页（例如弹窗点“取消”），则 ViewModel 不更改绑定的属性，组件保持在当前页码。
       - 如果用户同意翻页，则 ViewModel 实际更新 PageIndex/PageSize，调用 API 加载数据，组件 UI 同步刷新。

    3. 基础使用示例:
       <controls:PageNavigator TotalCount="{Binding TotalCount}" 
                               PageIndex="{Binding PageIndex, Mode=OneWay}" 
                               PageSize="{Binding PageSize, Mode=OneWay}" 
                               PageChangeCommand="{Binding PageChangeCommand}" />
    ========================================================================
    -->

    <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Background="Transparent" Height="30">

        <!-- 总条数 -->
        <TextBlock Text="共 " Foreground="{DynamicResource Brush_Txt_Primary}" VerticalAlignment="Center"/>
        <TextBlock Text="{Binding TotalCount, ElementName=Root, Mode=OneWay}" Foreground="{DynamicResource Brush_Txt_Primary}" FontWeight="Bold" VerticalAlignment="Center"/>
        <TextBlock Text=" 条数据" Foreground="{DynamicResource Brush_Txt_Primary}" VerticalAlignment="Center" Margin="0,0,15,0"/>

        <Rectangle Width="1" Height="14" Fill="{DynamicResource Brush_Txt_Secondary}" Margin="0,0,15,0" VerticalAlignment="Center"/>

        <!-- 每页条数 -->
        <TextBlock Text="每页 " VerticalAlignment="Center" Foreground="{DynamicResource Brush_Txt_Primary}"/>
        <ComboBox x:Name="CboPageSize" 
                  Style="{DynamicResource BaseComboBox}" 
                  Background="Transparent" Width="55" Margin="5,0" Padding="4,2" 
                  HorizontalContentAlignment="Left" VerticalContentAlignment="Center"
                  ItemsSource="{Binding PageSizeOptions, ElementName=Root, Mode=OneWay}"
                  SelectionChanged="CboPageSize_SelectionChanged"/>
        <TextBlock Text="条" VerticalAlignment="Center" Foreground="{DynamicResource Brush_Txt_Primary}" Margin="0,0,15,0"/>

        <!-- 动态页码列表 (已移除导航图标，通过数字直接跳转) -->
        <ItemsControl ItemsSource="{Binding PageItems, ElementName=Root}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal"/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Button Content="{Binding DisplayText}" 
                            IsEnabled="{Binding IsClickable}"
                            Click="BtnPage_Click"
                            Margin="2,0" MinWidth="40">
                        <Button.Style>
                            <Style TargetType="Button" BasedOn="{StaticResource ButtonText}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsCurrent}" Value="True">
                                        <Setter Property="Foreground" Value="DodgerBlue"/>
                                        <Setter Property="FontWeight" Value="Regular"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                    </Button>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

    </StackPanel>
</UserControl>
```



.cs

```c#
    public partial class PageNavigator : UserControl
    {
        public PageNavigator()
        {
            InitializeComponent();
        }

        #region Dependency Properties

        public static readonly DependencyProperty TotalCountProperty =
            DependencyProperty.Register("TotalCount", typeof(int), typeof(PageNavigator), new PropertyMetadata(0, OnPaginationChanged));
        public int TotalCount
        {
            get => (int)GetValue(TotalCountProperty);
            set => SetValue(TotalCountProperty, value);
        }

        public static readonly DependencyProperty PageIndexProperty =
            DependencyProperty.Register("PageIndex", typeof(int), typeof(PageNavigator), new PropertyMetadata(1, OnPaginationChanged));
        public int PageIndex
        {
            get => (int)GetValue(PageIndexProperty);
            set => SetValue(PageIndexProperty, value);
        }

        public static readonly DependencyProperty PageSizeProperty =
            DependencyProperty.Register("PageSize", typeof(int), typeof(PageNavigator), new PropertyMetadata(50, OnPaginationChanged));
        public int PageSize
        {
            get => (int)GetValue(PageSizeProperty);
            set => SetValue(PageSizeProperty, value);
        }

        public static readonly DependencyProperty PageSizeOptionsProperty =
            DependencyProperty.Register("PageSizeOptions", typeof(IEnumerable<int>), typeof(PageNavigator), new PropertyMetadata(new[] { 10, 20, 50, 100, 200 }));
        public IEnumerable<int> PageSizeOptions
        {
            get => (IEnumerable<int>)GetValue(PageSizeOptionsProperty);
            set => SetValue(PageSizeOptionsProperty, value);
        }

        public static readonly DependencyProperty PageChangeCommandProperty =
            DependencyProperty.Register("PageChangeCommand", typeof(ICommand), typeof(PageNavigator), new PropertyMetadata(null));
        public ICommand PageChangeCommand
        {
            get => (ICommand)GetValue(PageChangeCommandProperty);
            set => SetValue(PageChangeCommandProperty, value);
        }

        #endregion

        #region Internal Properties

        public static readonly DependencyProperty PageItemsProperty =
            DependencyProperty.Register("PageItems", typeof(ObservableCollection<PageItem>), typeof(PageNavigator), new PropertyMetadata(new ObservableCollection<PageItem>()));
        public ObservableCollection<PageItem> PageItems
        {
            get => (ObservableCollection<PageItem>)GetValue(PageItemsProperty);
            private set => SetValue(PageItemsProperty, value);
        }

        #endregion

        private static void OnPaginationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PageNavigator control)
            {
                control.CboPageSize.SelectionChanged -= control.CboPageSize_SelectionChanged;
                control.CboPageSize.SelectedItem = control.PageSize;
                control.CboPageSize.SelectionChanged += control.CboPageSize_SelectionChanged;

                control.UpdatePageItems();
            }
        }
        /// <summary>
        /// 核心：计算页码分布 (固定 9 个槽位显示)
        /// </summary>
        private void UpdatePageItems()
        {
            int total = PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
            int current = PageIndex;

            var items = new ObservableCollection<PageItem>();
            if (total <= 0)
            {
                PageItems = items;
                return;
            }

            // 规则：
            // 1. 如果总页数 <= 9，直接显示全部。
            // 2. 如果总页数 > 9，固定显示 9 个槽位（含省略号）。

            if (total <= 9)
            {
                for (int i = 1; i <= total; i++) items.Add(new PageItem(i, current));
            }
            else
            {
                // 槽位计算
                int start, end;

                if (current <= 5)
                {
                    // 在首页附近: 1 2 3 4 5 6 7 ... total (9个槽位)
                    start = 1;
                    end = 7;
                    for (int i = start; i <= end; i++) items.Add(new PageItem(i, current));
                    items.Add(new PageItem(-1, current)); // 第8位
                    items.Add(new PageItem(total, current)); // 第9位
                }
                else if (current >= total - 4)
                {
                    // 在末页附近: 1 ... total-6 total-5 total-4 total-3 total-2 total-1 total (9个槽位)
                    items.Add(new PageItem(1, current)); // 第1位
                    items.Add(new PageItem(-1, current)); // 第2位
                    start = total - 6;
                    end = total;
                    for (int i = start; i <= end; i++) items.Add(new PageItem(i, current));
                }
                else
                {
                    // 在中间部分: 1 ... 48 49 50 51 52 ... total (9个槽位)
                    items.Add(new PageItem(1, current)); // 第1位
                    items.Add(new PageItem(-1, current)); // 第2位

                    start = current - 2;
                    end = current + 2;
                    for (int i = start; i <= end; i++) items.Add(new PageItem(i, current));

                    items.Add(new PageItem(-1, current)); // 第8位
                    items.Add(new PageItem(total, current)); // 第9位
                }
            }

            PageItems = items;
        }

        private void RequestPageChange(int newIndex, int newSize)
        {
            if (newIndex == PageIndex && newSize == PageSize) return;

            var request = new PageChangeRequest
            {
                OldIndex = PageIndex,
                NewIndex = newIndex,
                OldSize = PageSize,
                NewSize = newSize
            };

            if (PageChangeCommand?.CanExecute(request) == true)
            {
                PageChangeCommand.Execute(request);
            }
            else
            {
                CboPageSize.SelectionChanged -= CboPageSize_SelectionChanged;
                CboPageSize.SelectedItem = PageSize;
                CboPageSize.SelectionChanged += CboPageSize_SelectionChanged;
            }
        }

        #region UI Event Handlers
        private void BtnPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PageItem item && item.PageIndex != -1)
            {
                RequestPageChange(item.PageIndex, PageSize);
            }
        }

        private void CboPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboPageSize.SelectedItem is int newSize)
            {
                RequestPageChange(1, newSize);
            }
        }
        #endregion
    }

    public class PageItem
    {
        public int PageIndex { get; }
        public bool IsCurrent { get; }
        public bool IsClickable => PageIndex != -1 && !IsCurrent;
        public string DisplayText => PageIndex == -1 ? "..." : PageIndex.ToString();

        public PageItem(int pageIndex, int currentIndex)
        {
            PageIndex = pageIndex;
            IsCurrent = pageIndex == currentIndex;
        }
    }
```