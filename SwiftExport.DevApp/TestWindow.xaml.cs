using MateWpfThemes.FW.Services.Dialogs;
using System.Collections.ObjectModel;
using System.Windows;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// TestWindow.xaml 的交互逻辑
    /// </summary>
    public partial class TestWindow : Window
    {
        // 测试用的 ObservableCollection，WPF 的亲儿子，数据变动自动刷新 UI
        public ObservableCollection<CustomerModel> TestCustomers { get; set; }
        private int _newCustomerSeed = 1;

        public TestWindow()
        {
            InitializeComponent();
            // 1. 初始化测试数据
            TestCustomers = new ObservableCollection<CustomerModel>
            {
                new CustomerModel { CustomerName = "阿里巴巴", Remark = "大客户\n需要重点跟进" },
                new CustomerModel { CustomerName = "腾讯科技", Remark = "微信支付接口联调\n(Alt+Enter可换行)" },
                new CustomerModel { CustomerName = "字节跳动", Remark = "抖音电商项目下周一交接" }
            };

            // 2. 指定 DataContext 为当前窗口，这样 XAML 才能绑定到 TestCustomers
            this.DataContext = this;
        }

        // ==========================================
        // 弹窗测试逻辑
        // ==========================================
        private async void OpenDialog_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await DialogProvider.ShowConfirmAsync("系统警告", "确定要删库吗？");
            if (ok)
            {
                // 继续弹输入框
                _ = await DialogProvider.ShowInputAsync("请输入验证码", "");
            }
        }

        private async void CloseDialog_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await DialogProvider.ShowConfirmAsync("系统警告", "确定要删库吗？");
            if (ok)
            {
                // 继续弹输入框
                _ = await DialogProvider.ShowInputAsync("请输入验证码", "");
            }
        }

        // ==========================================
        // DataGrid 数据测试逻辑
        // ==========================================
        private void ClearData_Click(object sender, RoutedEventArgs e)
        {
            // 清空数据，用来测试 assist:DataGridAssist.EmptyText 是否生效
            TestCustomers.Clear();
        }

        private void AddData_Click(object sender, RoutedEventArgs e)
        {
            // 插入一条新数据，用来测试 assist:DataGridAssist.AutoScrollToNewItem 是否生效
            TestCustomers.Add(new CustomerModel
            {
                CustomerName = $"新客户{_newCustomerSeed++:D2}",
                Remark = "新录入的数据"
            });
        }
    }

    // 极其简单的数据模型
    public class CustomerModel
    {
        public string CustomerName { get; set; }
        public string Remark { get; set; }
    }


}

