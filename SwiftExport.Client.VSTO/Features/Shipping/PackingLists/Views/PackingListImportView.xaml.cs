using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels;
namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views
{
    /// <summary>WinForms 宿主的 WPF 导入页；只桥接加载、多选、定位、编辑提交和配置入口。</summary>
    public partial class PackingListImportView : UserControl
    {
        private readonly PackingListImportViewModel model;
        private Action<Guid?> openConfiguration;
        /// <summary>供 XAML 设计器建立可视树；示例数据由 d:DataContext 提供，不接线运行时服务。</summary>
        public PackingListImportView()
        {
            InitializeComponent();
        }
        /// <summary>使用同 Scope 的页面模型；不访问全局容器。</summary>
        public PackingListImportView(PackingListImportViewModel model) : this()
        {
            this.model = model; DataContext = model;
            model.DesignReadProfileRequested += uid => openConfiguration?.Invoke(uid);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewMouseDown += OnPreviewMouseDown;
        }
        /// <summary>由组合入口打开右侧地址配置。</summary>
        public void SetConfigurationOpener(Action<Guid?> opener) { openConfiguration = opener; }
        /// <summary>关闭前提交编辑并保护内存数据；插件退出由宿主跳过。</summary>
        public bool CanClose() { CommitGrid(); return model.CanClose(); }
        // 仅离开表格点击页面操作时提交，保留原生表格的单元格、选行和连续输入行为。
        private void OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            for (var node = args.OriginalSource as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (ReferenceEquals(node, LinesGrid)) return;
            CommitGrid();
        }
        private void CommitGrid() { LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true); LinesGrid.CommitEdit(DataGridEditingUnit.Row, true); }
        /// <summary>幂等接线新增行定位，并按需初始化；重复加载不重置输入。</summary>
        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            model.LineAdded -= OnLineAdded; model.LineAdded += OnLineAdded;
            try { await model.InitializeAsync(); }
            catch (OperationCanceledException) { }
        }
        /// <summary>退出可视树后解除定位订阅，重复 Loaded 不叠加事件。</summary>
        private void OnUnloaded(object sender, RoutedEventArgs args) { model.LineAdded -= OnLineAdded; }
        /// <summary>排序时仍按对象身份桥接多选，子控件的选择事件不影响表格选择。</summary>
        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (model != null && ReferenceEquals(args.OriginalSource, LinesGrid))
                model.SetSelectedLines(LinesGrid.SelectedItems.Cast<PackingListImportLine>().ToArray());
        }
        /// <summary>集合绑定后选中并滚动至手工新增行，关闭或释放后放弃延迟定位。</summary>
        private void OnLineAdded(PackingListImportLine line)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!IsLoaded || model.IsDisposed || !model.Lines.Contains(line)) return;
                LinesGrid.SelectedItem = line;
                LinesGrid.CurrentCell = new DataGridCellInfo(line, LinesGrid.Columns[0]);
                LinesGrid.ScrollIntoView(line, LinesGrid.Columns[0]); LinesGrid.Focus();
            }));
        }
        /// <summary>模板参数绑定完成后刷新本行命令资格。</summary>
        private void OnMatchButtonLoaded(object sender, RoutedEventArgs args) { RefreshMatchButton(sender); }
        /// <summary>虚拟化容器改绑行后重新检查命令，不依赖当前选中行。</summary>
        private void OnMatchButtonDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) { RefreshMatchButton(sender); }
        /// <summary>延迟到参数绑定完成再刷新，视图不执行业务匹配。</summary>
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
