using System;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwiftExport.Client.VSTO.Diagnostics;

namespace SwiftExport.Client.VSTO.Resources.Controls
{
    /// <summary>使用 WPF 原生下载显示 HTTPS 缩略图；不保存图片文件、不建立缓存服务或附加登录令牌。</summary>
    public sealed class HttpsImage : Image
    {
        /// <summary>完整 HTTPS 图片地址的依赖属性标识。</summary>
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
        /// <summary>下载或解码失败状态的只读依赖属性标识。</summary>
        public static readonly DependencyProperty LoadFailedProperty = LoadFailedPropertyKey.DependencyProperty;
        private readonly AddInLog log = new AddInLog();
        private BitmapImage bitmap;

        /// <summary>仅订阅控件生命周期，构造和卸载状态不发起下载。</summary>
        public HttpsImage()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>完整 HTTPS 地址；不自动拼接服务器路径，也不接受本地文件或 UNC 路径。</summary>
        public string SourcePath { get => (string)GetValue(SourcePathProperty); set => SetValue(SourcePathProperty, value); }
        /// <summary>解码宽度，默认 96 像素，范围 1 至 2048。</summary>
        public int DecodePixelWidth { get => (int)GetValue(DecodePixelWidthProperty); set => SetValue(DecodePixelWidthProperty, value); }
        /// <summary>当前图片是否正在下载。</summary>
        public bool IsLoading => (bool)GetValue(IsLoadingProperty);
        /// <summary>当前地址是否无效，或下载、解码是否失败。</summary>
        public bool LoadFailed => (bool)GetValue(LoadFailedProperty);

        /// <summary>换地址或尺寸时移除旧图及事件，再按当前加载状态请求新图。</summary>
        private static void OnParameterChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var control = (HttpsImage)sender;
            control.Reset();
            if (control.IsLoaded) control.StartLoad();
        }

        /// <summary>进入可视树后才下载当前地址；虚拟化重载重新请求，不读取磁盘缓存。</summary>
        private void OnLoaded(object sender, RoutedEventArgs e) { StartLoad(); }
        /// <summary>离开可视树解除位图事件并清除 Source，迟到下载不会再更新控件。</summary>
        private void OnUnloaded(object sender, RoutedEventArgs e) { Reset(); }

        /// <summary>以禁用 URI 缓存策略创建位图，保留系统默认 TLS 证书验证。</summary>
        private void StartLoad()
        {
            Reset();
            if (string.IsNullOrWhiteSpace(SourcePath)) return;
            Uri uri;
            if (!Uri.TryCreate(SourcePath.Trim(), UriKind.Absolute, out uri)
                || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                SetValue(LoadFailedPropertyKey, true);
                return;
            }
            try
            {
                var current = new BitmapImage();
                bitmap = current;
                current.DownloadCompleted += OnDownloadCompleted;
                current.DownloadFailed += OnDownloadFailed;
                current.DecodeFailed += OnDownloadFailed;
                current.BeginInit();
                current.UriCachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                current.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                current.CacheOption = BitmapCacheOption.OnLoad;
                current.DecodePixelWidth = DecodePixelWidth;
                current.UriSource = uri;
                current.EndInit();
                // 同步完成或失败时，事件可能已经清理了本次对象。
                if (!ReferenceEquals(bitmap, current)) return;
                SetCurrentValue(SourceProperty, current);
                SetValue(IsLoadingPropertyKey, current.IsDownloading);
            }
            catch (Exception exception) when (exception is WebException || exception is IOException
                || exception is NotSupportedException || exception is ArgumentException || exception is InvalidOperationException
                || exception is FileFormatException || exception is System.Runtime.InteropServices.COMException)
            {
                Fail(exception);
            }
        }

        /// <summary>只接纳当前位图；旧请求与卸载对象的通知都被忽略。</summary>
        private void OnDownloadCompleted(object sender, EventArgs e)
        {
            if (!IsLoaded || !ReferenceEquals(sender, bitmap)) return;
            SetValue(IsLoadingPropertyKey, false);
        }

        /// <summary>失败保留空白缩略图，不向 Excel 弹异常或泄露地址中的签名参数。</summary>
        private void OnDownloadFailed(object sender, ExceptionEventArgs e)
        {
            if (!IsLoaded || !ReferenceEquals(sender, bitmap)) return;
            Fail(e.ErrorException);
        }

        /// <summary>记录脱敏错误类型并清理失败图片的事件。</summary>
        private void Fail(Exception exception)
        {
            Reset();
            SetValue(LoadFailedPropertyKey, true);
            log.Write("HttpsImageFailure", exception);
        }

        /// <summary>切断控件与下载位图的联系；WPF 不提供取消已启动 URI 下载的接口。</summary>
        private void Reset()
        {
            if (bitmap != null)
            {
                bitmap.DownloadCompleted -= OnDownloadCompleted;
                bitmap.DownloadFailed -= OnDownloadFailed;
                bitmap.DecodeFailed -= OnDownloadFailed;
                bitmap = null;
            }
            SetCurrentValue(SourceProperty, null);
            SetValue(IsLoadingPropertyKey, false);
            SetValue(LoadFailedPropertyKey, false);
        }
    }
}
