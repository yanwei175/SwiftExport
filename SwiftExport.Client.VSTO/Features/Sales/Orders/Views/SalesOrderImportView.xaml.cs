using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Views
{
    /// <summary>只桥接视图加载和多选状态，业务行为交给 ViewModel。</summary>
    public partial class SalesOrderImportView : UserControl
    {
        private readonly SalesOrderImportViewModel model;
        private Action<Guid?> openConfiguration;
        /// <summary>使用窗口 Scope 提供的页面模型，不查询全局容器。</summary>
        public SalesOrderImportView(SalesOrderImportViewModel vm)
        {
            InitializeComponent(); model = vm; DataContext = vm;
            Loaded += OnLoaded;
            vm.DesignReadProfileRequested += uid => openConfiguration?.Invoke(uid);
        }
        /// <summary>首次打开时由组合入口接线到读取配置侧栏，视图不持有 Excel COM 或创建另一配置窗体。</summary>
        public void SetConfigurationOpener(Action<Guid?> opener) { openConfiguration = opener; }
        /// <summary>普通关闭前确认内存草稿；插件退出由窗口宿主跳过此检查。</summary>
        public bool CanClose() => model.CanClose();
        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try { await model.InitializeAsync(); await model.EnsureReadProfilesAsync(); }
            catch (OperationCanceledException) { /* Excel 已退出，禁止继续初始化视图。 */ }
        }
        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (!ReferenceEquals(args.OriginalSource, OrderLinesGrid)) return;
            model.SetSelectedLines(OrderLinesGrid.SelectedItems.Cast<SalesOrderImportLine>().ToArray());
        }
    }
}
