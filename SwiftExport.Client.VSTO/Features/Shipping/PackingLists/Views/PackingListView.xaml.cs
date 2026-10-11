using System.Windows;
using System.Windows.Controls;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels;

namespace SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views
{
    /// <summary>装箱单侧栏只接线首次显示；查询和会话释放由同 Scope 的页面模型负责。</summary>
    public partial class PackingListView : UserControl
    {
        private readonly PackingListViewModel model;
        /// <summary>接纳注入模型，不访问全局容器或在构造时请求服务器。</summary>
        public PackingListView(PackingListViewModel model)
        { InitializeComponent(); this.model = model; DataContext = model; Loaded += OnLoaded; }
        // 同会话重复 Loaded 不重复查询；页面模型统一处理错误与释放门禁。
        private async void OnLoaded(object sender, RoutedEventArgs args) { await model.InitializeAsync(); }
    }
}
