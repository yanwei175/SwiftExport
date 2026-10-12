using SwiftExportClient.Services.Images;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 面向虚拟化列表和 DataGrid 的缓存图片控件；使用缓存参数解析展示地址，原始引用保持不变。
/// </summary>
public sealed class CachedImage : Image
{
    public const string ImageCacheServiceResourceKey = "SwiftExport.ImageCacheService";

    /// <summary>应用级图片地址解析器资源键。</summary>
    public const string ImageAddressResolverResourceKey = "SwiftExport.ImageAddressResolver";

    /// <summary>缩略图或原图展示方式，默认原图保持直接控件调用兼容。</summary>
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode), typeof(ImageDisplayMode), typeof(CachedImage), new PropertyMetadata(ImageDisplayMode.Original, OnLoadParameterChanged));

    /// <summary>可显式提供地址解析器，默认使用应用资源中的单例。</summary>
    public static readonly DependencyProperty AddressResolverProperty = DependencyProperty.Register(
        nameof(AddressResolver), typeof(IImageAddressResolver), typeof(CachedImage), new PropertyMetadata(null, OnLoadParameterChanged));

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath),
        typeof(string),
        typeof(CachedImage),
        new PropertyMetadata(null, OnLoadParameterChanged));

    public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(
        nameof(DecodePixelWidth),
        typeof(int),
        typeof(CachedImage),
        new PropertyMetadata(96, OnLoadParameterChanged, CoerceDecodePixelWidth));

    public static readonly DependencyProperty CacheServiceProperty = DependencyProperty.Register(
        nameof(CacheService),
        typeof(IImageCacheService),
        typeof(CachedImage),
        new PropertyMetadata(null, OnLoadParameterChanged));

    private static readonly DependencyPropertyKey IsLoadingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLoading),
        typeof(bool),
        typeof(CachedImage),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsLoadingProperty = IsLoadingPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey LoadFailedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LoadFailed),
        typeof(bool),
        typeof(CachedImage),
        new PropertyMetadata(false));

    public static readonly DependencyProperty LoadFailedProperty = LoadFailedPropertyKey.DependencyProperty;

    private CancellationTokenSource? _loadCancellation;
    private long _requestVersion;
    private IImageAddressResolver? _subscribedResolver;

    /// <summary>加载时订阅配置变化，卸载时取消请求并释放订阅。</summary>
    public CachedImage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>本机绝对路径、UNC 路径或完整 HTTP/HTTPS 地址；相对路径会被拒绝。</summary>
    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    /// <summary>解码缩略图宽度。默认 96；缓存服务还会应用全局上限。</summary>
    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    /// <summary>可选的显式缓存服务；为空时从 Application Resource 解析 DI 单例。</summary>
    public IImageCacheService? CacheService
    {
        get => (IImageCacheService?)GetValue(CacheServiceProperty);
        set => SetValue(CacheServiceProperty, value);
    }

    /// <summary>控制实际展示地址；不会改写 SourcePath。</summary>
    public ImageDisplayMode DisplayMode
    { get => (ImageDisplayMode)GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }

    /// <summary>可选显式地址解析器，不执行参数 API 请求。</summary>
    public IImageAddressResolver? AddressResolver
    { get => (IImageAddressResolver?)GetValue(AddressResolverProperty); set => SetValue(AddressResolverProperty, value); }

    public bool IsLoading => (bool)GetValue(IsLoadingProperty);

    public bool LoadFailed => (bool)GetValue(LoadFailedProperty);

    private static void OnLoadParameterChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (CachedImage)dependencyObject;
        control.CancelPendingLoad(clearSource: true);
        if (control.IsLoaded)
        {
            control.StartLoad();
        }
    }

    private static object CoerceDecodePixelWidth(DependencyObject dependencyObject, object baseValue) =>
        Math.Max(1, (int)baseValue);

    private void OnLoaded(object sender, RoutedEventArgs e) => StartLoad();

    /// <summary>虚拟化回收不能保留配置订阅或图像引用。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachResolver();
        CancelPendingLoad(clearSource: true);
    }

    /// <summary>冻结解析后的地址和解码尺寸；请求序号阻止回收或重绑后的旧结果显示。</summary>
    private void StartLoad()
    {
        CancelPendingLoad(clearSource: true);
        AttachResolver();
        SetValue(LoadFailedPropertyKey, false);

        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            SetValue(IsLoadingPropertyKey, false);
            return;
        }

        var service = CacheService ?? TryResolveCacheService();
        if (service is null)
        {
            SetValue(IsLoadingPropertyKey, false);
            ShowPlaceholder();
            return;
        }

        var address = _subscribedResolver?.Resolve(SourcePath, DisplayMode, DecodePixelWidth);
        if (_subscribedResolver is null)
        {
            // 应用资源未初始化时，小图禁止回退加载网络原图；本地和显式原图保持原流程。
            var isNetwork = Uri.TryCreate(SourcePath.Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https";
            address = isNetwork && DisplayMode == ImageDisplayMode.Thumbnail ? null : new(SourcePath, DecodePixelWidth);
        }
        if (address is null) { ShowPlaceholder(); return; }

        var version = Interlocked.Increment(ref _requestVersion);
        _loadCancellation = new CancellationTokenSource();
        SetValue(IsLoadingPropertyKey, true);
        _ = LoadAsync(service, address.SourcePath, address.DecodePixelWidth, version, _loadCancellation.Token);
    }

    private async Task LoadAsync(
        IImageCacheService service,
        string sourcePath,
        int decodePixelWidth,
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await service.GetAsync(sourcePath, decodePixelWidth, cancellationToken);
            if (version != Volatile.Read(ref _requestVersion) || cancellationToken.IsCancellationRequested || !IsLoaded)
            {
                return;
            }

            if (bitmap is null) ShowPlaceholder();
            else Source = bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (version == Volatile.Read(ref _requestVersion) && IsLoaded)
            {
                ShowPlaceholder();
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _requestVersion))
            {
                SetValue(IsLoadingPropertyKey, false);
            }
        }
    }

    private IImageCacheService? TryResolveCacheService() =>
        Application.Current?.TryFindResource(ImageCacheServiceResourceKey) as IImageCacheService;

    /// <summary>只订阅当前解析器，参数更新即使引用文本未变也重新加载。</summary>
    private void AttachResolver()
    {
        var resolver = AddressResolver ?? Application.Current?.TryFindResource(ImageAddressResolverResourceKey) as IImageAddressResolver;
        if (ReferenceEquals(resolver, _subscribedResolver)) return;
        DetachResolver();
        _subscribedResolver = resolver;
        if (resolver is not null) resolver.Changed += OnAddressChanged;
    }
    /// <summary>解除当前解析器订阅，虚拟化回收不能继续持有控件。</summary>
    private void DetachResolver()
    {
        if (_subscribedResolver is not null) _subscribedResolver.Changed -= OnAddressChanged;
        _subscribedResolver = null;
    }
    /// <summary>配置通知回 UI 线程，只有仍可见的控件重新发起加载。</summary>
    private void OnAddressChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnAddressChanged(sender, e)); return; }
        if (IsLoaded) StartLoad();
    }

    /// <summary>缺配置或图片失败展示占位，不缓存占位、不主动加载原图。</summary>
    private void ShowPlaceholder()
    {
        Source = TryFindResource("SwiftExportImagePlaceholder") as System.Windows.Media.ImageSource;
        SetValue(LoadFailedPropertyKey, true);
        SetValue(IsLoadingPropertyKey, false);
    }

    private void CancelPendingLoad(bool clearSource)
    {
        Interlocked.Increment(ref _requestVersion);
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        SetValue(IsLoadingPropertyKey, false);
        if (clearSource)
        {
            Source = null;
        }
    }
}
