using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels;
namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views
{
    /// <summary>WinForms 宿主的 WPF 导入页；仅桥接加载、编辑提交和配置入口。</summary>
    public partial class PackingListImportView : UserControl
    {
        private readonly PackingListImportViewModel model;
        private Action<Guid?> openConfiguration;
        /// <summary>使用同 Scope 的页面模型；不访问全局容器。</summary>
        public PackingListImportView(PackingListImportViewModel model)
        {
            InitializeComponent(); this.model = model; DataContext = model;
            model.DesignReadProfileRequested += uid => openConfiguration?.Invoke(uid);
            Loaded += OnLoaded;
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
        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try { await model.InitializeAsync(); }
            catch (OperationCanceledException) { }
        }
    }
}
