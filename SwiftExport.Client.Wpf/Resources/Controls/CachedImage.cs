using SwiftExportClient.Services.Images;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 面向虚拟化列表和 DataGrid 的缓存图片控件。控件只接收本地路径或完整网络地址，不拼接业务 URL。
/// </summary>
public sealed class CachedImage : Image
{
    public const string ImageCacheServiceResourceKey = "SwiftExport.ImageCacheService";

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

    private void OnUnloaded(object sender, RoutedEventArgs e) => CancelPendingLoad(clearSource: true);

    private void StartLoad()
    {
        CancelPendingLoad(clearSource: true);
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
            SetValue(LoadFailedPropertyKey, true);
            return;
        }

        var version = Interlocked.Increment(ref _requestVersion);
        _loadCancellation = new CancellationTokenSource();
        SetValue(IsLoadingPropertyKey, true);
        _ = LoadAsync(service, SourcePath, DecodePixelWidth, version, _loadCancellation.Token);
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

            Source = bitmap;
            SetValue(LoadFailedPropertyKey, bitmap is null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (version == Volatile.Read(ref _requestVersion) && IsLoaded)
            {
                Source = null;
                SetValue(LoadFailedPropertyKey, true);
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
