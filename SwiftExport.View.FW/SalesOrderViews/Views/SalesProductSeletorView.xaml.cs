using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.SalesOrderViews.Views
{
    /// <summary>
    /// SalesProductSeletorView.xaml 的交互逻辑
    /// </summary>
    public partial class SalesProductSeletorView : Window
    {
        private SalesProductSelectorViewModel ViewModel => DataContext as SalesProductSelectorViewModel;

        /// <summary>
        /// 最终选中结果（仅在确认时可用）。
        /// </summary>
        public List<SalesProductDto> ResultData
        {
            get
            {
                if (DialogResult != true) return null;
                return ViewModel?.ResultData;
            }
        }

        public SalesProductSeletorView(SalesProductSelectorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            if (viewModel != null)
            {
                // 中文注释：由 ViewModel 统一控制确认/取消关闭，窗口只响应事件。
                viewModel.RequestClose += ViewModel_RequestClose;
            }

            Closed += (s, e) =>
            {
                if (ViewModel != null)
                {
                    ViewModel.RequestClose -= ViewModel_RequestClose;
                }
            };

            Loaded += (s, e) =>
            {
                if (DataContext is SalesProductSelectorViewModel vm)
                {
                    vm.RequestScrollToTop += OnRequestScrollToTop;
                }

                FocusSearchTextBox();
            };
        }

        /// <summary>
        /// 只允许以 ShowDialog 方式打开。
        /// </summary>
        public new void Show()
        {
            throw new InvalidOperationException("SalesProductSeletorView 只允许以对话框方式打开，请使用 ShowDialog()。");
        }

        private void ViewModel_RequestClose(object sender, bool ok)
        {
            if (ok)
            {
                DialogResult = true;
            }
            else
            {
                try { DialogResult = false; } catch { }
                Close();
            }
        }

        private void OnRequestScrollToTop()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (SourceDataGrid.Items.Count > 0)
                {
                    SourceDataGrid.ScrollIntoView(SourceDataGrid.Items[0]);
                }
            }, DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 首次打开聚焦搜索框，便于用户直接输入。
        /// </summary>
        private void FocusSearchTextBox()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (SearchBox == null) return;

                SearchBox.Focus();
                SearchBox.CaretIndex = (SearchBox.Text ?? string.Empty).Length;
            }, DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 左侧 DataGrid 选中项同步到 ViewModel 临时集合。
        /// </summary>
        private void SourceDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.TempSelectedSourceItems = SourceDataGrid.SelectedItems.Cast<SalesProductDto>().ToList();
            }
        }

        /// <summary>
        /// 右侧 DataGrid 选中项同步到 ViewModel 临时集合。
        /// </summary>
        private void TargetDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.TempSelectedTargetItems = TargetDataGrid.SelectedItems.Cast<SalesProductDto>().ToList();
            }
        }

        /// <summary>
        /// 左侧双击快捷右移。
        /// </summary>
        private void SourceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.SelectItemsCommand.Execute(null);
        }

        /// <summary>
        /// 右侧双击快捷左移。
        /// </summary>
        private void TargetDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.RemoveItemsCommand.Execute(null);
        }

        /// <summary>
        /// 左侧表格键盘操作：Enter 右移，Ctrl+Enter 确认。
        /// </summary>
        private void SourceDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    ViewModel?.ConfirmCommand.Execute(null);
                }
                else
                {
                    ViewModel?.SelectItemsCommand.Execute(null);
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel?.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 搜索框按下向下键时，将焦点移入左侧表格。
        /// </summary>
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Down) return;

            if (SourceDataGrid.Items.Count > 0)
            {
                SourceDataGrid.Focus();
                SourceDataGrid.SelectedIndex = 0;
                var row = SourceDataGrid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
                row?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }

            e.Handled = true;
        }

        // 中文注释：触底加载已移除，改由分页控件统一驱动翻页查询。
    }
}
