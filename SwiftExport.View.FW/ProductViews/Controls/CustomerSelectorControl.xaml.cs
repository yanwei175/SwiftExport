using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.View.FW.DI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Controls
{
    /// <summary>
    /// 客户选择自主控件
    /// </summary>
    public partial class CustomerSelectorControl : UserControl
    {
        // 1. 定义对外暴露的双向绑定属性 SelectedItem (根据API定义为 CustomerDto)
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(CustomerDto),
                typeof(CustomerSelectorControl),
                new FrameworkPropertyMetadata(
                    null, // 默认值
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));

        public CustomerDto SelectedItem
        {
            get => (CustomerDto)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // 2. 对外暴露 SelectedValuePath，调用方按 ComboBox 用法配置“取值字段”。
        public static readonly DependencyProperty SelectedValuePathProperty =
            DependencyProperty.Register(
                nameof(SelectedValuePath),
                typeof(string),
                typeof(CustomerSelectorControl),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string SelectedValuePath
        {
            get => (string)GetValue(SelectedValuePathProperty);
            set => SetValue(SelectedValuePathProperty, value);
        }

        // 3. 对外暴露 SelectedValue，调用方可直接绑定标量值（例如 CustomerCode/Id）。
        public static readonly DependencyProperty SelectedValueProperty =
            DependencyProperty.Register(
                nameof(SelectedValue),
                typeof(object),
                typeof(CustomerSelectorControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public object SelectedValue
        {
            get => GetValue(SelectedValueProperty);
            set => SetValue(SelectedValueProperty, value);
        }

        // 内部数据源属性，用于绑定 UI
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(
                nameof(Items),
                typeof(ObservableCollection<CustomerDto>),
                typeof(CustomerSelectorControl),
                new PropertyMetadata(new ObservableCollection<CustomerDto>()));

        public ObservableCollection<CustomerDto> Items
        {
            get => (ObservableCollection<CustomerDto>)GetValue(ItemsProperty);
            private set => SetValue(ItemsProperty, value);
        }

        public CustomerSelectorControl()
        {
            InitializeComponent();
            Items = new ObservableCollection<CustomerDto>();
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
        /// 使用全局 DI 获取 API 接口，实现数据集加载自治
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                var api = IocProvider.GetService<ICustomerApi>(); // 获取 API 接口实例
                if (api == null) return;

                var result = await api.GetAllAsync();
                if (result != null && result.Success && result.Data != null)
                {
                    Items.Clear();
                    //Items.Add(new CustomerDto { CustomerCode = "All Customer", CustomerName = "全部客户" }); // 添加默认选项
                    foreach (var code in result.Data)
                    {
                        Items.Add(code);
                    }
                }
            }
            catch
            {
                // 容错处理
            }
        }

        /// <summary>
        /// 当外部/内部更新 SelectedItem 时，若为空则同步清空 SelectedValue。
        /// 这样 ResetFilter 时两个绑定出口状态保持一致。
        /// </summary>
        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as CustomerSelectorControl;
            if (control == null) return;

            if (e.NewValue == null)
            {
                control.SelectedValue = null;
            }
        }

    }
}
