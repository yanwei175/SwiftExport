using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views;
using SwiftExport.Client.VSTO.Tests;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>单独验证装箱单配置自动选择的实际下拉绑定，不依赖其他布局默认值。</summary>
    internal static class PackingListReadChecks
    {
        /// <summary>首次显示选中首项，刷新按身份恢复选择；编辑内容及用户的布局开关保持原状。</summary>
        internal static void Run(Action<bool, string> check)
        {
            using (var fixture = new PackingListImportTests.Fixture())
            {
                var model = fixture.Model; var view = new PackingListImportView(model);
                var window = new Window { Style = null, Content = view, Width = 1280, Height = 820, ShowInTaskbar = false };
                model.Header.Note = "保留手工备注"; window.Show();
                try
                {
                    Pump(); var selector = Find<ComboBox>(view).Single(combo => ReferenceEquals(combo.ItemsSource, model.ReadProfiles));
                    check(model.SelectedReadProfile != null && ReferenceEquals(selector.SelectedItem, model.ReadProfiles[0]), "首次 Loaded 真实下拉自动选择首项");
                    check(model.ReadHeaderCommand.CanExecute(null) && model.ReadLinesCommand.CanExecute(null), "首项选中后启用两个读取入口");
                    var updated = new ExcelReadProfileListItemDto { Uid = model.SelectedReadProfile.Uid, GroupCode = "PackingList", ConfigName = "更新后的名称" };
                    fixture.Base.ProfileApi.ListOverride = (group, token) => System.Threading.Tasks.Task.FromResult(
                        ServiceResult<System.Collections.Generic.IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(new[] { updated }));
                    model.InitializeCommand.Execute(null); Pump();
                    check(ReferenceEquals(selector.SelectedItem, updated) && model.Header.Note == "保留手工备注", "刷新按 Uid 接纳新摘要并保留编辑");
                    var calls = fixture.Base.ProfileApi.Lists; view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Pump();
                    check(fixture.Base.ProfileApi.Lists == calls && ReferenceEquals(selector.SelectedItem, updated), "重复 Loaded 保留选择且不重载");
                }
                finally { window.Close(); }
            }
        }
        private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T item) yield return item;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
        // 同一 STA 驱动真实 Loaded 和绑定更新，不执行 Excel 或请求真实服务器。
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
