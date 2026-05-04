using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.DI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Controls
{
    /// <summary>
    /// 完全自治的颜色搜索选择控件
    /// </summary>
    public partial class ColorSelectorControl : UserControl
    {
        // 1. 对外暴露的选中项，支持双向绑定
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(ProductColorDto),
                typeof(ColorSelectorControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public ProductColorDto SelectedItem
        {
            get => (ProductColorDto)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // 2. 内部数据源依赖属性（用于 XAML 绑定到 SearchableTextBox）
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(
                nameof(Items),
                typeof(ObservableCollection<ProductColorDto>),
                typeof(ColorSelectorControl),
                new PropertyMetadata(null));

        public ObservableCollection<ProductColorDto> Items
        {
            get => (ObservableCollection<ProductColorDto>)GetValue(ItemsProperty);
            private set => SetValue(ItemsProperty, value);
        }

        private bool _isDataInitialized = false;

        public ColorSelectorControl()
        {
            InitializeComponent();
            Items = new ObservableCollection<ProductColorDto>();
            this.Loaded += OnLoadedAsync;
        }

        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            if (_isDataInitialized) return;
            _isDataInitialized = true;
            await LoadDataAsync();
        }

        /// <summary>
        /// 自治 API 加载逻辑
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                var api = IocProvider.GetService<IProductColorApi>();
                if (api == null) return;

                var result = await api.GetAllAsync();
                if (result?.Success == true && result.Data != null)
                {
                    Items.Clear();

                    //// 插入一个虚拟的“全部”选项，方便清空或全选
                    //Items.Add(new ProductColorDto
                    //{
                    //    ColorNameZh = "全部颜色",
                    //    ColorCodeUnique = "ALL",
                    //    ColorNameEn = "All Colors"
                    //});

                    foreach (var item in result.Data)
                    {
                        Items.Add(item);
                    }
                }
            }
            catch
            {
                // 静默处理 API 错误
            }
        }
    }
}