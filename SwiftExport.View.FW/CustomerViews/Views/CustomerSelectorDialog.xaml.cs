using GalaSoft.MvvmLight.Messaging;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.View.FW.CustomerViews.Messages;
using SwiftExport.View.FW.CustomerViews.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.CustomerViews.Views
{
    /// <summary>
    /// 客户选择对话框。
    /// 该窗口仅用于 Dialog 模式，供单据页面弹出选择客户，并返回完整客户信息。
    /// </summary>
    public partial class CustomerSelectorDialogWindow : Window
    {
        /// <summary>
        /// 获取用户最终选中的客户。
        /// 若用户取消（DialogResult != true），返回 null。
        /// </summary>
        public CustomerDto SelectedCustomer
        {
            get
            {
                if (DialogResult != true) return null;
                return ViewModel?.SelectedCustomer;
            }
        }

        private CustomerSelectorDialogViewModel ViewModel => DataContext as CustomerSelectorDialogViewModel;

        public CustomerSelectorDialogWindow(CustomerSelectorDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 中文注释：按 ViewModel 实例作为 token 订阅消息，避免多窗口实例之间串消息。
            Messenger.Default.Register<CustomerSelectionResultMessage>(this, viewModel, OnSelectionResult);

            // 中文注释：窗口关闭时注销消息订阅，避免内存泄漏。
            Closed += (s, e) =>
            {
                Messenger.Default.Unregister<CustomerSelectionResultMessage>(this, ViewModel);
            };

            Loaded += (s, e) =>
            {
                if (DataContext is CustomerSelectorDialogViewModel vm)
                {
                    // 中文注释：查询条件变化并重置分页后，让列表回到第一行，提升可用性。
                    vm.RequestScrollToTop += () =>
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (SourceDataGrid.Items.Count > 0)
                            {
                                SourceDataGrid.ScrollIntoView(SourceDataGrid.Items[0]);
                            }
                        }, DispatcherPriority.ContextIdle);
                    };
                }

                FocusSearchTextBox();
            };
        }

        /// <summary>
        /// 此窗口只允许通过 ShowDialog() 方式打开。
        /// </summary>
        public new void Show()
        {
            throw new InvalidOperationException("CustomerSelectorDialogWindow 只允许以对话框方式打开，请使用 ShowDialog()。");
        }

        /// <summary>
        /// 聚焦搜索框，方便用户打开后立即输入关键字。
        /// </summary>
        private void FocusSearchTextBox()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (SearchTextBox == null) return;

                SearchTextBox.Focus();
                SearchTextBox.CaretIndex = (SearchTextBox.Text ?? string.Empty).Length;
            }, DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 处理 ViewModel 广播的选择结果并关闭窗口。
        /// </summary>
        private void OnSelectionResult(CustomerSelectionResultMessage msg)
        {
            Messenger.Default.Unregister<CustomerSelectionResultMessage>(this, ViewModel);

            if (msg.Result != null)
            {
                DialogResult = true;
            }
            else
            {
                try { DialogResult = false; } catch { }
                Close();
            }
        }

        /// <summary>
        /// 搜索框按下方向键时，把焦点移动到数据列表首行。
        /// </summary>
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
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

        /// <summary>
        /// 双击数据行时快速确认。
        /// </summary>
        private void SourceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewModel?.ConfirmCommand.Execute(null);
        }

        /// <summary>
        /// 列表键盘快捷键：Enter 确认，Esc 取消。
        /// </summary>
        private void SourceDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel?.ConfirmCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ViewModel?.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 触底加载下一页客户数据。
        /// </summary>
        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0) return;
            if (e.VerticalChange < 0) return;

            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10)
            {
                if (DataContext is CustomerSelectorDialogViewModel vm && !vm.IsBusy)
                {
                    vm.LoadDataCommand.Execute(null);
                }
            }
        }
    }
}
