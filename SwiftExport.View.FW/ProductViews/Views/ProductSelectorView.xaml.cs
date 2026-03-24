using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.ProductViews.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExport.View.FW.ProductViews.Views
{
    public partial class ProductSelectorWindow : Window
    {
        // 专门供 VSTO/Excel 获取最终选择结果的只读属性
        public List<ProductDto> SelectedProducts => ViewModel?.SelectedProducts.ToList() ?? new List<ProductDto>();

        private ProductSelectorViewModel ViewModel => DataContext as ProductSelectorViewModel;

        public ProductSelectorWindow(ProductSelectorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 订阅 ViewModel 的关闭请求
            viewModel.RequestClose += (isConfirmed) =>
            {
                if (isConfirmed)
                {
                    // 如果是“确定”，赋 true 会让 WPF 完美地自动关闭窗口，并返回 true
                    this.DialogResult = true;
                }
                else
                {
                    // 如果是“取消”，WPF 如果偷懒不关，我们就强行关
                    try { this.DialogResult = false; } catch { }
                    this.Close();
                }
            };

            // 订阅 ViewModel 的隐藏请求
            viewModel.RequestHide += () =>
            {
                // WPF 的 Hide() 会暂时隐藏窗口。
                // 注意：如果窗口是用 ShowDialog() 打开的，调用 Hide() 会导致 ShowDialog() 立即返回 null/false！
                // 但窗口对象并没有被销毁，你在外层的 VSTO 代码里可以再次调用 window.ShowDialog() 把他唤醒。
                this.Hide();
            };

            // 滚动到顶部
            this.Loaded += (s, e) =>
            {
                if (this.DataContext is ProductSelectorViewModel vm)
                {
                    // 3. 订阅事件：收到通知就滚到第一行
                    vm.RequestScrollToTop += () =>
                    {
                        // 必须在 UI 线程闲置时执行，否则可能数据还没渲染出来滚不动
                        this.Dispatcher.InvokeAsync(() =>
                        {
                            if (SourceDataGrid.Items.Count > 0)
                            {
                                //这是 WPF 最简单的滚到顶部的方法：让它去“看”第一项
                                SourceDataGrid.ScrollIntoView(SourceDataGrid.Items[0]);
                            }
                        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    };
                }
            };

        }

        // ================= 获取多选项并传给 ViewModel =================
        private void SourceDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.TempSelectedSourceItems = SourceDataGrid.SelectedItems.Cast<ProductDto>().ToList();
        }

        private void TargetDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.TempSelectedTargetItems = TargetDataGrid.SelectedItems.Cast<ProductDto>().ToList();
        }

        // ================= 双击快捷操作 =================
        private void SourceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SourceDataGrid.SelectedItem is ProductDto item && ViewModel != null)
                ViewModel.SelectItemsCommand.Execute(new[] { item });
        }

        private void TargetDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TargetDataGrid.SelectedItem is ProductDto item && ViewModel != null)
                ViewModel.RemoveItemsCommand.Execute(new[] { item });
        }

        // ================= 无限滚动 (触底加载) =================
        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 1. 只有当垂直滚动发生变化时才处理 (防止横向滚动触发)
            if (e.VerticalChange == 0) return;

            // 2. ★ 核心：只有用户是在【向下】滚动时才加载！
            //    当数据清空时，VerticalChange 是负数（向上弹），这句判断直接把死循环掐死了。
            if (e.VerticalChange < 0) return;

            // 3. 距离底部还有 10 像素时触发
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10)
            {
                // 4. 双重保险：如果正在加载，绝对不要再次触发
                if (this.DataContext is ProductSelectorViewModel vm && !vm.IsLoading)
                {
                    // 触发加载下一页
                    vm.LoadDataCommand.Execute(null);
                }
            }
        }







    }
}