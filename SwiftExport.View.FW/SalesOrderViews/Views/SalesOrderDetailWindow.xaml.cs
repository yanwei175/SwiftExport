using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using System;
using System.Windows;

namespace SwiftExport.View.FW.SalesOrderViews.Views
{
    /// <summary>
    /// SalesOrderDetailWindow — 销售订单详情弹窗容器。
    /// 职责：
    ///   1. 接收 SalesOrderDetailViewModel，注入给内部 UserControl。
    ///   2. 订阅 VM 的 CloseWindow 事件，将保存成功/取消结果映射到 DialogResult。
    ///   3. 仅允许以 ShowDialog() 方式打开。
    /// </summary>
    public partial class SalesOrderDetailWindow : Window
    {
        private SalesOrderDetailViewModel ViewModel => DataContext as SalesOrderDetailViewModel;

        public SalesOrderDetailWindow()
        {
            InitializeComponent();
        }

        public SalesOrderDetailWindow(SalesOrderDetailViewModel vm) : this()
        {
            // 中文注释：Window 和内部 UserControl 共享同一个 DataContext，
            // 这样 DetailView 内部的所有绑定无需任何改动。
            DataContext = vm;
            DetailView.DataContext = vm;

            if (vm != null)
            {
                vm.CloseWindow += OnViewModelCloseWindow;
            }

            Closed += (s, e) =>
            {
                if (ViewModel != null)
                    ViewModel.CloseWindow -= OnViewModelCloseWindow;
            };
        }

        /// <summary>
        /// 只允许以 ShowDialog 方式打开，与 SalesProductSeletorView 保持一致。
        /// </summary>
        public new void Show()
        {
            throw new InvalidOperationException("SalesOrderDetailWindow 只允许以对话框方式打开，请使用 ShowDialog()。");
        }

        /// <summary>
        /// 中文注释：VM 通知关闭时，将保存结果映射到 DialogResult，驱动 ShowDialog() 返回。
        /// </summary>
        private void OnViewModelCloseWindow(bool? success)
        {
            if (success == true)
            {
                try { DialogResult = true; } catch { }
            }
            else
            {
                try { DialogResult = false; } catch { }
                Close();
            }
        }
    }
}
