using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Customers.Components;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Components;
using SwiftExport.Client.VSTO.Features.Suppliers.Components;
using SwiftExport.Client.VSTO.Resources.Controls;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.Suppliers;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>验证业务包装器和 HTTPS／本地图片管线；模拟图片响应，不访问外网或真实 API。</summary>
    internal static class BusinessLookupChecks
    {
        /// <summary>检查客户/销售字段语义、绑定命令及无磁盘缓存的图片加载。</summary>
        internal static void Run(Action<bool, string> check)
        {
            var customer = new CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "C001", CustomerName = "测试客户" };
            var control = new CustomerAutoSuggestComboBox { ItemsSource = new[] { customer }, FontSize = 17 };
            var host = Show(control);
            try
            {
                var editor = (LookupTextBox)control.FindName("LookupEditor");
                check(editor.SearchMode == LookupTextBoxSearchMode.Local && editor.FontSize == 17, "客户使用本地筛选并转发字号");
                check(editor.DisplayMemberPath == "CustomerCode" && editor.FilterMemberPaths == "CustomerCode,CustomerName", "客户代码显示、代码名称搜索");
                check(!editor.ClearSelectionOnEdit && editor.ShowDropDownButton && !editor.IsDropDownOpen, "客户选择保留、可显式展开且不自动展开");
                var calls = 0;
                object selected = null;
                control.SelectionCommittedCommand = new Command(value => { calls++; selected = value; });
                Pump();
                editor.CommitExternalSelection(customer, LookupCommitReason.Mouse);
                check(ReferenceEquals(control.SelectedCustomer, customer) && ReferenceEquals(selected, customer) && calls == 1, "客户提交完整 DTO 并转发命令");
                check(BindingOperations.IsDataBound(editor, LookupTextBox.SelectedItemProperty), "客户包装器保留内部双向绑定");
                ((ButtonBase)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check(control.SelectedCustomer == null, "清除回写客户选择");
            }
            finally { host.Close(); Pump(); }

            var supplier = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "S001", SupplierName = "测试供应商" };
            var suppliers = new System.Collections.ObjectModel.ReadOnlyObservableCollection<SupplierDto>(
                new System.Collections.ObjectModel.ObservableCollection<SupplierDto> { supplier });
            var supplierControl = new SupplierAutoSuggestComboBox { ItemsSource = suppliers, FontSize = 17, StretchToHost = true };
            host = Show(supplierControl);
            try
            {
                var editor = (LookupTextBox)supplierControl.FindName("LookupEditor");
                check(ReferenceEquals(editor.ItemsSource, suppliers) && editor.SearchMode == LookupTextBoxSearchMode.Local,
                    "供应商使用页面提供的本地缓存，不主动查询 API");
                check(editor.DisplayMemberPath == "SupplierCode" && editor.FilterMemberPaths == "SupplierCode"
                    && editor.SelectedValuePath == "Uid", "供应商按编码筛选并保留 Uid 语义");
                check(editor.FontSize == 17 && editor.StretchToHost && editor.RequireItemMatch
                    && !editor.ClearSelectionOnEdit && editor.ShowDropDownButton, "供应商转发字号及原有选择行为");
                var calls = 0;
                object selected = null;
                supplierControl.SelectionCommittedCommand = new Command(value => { calls++; selected = value; });
                Pump();
                editor.CommitExternalSelection(supplier, LookupCommitReason.Mouse);
                check(ReferenceEquals(supplierControl.SelectedSupplier, supplier) && ReferenceEquals(selected, supplier) && calls == 1,
                    "供应商提交完整缓存 DTO 并转发命令");
                ((ButtonBase)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check(supplierControl.SelectedSupplier == null, "清除回写供应商选择");
                var content = (FrameworkElement)editor.ItemTemplate.LoadContent();
                var fields = Descendants<TextBlock>(content).Select(x => BindingOperations.GetBinding(x, TextBlock.TextProperty)?.Path.Path).ToArray();
                check(fields.Contains("SupplierCode") && fields.Contains("SupplierName"), "候选显示供应商编码和名称");
            }
            finally { host.Close(); Pump(); }



            var product = new SalesOrderProductLookupDto
            {
                ProductId = Guid.NewGuid(), Sku = "SKU-1", CustomerSku = "CUS-1",
                DescriptionEn = "完整产品描述", MainImage = string.Empty
            };
            var sales = new SalesProductLookupTextBox { ItemsSource = new[] { product }, FontSize = 16 };
            host = Show(sales);
            try
            {
                var editor = (LookupTextBox)sales.FindName("LookupEditor");
                check(editor.SearchMode == LookupTextBoxSearchMode.Remote && editor.SelectedValuePath == "ProductId", "销售产品保持远程搜索与 ProductId 语义");
                check(editor.FilterMemberPaths == "Sku,CustomerSku,DescriptionEn,LinkSku" && editor.FontSize == 16, "销售保留全部搜索字段和字号");
                var calls = 0;
                sales.SearchCommand = new Command(value => calls++);
                sales.IsBusy = true;
                sales.SearchDelay = TimeSpan.FromMilliseconds(450);
                sales.MinimumSearchLength = 2;
                Pump();
                check(ReferenceEquals(sales.SearchCommand, editor.SearchCommand) && editor.IsBusy
                    && editor.SearchDelay == sales.SearchDelay && editor.MinimumSearchLength == 2, "销售包装器转发查询与防抖状态");
                check(calls == 0, "业务控件初始化不查询 API");
                editor.CommitExternalSelection(product, LookupCommitReason.Enter);
                check(ReferenceEquals(sales.SelectedProduct, product) && sales.SelectedProductId == product.ProductId, "提交销售候选及产品 ID");
                ((ButtonBase)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                check(sales.SelectedProduct == null && sales.SelectedProductId == null, "清除同时回写产品与 ID");
                var content = (FrameworkElement)editor.ItemTemplate.LoadContent();
                content.DataContext = product;
                content.Measure(new Size(600, 150));
                content.Arrange(new Rect(0, 0, 600, 150));
                var image = Descendants<HttpsImage>(content).Single();
                check(image.DecodePixelWidth == 96 && image.Width == 48 && image.Style != null, "候选使用无本地缓存的 96 像素缩略图及现有图片样式");
                var fields = Descendants<TextBlock>(content).Select(x => BindingOperations.GetBinding(x, TextBlock.TextProperty)?.Path.Path).ToArray();
                check(fields.Contains("Sku") && fields.Contains("CustomerSku") && fields.Contains("DescriptionEn"), "候选保留 SKU、客户型号和完整描述");
            }
            finally { host.Close(); Pump(); }
            ImageLoadingChecks.Run(check);
        }

        /// <summary>在不激活测试窗的条件下触发真实控件生命周期。</summary>
        private static Window Show(FrameworkElement content)
        {
            var window = new Window { Content = content, Width = 600, Height = 140, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); Pump(); return window;
        }

        /// <summary>推进异步下载事件与绑定。</summary>
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        /// <summary>读取实际模板子控件。</summary>
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        /// <summary>观测包装器转发的页面命令。</summary>
        private sealed class Command : ICommand
        {
            private readonly Action<object> action;
            /// <summary>保存执行观察器。</summary>
            public Command(Action<object> action) { this.action = action; }
            /// <summary>测试中执行资格不变化。</summary>
            public event EventHandler CanExecuteChanged { add { } remove { } }
            /// <summary>允许执行测试命令。</summary>
            public bool CanExecute(object parameter) => true;
            /// <summary>记录传入的选择或查询参数。</summary>
            public void Execute(object parameter) { action(parameter); }
        }

    }
}
