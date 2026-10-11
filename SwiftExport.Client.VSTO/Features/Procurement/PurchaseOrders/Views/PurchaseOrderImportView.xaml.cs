using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views
{
    /// <summary>只桥接加载、多选及配置侧栏入口，业务交给页面模型。</summary>
    public partial class PurchaseOrderImportView : UserControl
    {
        private readonly PurchaseOrderImportViewModel model;
        private Action<Guid?> openConfiguration;
        /// <summary>绑定同 Scope 的编辑模型，不解析全局容器。</summary>
        public PurchaseOrderImportView(PurchaseOrderImportViewModel model)
        {
            InitializeComponent();
            this.model = model;
            DataContext = model;
            Loaded += OnLoaded;
            model.DesignReadProfileRequested += uid => openConfiguration?.Invoke(uid);
        }
        /// <summary>组合入口提供对应采购分组的侧栏打开动作。</summary>
        public void SetConfigurationOpener(Action<Guid?> opener) { openConfiguration = opener; }
        /// <summary>普通关闭确认未保存草稿，宿主退出由 Manager 跳过。</summary>
        public bool CanClose() => model.CanClose();
        private async void OnLoaded(object sender, RoutedEventArgs args)
        { try { await model.InitializeAsync(); await model.EnsureReadProfilesAsync(); } catch (OperationCanceledException) { } }
        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        { if (model != null && ReferenceEquals(args.OriginalSource, OrderLinesGrid)) model.SetSelectedLines(OrderLinesGrid.SelectedItems.Cast<PurchaseOrderImportLine>().ToArray()); }
        /// <summary>参数绑定完成后刷新行命令，避免新增行沿用禁用状态。</summary>
        private void OnMatchButtonLoaded(object sender, RoutedEventArgs args) { RefreshMatchButton(sender); }
        /// <summary>虚拟化复用模板后重新检查行身份，不依赖当前网格选择。</summary>
        private void OnMatchButtonDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) { RefreshMatchButton(sender); }
        /// <summary>等待 DataBind 完成后刷新活跃按钮资格，不在视图执行业务匹配。</summary>
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
