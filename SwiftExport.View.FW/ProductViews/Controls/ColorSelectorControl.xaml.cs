using GalaSoft.MvvmLight.Ioc;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Controls
{
    /// <summary>
    /// 颜色选择自主控件，包含下拉检索与中文、英文及色号模糊过滤
    /// </summary>
    public partial class ColorSelectorControl : UserControl
    {
        // 1. 定义对外暴露的选中的具体颜色实体 (ProductColorDto)
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(ProductColorDto),
                typeof(ColorSelectorControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ProductColorDto SelectedItem
        {
            get => (ProductColorDto)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // 内部数据源集合
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(
                nameof(Items),
                typeof(ObservableCollection<ProductColorDto>),
                typeof(ColorSelectorControl),
                new PropertyMetadata(new ObservableCollection<ProductColorDto>()));

        public ObservableCollection<ProductColorDto> Items
        {
            get => (ObservableCollection<ProductColorDto>)GetValue(ItemsProperty);
            private set => SetValue(ItemsProperty, value);
        }

        public ColorSelectorControl()
        {
            InitializeComponent();
            Items = new ObservableCollection<ProductColorDto>();
            Loaded += OnLoadedAsync;
        }


        private bool _isLoaded;
        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            _ = LoadDataAsync();
        }

        /// <summary>
        /// 从接口层自主提取全量颜色池以备展示
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                var api = IocProvider.Get<IProductColorApi>();
                if (api == null) return;

                var result = await api.GetAllAsync();
                if (result != null && result.Success && result.Data != null)
                {
                    Items.Clear();

                    Items.Add(new ProductColorDto { ColorCodeUnique = "全部颜色", ColorNameEn = "All Colors", ColorNameZh = "全部颜色", ColorCode = "全部颜色" });
                    // Optional: 为防止“全部”这一假选项影响，可以通过 Dto 构造一个"全部颜色"节点，或者纯清除搜索。
                    // 此处直接加载所有有效颜色
                    foreach (var item in result.Data)
                    {
                        Items.Add(item);
                    }
                }
            }
            catch
            {
                // 日志记录与吞出
            }
        }
    }
}
