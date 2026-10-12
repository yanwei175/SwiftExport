using System.IO;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Services.Images;

/// <summary>分流本地、托管网络和外部网络引用；仅已知目录改写缩略图地址。</summary>
public sealed class ImageAddressResolver : IImageAddressResolver, IDisposable
{
    private readonly ISystemParameterCacheService _parameters;

    /// <summary>依赖内存参数快照；整个进程共用一次配置通知。</summary>
    public ImageAddressResolver(ISystemParameterCacheService parameters)
    { _parameters = parameters; parameters.Changed += OnChanged; }
    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public bool TryGetProductBaseAddress(out string baseAddress)
    {
        baseAddress = null!;
        return _parameters.TryGetValue(SystemParameterKeys.ProductImagesBaseUrl, out var value)
            && ProductImageAddress.TryNormalizeBaseAddress(value, out baseAddress);
    }

    /// <summary>网络展示只用上传公开地址；升级期缺少新参数时兼容原有网络基址，本地重建目录不会参与。</summary>
    private bool TryGetProductPublicBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        if (_parameters.TryGetValue(SystemParameterKeys.ProductImagesUploadBaseUrl, out var uploadBase))
            return ProductImageAddress.TryNormalizeBaseUrl(uploadBase, out baseUri);
        return _parameters.TryGetValue(SystemParameterKeys.ProductImagesBaseUrl, out var legacyBase)
            && ProductImageAddress.TryNormalizeBaseUrl(legacyBase, out baseUri);
    }

    /// <inheritdoc />
    public ImageLoadAddress? Resolve(string? sourcePath, ImageDisplayMode mode, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        var width = Math.Max(1, decodePixelWidth);
        if (Path.IsPathFullyQualified(sourcePath)) return new(sourcePath, width);
        if (!Uri.TryCreate(sourcePath.Trim(), UriKind.Absolute, out var source)
            || (source.Scheme != Uri.UriSchemeHttp && source.Scheme != Uri.UriSchemeHttps)) return null;

        if (!TryGetProductPublicBaseUri(out var baseUri))
            return mode == ImageDisplayMode.Original ? new(source.AbsoluteUri, width) : null;
        // URI 目录严格区分大小写，并包含尾部斜杠，避免把相似目录或其他主机当成托管图片。
        if (source.Scheme != baseUri.Scheme || !source.IdnHost.Equals(baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || source.Port != baseUri.Port || !string.IsNullOrEmpty(source.UserInfo)
            || !source.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            return new(source.AbsoluteUri, width);
        var relative = source.AbsolutePath[baseUri.AbsolutePath.Length..];
        var segments = relative.Split('/');
        string escapedName;
        if (segments.Length == 1 && segments[0].Length > 0) escapedName = segments[0];
        else if (segments.Length == 2 && segments[0] == "Thumbnails" && segments[1].Length > 0) escapedName = segments[1];
        else return new(source.AbsoluteUri, width);
        var name = Uri.UnescapeDataString(escapedName);
        if (!ProductImageAddress.IsValidFileStem(name)) return null;
        // 解码后只对文件名重新转义一次，保留源查询参数，避免重复 Thumbnails 或转义。
        var resolved = ProductImageAddress.BuildUrl(baseUri, name, mode == ImageDisplayMode.Thumbnail) + source.Query;
        return new(resolved, mode == ImageDisplayMode.Thumbnail ? Math.Min(width, 256) : width);
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);
    /// <summary>解除系统参数事件订阅。</summary>
    public void Dispose() => _parameters.Changed -= OnChanged;
}
