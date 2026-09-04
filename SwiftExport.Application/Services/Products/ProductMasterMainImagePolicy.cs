using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Products;

/// <summary>根据详情保存的完整变更集计算最终默认主图状态，防止同一主型号出现多个默认。</summary>
internal static class ProductMasterMainImagePolicy
{
    /// <summary>判断数据库现状应用本次新增、修改和删除后是否会产生多个默认主图。</summary>
    internal static bool HasConflict(ProductMasterDetailDto? existingDetail, SaveProductMasterDetailRequest request)
        => HasConflict(existingDetail, new BatchSaveRequestBase<ProductNeutralWriteDto>
        {
            CreateItems = request.CreateNeutrals,
            UpdateItems = request.UpdateNeutrals,
            DeleteItems = request.DeleteNeutrals
        });

    /// <summary>按通用明细变更集检查最终有效主图数量，供两种保存入口共用。</summary>
    internal static bool HasConflict(ProductMasterDetailDto? existingDetail, BatchSaveRequestBase<ProductNeutralWriteDto> request)
    {
        var existingFlags = existingDetail?.Neutrals.ToDictionary(item => item.Uid, item => item.Data.IsMainImage)
            ?? new Dictionary<Guid, bool>();

        foreach (var item in request.DeleteItems) existingFlags.Remove(item.Uid);
        foreach (var item in request.UpdateItems) existingFlags[item.Uid] = item.Data.IsMainImage;

        var mainImageCount = existingFlags.Values.Count(isMainImage => isMainImage)
            + request.CreateItems.Count(item => item.Data.IsMainImage);
        return mainImageCount > 1;
    }
}
