using GalaSoft.MvvmLight.Messaging;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.ProductViews.Messages;
using SwiftExport.View.FW.ProductViews.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.ProductViews.Views
{
    public partial class ProductSelectorWindow : Window
    {
        /// <summary>
        /// 获取用户最终选中的产品列表。
        /// 若用户取消（DialogResult != true），返回 null。
        /// 通常在 ShowDialog() 返回后读取。
        /// </summary>
        public List<ProductDto> SelectedProducts
        {
            get
            {
                if (this.DialogResult != true) return null;
                return ViewModel?.SelectedProducts?.ToList();
            }
        }

        private ProductSelectorViewModel ViewModel => DataContext as ProductSelectorViewModel;

        public ProductSelectorWindow(ProductSelectorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 通过 Messenger 订阅选择结果（以 viewModel 实例为 token，防止多窗口实例之间串台）
            Messenger.Default.Register<ProductSelectionResultMessage>(this, viewModel, OnSelectionResult);

            // 窗口关闭时注销 Messenger，避免内存泄漏
            this.Closed += (s, e) =>
            {
                Messenger.Default.Unregister<ProductSelectionResultMessage>(this, ViewModel);
            };

            // 加载完成后订阅"滚动到顶部"事件 + 首次焦点定位
            this.Loaded += (s, e) =>
            {
                if (this.DataContext is ProductSelectorViewModel vm)
                {
                    // 收到通知就在 UI 线程空闲时滚到第一行
                    vm.RequestScrollToTop += () =>
                    {
                        this.Dispatcher.InvokeAsync(() =>
                        {
                            if (SourceDataGrid.Items.Count > 0)
                                SourceDataGrid.ScrollIntoView(SourceDataGrid.Items[0]);
                        }, DispatcherPriority.ContextIdle);
                    };
                }

                // 中文注释：窗口首次显示时，将焦点放到搜索框并尽量切换到英文输入模式。
                FocusSearchTextBoxAndUseEnglishMode();
            };
        }

        /// <summary>
        /// 中文注释：聚焦产品搜索框，并关闭 IME（优先英文输入）。
        /// </summary>
        private void FocusSearchTextBoxAndUseEnglishMode()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (SearchTextBox == null) return;

                SearchTextBox.Focus();
                SearchTextBox.CaretIndex = (SearchTextBox.Text ?? string.Empty).Length;
            }, DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 此窗口仅支持以对话框方式打开，请使用 ShowDialog()。
        /// </summary>
        public new void Show()
        {
            throw new InvalidOperationException("ProductSelectorWindow 只允许以对话框方式打开，请使用 ShowDialog()。");
        }

        // ================= Messenger 结果处理 =================

        /// <summary>
        /// 收到 ViewModel 广播的选择结果消息后关闭窗口。
        /// </summary>
        private void OnSelectionResult(ProductSelectionResultMessage msg)
        {
            // 立即注销，防止重复触发
            Messenger.Default.Unregister<ProductSelectionResultMessage>(this, ViewModel);

            if (msg.Result != null)
            {
                // 确认：设置 DialogResult = true 会自动关闭对话框
                this.DialogResult = true;
            }
            else
            {
                // 取消
                try { this.DialogResult = false; } catch { }
                this.Close();
            }
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

        // ================= 键盘快捷键 =================

        /// <summary>
        /// 搜索框按下方向键↓时，焦点自动移入左侧 DataGrid 并选中第一行。
        /// </summary>
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (SourceDataGrid.Items.Count > 0)
                {
                    SourceDataGrid.Focus();
                    SourceDataGrid.SelectedIndex = 0;
                    // 让行容器本身获得键盘焦点，使方向键可以继续导航
                    var row = SourceDataGrid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
                    row?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// 左侧 DataGrid 的键盘操作：
        ///   Enter        → 将选中行穿梭到右侧
        ///   Ctrl + Enter → 直接执行确认（相当于点"确定"按钮）
        /// </summary>
        private void SourceDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    // Ctrl+Enter → 直接确认
                    ViewModel?.ConfirmCommand.Execute(null);
                }
                else
                {
                    // Enter → 穿梭到右侧
                    ViewModel?.SelectItemsCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        // ================= 无限滚动 (触底加载) =================

        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 只有当垂直滚动发生变化时才处理（防止横向滚动触发）
            if (e.VerticalChange == 0) return;

            // 只有用户向下滚动时才加载（数据清空时 VerticalChange 为负，可防止死循环）
            if (e.VerticalChange < 0) return;

            // 距离底部还有 10 像素时触发下一页加载
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10)
            {
                // 用 IsBusy 防止重复触发
                if (this.DataContext is ProductSelectorViewModel vm && !vm.IsBusy)
                {
                    vm.LoadDataCommand.Execute(null);
                }
            }
        }
    }
}