using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using SwiftExport.Client.VSTO.Diagnostics;

namespace SwiftExport.Client.VSTO.Resources.Controls
{
    /// <summary>异步显示 HTTPS 或本地图片，复用有上限的内存缩略图；不写磁盘缓存或附加登录令牌。</summary>
    public sealed class HttpsImage : Image
    {
        /// <summary>完整 HTTPS 地址或本地绝对路径的依赖属性标识。</summary>
        public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
            nameof(SourcePath), typeof(string), typeof(HttpsImage), new PropertyMetadata(null, OnParameterChanged));
        /// <summary>缩略图解码宽度的依赖属性标识。</summary>
        public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(
            nameof(DecodePixelWidth), typeof(int), typeof(HttpsImage),
            new PropertyMetadata(96, OnParameterChanged), value => (int)value > 0 && (int)value <= 2048);
        private static readonly DependencyPropertyKey IsLoadingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsLoading), typeof(bool), typeof(HttpsImage), new PropertyMetadata(false));
        private static readonly DependencyPropertyKey LoadFailedPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LoadFailed), typeof(bool), typeof(HttpsImage), new PropertyMetadata(false));
        /// <summary>下载状态的只读依赖属性标识。</summary>
        public static readonly DependencyProperty IsLoadingProperty = IsLoadingPropertyKey.DependencyProperty;
        /// <summary>读取或解码失败状态的只读依赖属性标识。</summary>
        public static readonly DependencyProperty LoadFailedProperty = LoadFailedPropertyKey.DependencyProperty;
        private readonly AddInLog log = new AddInLog();
        private readonly ImageSourceLoader testLoader;
        private CancellationTokenSource request;

        /// <summary>仅订阅生命周期；构造和卸载状态不读取图片。</summary>
        public HttpsImage() : this(null) { }

        /// <summary>供独立测试替换图片传输，不更改生产共享连接。</summary>
        internal HttpsImage(ImageSourceLoader loader)
        {
            testLoader = loader;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>完整 HTTPS、磁盘绝对路径或 file URI；不自动拼接 API 根地址。</summary>
        public string SourcePath { get => (string)GetValue(SourcePathProperty); set => SetValue(SourcePathProperty, value); }
        /// <summary>解码宽度，默认 96 像素，范围 1 至 2048。</summary>
        public int DecodePixelWidth { get => (int)GetValue(DecodePixelWidthProperty); set => SetValue(DecodePixelWidthProperty, value); }
        /// <summary>当前图片是否正在读取。</summary>
        public bool IsLoading => (bool)GetValue(IsLoadingProperty);
        /// <summary>当前路径是否无效，或读取、解码是否失败。</summary>
        public bool LoadFailed => (bool)GetValue(LoadFailedProperty);

        /// <summary>换路径或尺寸时取消旧读取，再按当前加载状态请求新图。</summary>
        private static void OnParameterChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var control = (HttpsImage)sender;
            control.Reset();
            if (control.IsLoaded) control.StartLoad();
        }

        /// <summary>进入可视树后读取，虚拟化重载可命中内存缓存。</summary>
        private void OnLoaded(object sender, RoutedEventArgs e) { StartLoad(); }
        /// <summary>离开可视树取消当前请求并断开显示，迟到结果不得回填。</summary>
        private void OnUnloaded(object sender, RoutedEventArgs e) { Reset(); }

        /// <summary>先验证路径，再异步读取；结果接纳仍在控件所属 Dispatcher 上。</summary>
        private async void StartLoad()
        {
            Reset();
            if (string.IsNullOrWhiteSpace(SourcePath)) return;
            Uri uri;
            if (!ImageSourceLoader.TryGetUri(SourcePath, out uri))
            {
                SetValue(LoadFailedPropertyKey, true);
                return;
            }
            var current = new CancellationTokenSource();
            request = current;
            SetValue(IsLoadingPropertyKey, true);
            try
            {
                var image = await (testLoader ?? ImageSourceLoader.Shared).LoadAsync(uri, DecodePixelWidth, current.Token);
                if (IsLoaded && ReferenceEquals(request, current) && !current.IsCancellationRequested)
                    SetCurrentValue(SourceProperty, image);
            }
            catch (OperationCanceledException)
            {
                // 换图／卸载的主动取消不显示错误；当前读取超时仍提供失败状态。
                if (IsLoaded && ReferenceEquals(request, current) && !current.IsCancellationRequested)
                    SetValue(LoadFailedPropertyKey, true);
            }
            catch (Exception exception)
            {
                if (!IsLoaded || !ReferenceEquals(request, current)) return;
                SetValue(LoadFailedPropertyKey, true);
                log.Write("HttpsImageFailure", exception);
            }
            finally
            {
                if (ReferenceEquals(request, current))
                {
                    request = null;
                    SetValue(IsLoadingPropertyKey, false);
                }
                current.Dispose();
            }
        }

        /// <summary>取消旧操作并立即移除旧图；令牌源由所属操作在 finally 中释放。</summary>
        private void Reset()
        {
            var old = request;
            request = null;
            old?.Cancel();
            SetCurrentValue(SourceProperty, null);
            SetValue(IsLoadingPropertyKey, false);
            SetValue(LoadFailedPropertyKey, false);
        }
    }
}
