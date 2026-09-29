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
    /// <summary>验证业务包装器和原生 URI 图片管线；模拟图片响应，不访问外网或真实 API。</summary>
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
            CheckImages(check);
        }

        /// <summary>运行真实 BitmapImage URI 下载/解码，以内存 WebRequest 响应隔离外部网络。</summary>
        private static void CheckImages(Action<bool, string> check)
        {
            var transport = new ImageRequestFactory();
            check(WebRequest.RegisterPrefix("https://vsto-image.test/", transport), "注册仅限测试域名的图片传输");
            var image = new HttpsImage { SourcePath = "https://vsto-image.test/first.png", DecodePixelWidth = 8 };
            check(transport.Requests.Count == 0 && image.Source == null, "未加载的图片不预取");
            var window = Show(image);
            try
            {
                WaitUntil(() => transport.Requests.Count == 1);
                var bitmap = (BitmapImage)image.Source;
                check(bitmap.UriSource.Scheme == "https" && bitmap.UriCachePolicy.Level == RequestCacheLevel.NoCacheNoStore
                    && bitmap.CreateOptions == BitmapCreateOptions.IgnoreImageCache, "HTTPS 请求禁用 URI 缓存与位图复用");
                check(image.IsLoading, "下载中状态可供绑定");
                transport.Requests[0].Complete(Png());
                WaitUntil(() => !image.IsLoading);
                check(!image.LoadFailed && ((BitmapSource)image.Source).PixelWidth == 8, "真实 PNG 解码完成并限制缩略图宽度");
                image.SourcePath = "https://vsto-image.test/old.png";
                WaitUntil(() => transport.Requests.Count == 2);
                var oldBitmap = (BitmapImage)image.Source;
                image.SourcePath = "https://vsto-image.test/new.png";
                WaitUntil(() => transport.Requests.Count == 3);
                var newest = image.Source;
                transport.Requests[1].Complete(Png());
                WaitUntil(() => !oldBitmap.IsDownloading);
                check(ReferenceEquals(newest, image.Source), "旧地址完成不替换当前图片");
                transport.Requests[2].Complete(new byte[] { 1, 2, 3 });
                WaitUntil(() => image.LoadFailed);
                check(image.Source == null && !image.IsLoading, "损坏响应清空图片并记录失败状态");
                image.SourcePath = "https://vsto-image.test/unloaded.png";
                WaitUntil(() => transport.Requests.Count == 4);
                var unloadedBitmap = (BitmapImage)image.Source;
                window.Content = null;
                Pump();
                transport.Requests[3].Complete(Png());
                WaitUntil(() => !unloadedBitmap.IsDownloading);
                check(image.Source == null && !image.IsLoading, "卸载切断下载与控件，不回填迟到图片");
                window.Content = image;
                Pump();
                WaitUntil(() => transport.Requests.Count == 5);
                transport.Requests[4].Fail();
                WaitUntil(() => image.LoadFailed);
                check(image.Source == null, "网络失败不会保留其他候选的图片");
                foreach (var path in new[] { "http://vsto-image.test/plain.png", "/relative.png", "file:///C:/image.png", "https://user:password@vsto-image.test/a.png" })
                {
                    image.SourcePath = path;
                    check(image.LoadFailed && image.Source == null, "拒绝非完整 HTTPS 或内嵌凭据地址");
                }
                check(transport.Requests.Count == 5, "非法地址没有进入传输");
                image.SourcePath = null;
                check(!image.LoadFailed && !image.IsLoading && image.Source == null, "空图片正常留白而非报错");
            }
            finally { window.Close(); Pump(); }
        }

        /// <summary>创建合法 PNG 字节，不读写图片文件。</summary>
        private static byte[] Png()
        {
            var pixels = new byte[16 * 16 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 128; pixels[i + 3] = 255; }
            var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }

        /// <summary>在不激活测试窗的条件下触发真实控件生命周期。</summary>
        private static Window Show(FrameworkElement content)
        {
            var window = new Window { Content = content, Width = 600, Height = 140, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); Pump(); return window;
        }

        /// <summary>最多等待三秒并推进 WPF 消息；失败及时结束专项测试。</summary>
        private static void WaitUntil(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > 3000) throw new InvalidOperationException("图片测试等待超时。");
                Pump(); Thread.Sleep(10);
            }
            Pump();
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

        /// <summary>只接管测试专属域名，生产 HTTPS 仍走系统传输和证书验证。</summary>
        private sealed class ImageRequestFactory : IWebRequestCreate
        {
            public readonly List<ImageRequest> Requests = new List<ImageRequest>();
            /// <summary>返回可人工完成的 URI 请求。</summary>
            public WebRequest Create(Uri uri) { var request = new ImageRequest(uri); Requests.Add(request); return request; }
        }

        /// <summary>供 WPF 原生 BitmapImage 下载器调用的内存异步请求。</summary>
        private sealed class ImageRequest : WebRequest
        {
            private readonly Uri uri;
            private TaskCompletionSource<WebResponse> response;
            /// <summary>保存请求 URI。</summary>
            public ImageRequest(Uri uri) { this.uri = uri; }
            public override Uri RequestUri => uri;
            public override RequestCachePolicy CachePolicy { get; set; }
            /// <summary>保留异步状态并把响应回调交给线程池，模拟网络完成。</summary>
            public override IAsyncResult BeginGetResponse(AsyncCallback callback, object state)
            {
                response = new TaskCompletionSource<WebResponse>(state);
                response.Task.ContinueWith(task => callback(task), TaskScheduler.Default);
                return response.Task;
            }
            /// <summary>向真实 WPF 下载管线交付响应或失败。</summary>
            public override WebResponse EndGetResponse(IAsyncResult result) => ((Task<WebResponse>)result).GetAwaiter().GetResult();
            /// <summary>返回 PNG 或损坏数据。</summary>
            public void Complete(byte[] data) { response.SetResult(new ImageResponse(uri, data)); }
            /// <summary>模拟网络失败。</summary>
            public void Fail() { response.SetException(new WebException("测试网络失败")); }
        }

        /// <summary>提供无需本地文件的图片响应流。</summary>
        private sealed class ImageResponse : WebResponse
        {
            private readonly Uri uri;
            private readonly byte[] data;
            /// <summary>保存模拟响应内容。</summary>
            public ImageResponse(Uri uri, byte[] data) { this.uri = uri; this.data = data; }
            public override Uri ResponseUri => uri;
            public override long ContentLength { get => data.Length; set { } }
            public override string ContentType { get => "image/png"; set { } }
            /// <summary>返回本次下载独占的内存流。</summary>
            public override Stream GetResponseStream() => new MemoryStream(data, false);
            /// <summary>响应不持有外部资源。</summary>
            public override void Close() { }
        }
    }
}
