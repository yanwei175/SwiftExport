using SwiftExport.Contracts.DTOs.Files.ProductImages;

namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>保存一个已授权批次的限制快照、目标去重集合及有序逐项结果。</summary>
public sealed class ProductImageBatchSession
{
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProductImageUploadItemResultDto> _items = [];

    /// <summary>固定本批次的物理存储与公开原图基址，批次中途参数变化不影响回执。</summary>
    internal ProductImageBatchSession(ProductImageStorageContext storage, Uri publicBaseUri)
    {
        Storage = storage;
        PublicBaseUri = publicBaseUri;
    }

    /// <summary>获取包含 multipart 协议开销的请求总字节上限。</summary>
    public long MaxBatchBytes => Storage.MaxBatchBytes;

    /// <summary>获取本批次允许的最大文件数。</summary>
    public int MaxFiles => Storage.MaxFiles;

    internal ProductImageStorageContext Storage { get; }
    internal Uri PublicBaseUri { get; }
    internal IReadOnlyList<ProductImageUploadItemResultDto> Items => _items;

    internal bool TryReserveTarget(string targetFileName) => _targets.Add(targetFileName);
    internal void Add(ProductImageUploadItemResultDto item) => _items.Add(item);
}
