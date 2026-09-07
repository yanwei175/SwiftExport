using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;

namespace SwiftExport.Application.Services.Products;

/// <summary>集中判定跨主型号批次中的默认主图候选，避免保存顺序改变最终主图。</summary>
internal static class ProductNeutralManagerMainImagePolicy
{
    /// <summary>判断同一目标主型号是否被提交了多个默认主图候选。</summary>
    internal static bool HasMultipleWinners(IEnumerable<ProductNeutralManagerWriteDto> writes) =>
        writes.Where(item => item.IsMainImage)
            .GroupBy(item => item.ProductMasterId)
            .Any(group => group.Count() > 1);

    /// <summary>按稳定顺序返回需要自动替换数据库原主图的目标主型号。</summary>
    internal static IReadOnlyList<Guid> GetWinnerMasterIds(IEnumerable<ProductNeutralManagerWriteDto> writes) =>
        writes.Where(item => item.IsMainImage)
            .Select(item => item.ProductMasterId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
}
