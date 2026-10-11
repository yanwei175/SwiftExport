using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Views
{
    /// <summary>桥接视图加载、删除选择、新增行定位及行模板命令状态，业务行为交给 ViewModel。</summary>
    public partial class SalesOrderImportView : UserControl
    {
        private readonly SalesOrderImportViewModel model;
        private Action<Guid?> openConfiguration;
        /// <summary>使用窗口 Scope 提供的页面模型，不查询全局容器。</summary>
        public SalesOrderImportView(SalesOrderImportViewModel vm)
        {
            InitializeComponent(); model = vm; DataContext = vm;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            vm.DesignReadProfileRequested += uid => openConfiguration?.Invoke(uid);
        }
        /// <summary>首次打开时由组合入口接线到读取配置侧栏，视图不持有 Excel COM 或创建另一配置窗体。</summary>
        public void SetConfigurationOpener(Action<Guid?> opener) { openConfiguration = opener; }
        /// <summary>普通关闭前确认内存草稿；插件退出由窗口宿主跳过此检查。</summary>
        public bool CanClose() => model.CanClose();
        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            model.LineAdded -= OnLineAdded; model.LineAdded += OnLineAdded;
            try { await model.InitializeAsync(); await model.EnsureReadProfilesAsync(); }
            catch (OperationCanceledException) { /* Excel 已退出，禁止继续初始化视图。 */ }
        }
        /// <summary>离开可视树后解除定位订阅，后续重新加载可重新接线。</summary>
        private void OnUnloaded(object sender, RoutedEventArgs args) { model.LineAdded -= OnLineAdded; }
        /// <summary>等集合绑定完成后定位实际新行，排序后的视图仍按对象身份查找。</summary>
        private void OnLineAdded(SalesOrderImportLine line)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!IsLoaded || model.IsDisposed || !model.Lines.Contains(line)) return;
                OrderLinesGrid.SelectedItem = line;
                OrderLinesGrid.CurrentCell = new DataGridCellInfo(line, OrderLinesGrid.Columns[0]);
                OrderLinesGrid.ScrollIntoView(line, OrderLinesGrid.Columns[0]);
                OrderLinesGrid.Focus();
            }));
        }
        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (!ReferenceEquals(args.OriginalSource, OrderLinesGrid)) return;
            model.SetSelectedLines(OrderLinesGrid.SelectedItems.Cast<SalesOrderImportLine>().ToArray());
        }

        /// <summary>行按钮加载后，在参数绑定完成时刷新命令，避免新行显示旧的禁用状态。</summary>
        private void OnMatchButtonLoaded(object sender, RoutedEventArgs args) { RefreshMatchButton(sender); }

        /// <summary>虚拟化复用行模板时刷新参数命令，不根据网格选择决定匹配目标。</summary>
        private void OnMatchButtonDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) { RefreshMatchButton(sender); }

        /// <summary>集合通知先于模板绑定；在 Loaded 优先级等待 DataBind 完成，仅重算活跃按钮的资格。</summary>
        private void RefreshMatchButton(object sender)
        {
            var button = sender as FrameworkElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (button?.IsLoaded == true && model != null && !model.IsDisposed)
                    model.MatchLineSkuCommand.NotifyCanExecuteChanged();
            }));
        }
    }
}
