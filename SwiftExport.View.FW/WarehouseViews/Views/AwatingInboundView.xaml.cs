using GalaSoft.MvvmLight.Messaging;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.View.FW.WarehouseViews.Messages;
using SwiftExport.View.FW.WarehouseViews.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.View.FW.WarehouseViews.Views
{
    /// <summary>
    /// AwatingInboundView.xaml 的交互逻辑。
    /// </summary>
    public partial class AwatingInboundView : Window
    {
        /// <summary>
        /// 获取用户最终选中的待入库行。
        /// 若用户取消（DialogResult != true），返回 null。
        /// </summary>
        public List<AwatingInboundDto> AwingOutPutList
        {
            get
            {
                if (DialogResult != true) return null;
                return ViewModel?.ResultData?.ToList();
            }
        }

        private AwatingInboundViewModel ViewModel => DataContext as AwatingInboundViewModel;

        public AwatingInboundView(AwatingInboundViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 通过 Messenger 订阅选择结果（以 viewModel 实例为 token，防止多窗口实例之间串台）
            Messenger.Default.Register<AwatingInboundSelectionResultMessage>(this, viewModel, OnSelectionResult);

            // 窗口关闭时注销 Messenger，避免内存泄漏
            Closed += (s, e) =>
            {
                Messenger.Default.Unregister<AwatingInboundSelectionResultMessage>(this, ViewModel);
            };

            // 加载完成后订阅"滚动到顶部"事件 + 首次焦点定位
            Loaded += (s, e) =>
            {
                if (DataContext is AwatingInboundViewModel vm)
                {
                    vm.RequestScrollToTop += () =>
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (SourceDataGrid.Items.Count > 0)
                                SourceDataGrid.ScrollIntoView(SourceDataGrid.Items[0]);
                        }, DispatcherPriority.ContextIdle);
                    };
                }

                FocusFirstControl();
            };
        }

        /// <summary>
        /// 首次聚焦到供应商列表。
        /// </summary>
        private void FocusFirstControl()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (SupplierFilter == null) return;
                SupplierFilter.Focus();
            }, DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 此窗口仅支持以对话框方式打开，请使用 ShowDialog()。
        /// </summary>
        public new void Show()
        {
            throw new InvalidOperationException("AwatingInboundView 只允许以对话框方式打开，请使用 ShowDialog()。");
        }

        /// <summary>
        /// 收到 ViewModel 广播的选择结果消息后关闭窗口。
        /// </summary>
        private void OnSelectionResult(AwatingInboundSelectionResultMessage msg)
        {
            Messenger.Default.Unregister<AwatingInboundSelectionResultMessage>(this, ViewModel);

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

        private void SourceDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.TempSelectedSourceItems = SourceDataGrid.SelectedItems.Cast<AwatingInboundDto>().ToList();
        }

        private void TargetDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.TempSelectedTargetItems = TargetDataGrid.SelectedItems.Cast<AwatingInboundDto>().ToList();
        }

        private void SourceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SourceDataGrid.SelectedItem is AwatingInboundDto && ViewModel != null)
                ViewModel.SelectItemsCommand.Execute(null);
        }

        private void TargetDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TargetDataGrid.SelectedItem is AwatingInboundDto && ViewModel != null)
                ViewModel.RemoveItemsCommand.Execute(null);
        }

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
        }

        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0) return;
            if (e.VerticalChange < 0) return;

            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10)
            {
                if (DataContext is AwatingInboundViewModel vm && !vm.IsBusy)
                {
                    vm.LoadDataCommand.Execute(null);
                }
            }
        }
    }
}
