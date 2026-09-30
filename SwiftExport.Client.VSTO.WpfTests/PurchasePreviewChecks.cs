using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views;
using SwiftExport.Client.VSTO.Features.Customers.Components;
using SwiftExport.Client.VSTO.Features.Suppliers.Components;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>用真实 WPF 资源验证采购静态预览在独立窗口宽度下可呈现。</summary>
    internal static class PurchasePreviewChecks
    {
        /// <summary>检查页面分区、原生明细表与尚未接线的操作门禁。</summary>
        internal static void Run(Action<bool, string> check)
        {
            var model = new PurchaseOrderImportViewModel();
            var view = new PurchaseOrderImportView(model);
            var window = new Window { Style = null, Width = 1280, Height = 820, Content = view, ShowInTaskbar = false };
            window.Show();
            try
            {
                view.UpdateLayout();
                check(ReferenceEquals(view.DataContext, model), "采购 View 绑定空 ViewModel");
                check(Descendants<TextBlock>(view).Any(t => t.Text == "采购单头")
                    && Descendants<TextBlock>(view).Any(t => t.Text == "产品与包装要求")
                    && Descendants<TextBlock>(view).Any(t => t.Text == "采购行"), "单头、包装和采购行分区可见");
                var grid = (DataGrid)view.FindName("PurchaseLinesGrid");
                check(grid != null && grid.Columns.Count == 19 && grid.Items.Count == 0, "原生采购行表格仅显示列标题，无假业务数据");
                check(grid.Columns.Any(c => (string)c.Header == "SKU")
                    && grid.Columns.Any(c => (string)c.Header == "送至供应商")
                    && grid.Columns.Any(c => (string)c.Header == "来源单/行")
                    && grid.Columns.Any(c => (string)c.Header == "未税总价"), "采购关键列完整");
                check(grid.IsReadOnly && !grid.CanUserAddRows && ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Auto,
                    "明细表只读且允许横向滚动");
                check(grid.ActualWidth > 0 && grid.ActualWidth < window.ActualWidth, "独立窗口中明细表未把窗体撑宽");
                var buttons = Descendants<Button>(view).Where(button => (button.Content as string)?.Contains("待实现") == true).ToArray();
                check(buttons.Length == 7 && buttons.All(button => !button.IsEnabled), "七个业务操作明确标记待实现并禁用");
                check(Descendants<CustomerAutoSuggestComboBox>(view).Single().IsEnabled == false,
                    "客户选择位置复用现有控件但不触发加载");
                check(Descendants<SupplierAutoSuggestComboBox>(view).Single().IsEnabled == false,
                    "供应商选择位置复用本地缓存控件但不触发加载");
                check(new[] { "价格浮动率", "价格直接浮动", "商标类型" }
                    .All(label => Descendants<TextBlock>(view).Any(text => text.Text == label)),
                    "详情页导入相关的价格与商标字段保留在静态布局中");
                check(Descendants<DatePicker>(view).Count() == 3 && Descendants<DatePicker>(view).All(picker => !picker.IsEnabled),
                    "采购日期保留布局且不生成编辑结果");
                check(Descendants<TextBox>(view).All(box => box.IsReadOnly || !box.IsEnabled), "文本预览不会收集未接线的输入");
                check(Descendants<TextBlock>(view).Any(t => t.Text == "产品行数：—")
                    && Descendants<TextBlock>(view).Any(t => t.Text == "人民币总额：—"), "页脚汇总只显示静态占位");
            }
            finally { window.Close(); model.Dispose(); }
        }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match) yield return match;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }
    }
}
