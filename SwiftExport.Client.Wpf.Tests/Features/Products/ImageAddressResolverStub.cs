using SwiftExportClient.Services.Images;
using SwiftExport.Contracts.Common;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>隔离系统参数 API，提供可变基址和配置通知，供产品编辑测试使用。</summary>
internal sealed class ImageAddressResolverStub(string? baseUrl = "https://example.test/files/ProductImages/") : IImageAddressResolver
{
    public string? BaseUrl { get; set; } = baseUrl;
    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public bool TryGetProductBaseAddress(out string baseAddress)
        => ProductImageAddress.TryNormalizeBaseAddress(BaseUrl!, out baseAddress);
    /// <inheritdoc />
    public ImageLoadAddress? Resolve(string? sourcePath, ImageDisplayMode mode, int decodePixelWidth) => null;
    /// <summary>模拟管理员保存或手动刷新成功后的通知。</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
