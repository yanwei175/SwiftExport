namespace SwiftExportClient.Services.Images;

/// <summary>根据已缓存系统参数解析图片加载地址，控件不访问参数 API。</summary>
public interface IImageAddressResolver
{
    /// <summary>配置变化通知当前可见控件重新解析。</summary>
    event EventHandler? Changed;
    /// <summary>获取有效产品原图目录快照，用于一次编辑或批量生成。</summary>
    bool TryGetProductBaseUri(out Uri baseUri);
    /// <summary>解析实际加载地址与解码宽度，无有效引用返回 null。</summary>
    ImageLoadAddress? Resolve(string? sourcePath, ImageDisplayMode mode, int decodePixelWidth);
}

/// <summary>实际缓存键输入；不回写到产品 DTO 或编辑字段。</summary>
public sealed record ImageLoadAddress(string SourcePath, int DecodePixelWidth);
