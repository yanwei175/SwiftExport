using Mapster;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.Catalog.Maintenance;

/// <summary>统一普通产品写入入口，并保护产品资料变化直接影响的当前 BOM 组成。</summary>
public partial class ProductMaintenanceService
{
    /// <summary>保留新增请求和行标识，通过批量保存内核执行资料校验及自有事务。</summary>
    public override async Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<ProductDto> item)
        => ToSingleSaveResult(await BatchSaveAsync(new ProductBatchSaveRequest { CreateItems = [item] }));

    /// <summary>以包装请求的身份及版本为更新依据，统一执行资料、BOM 边界与事务校验。</summary>
    public override async Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<ProductDto> item)
        => ToSingleSaveResult(await BatchSaveAsync(new ProductBatchSaveRequest { UpdateItems = [item] }));

    /// <summary>保留软删或物理删除选项，当前有效组成仍引用的父或子产品不能删除。</summary>
    public override async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item)
        => ToSingleSaveResult(await BatchSaveAsync(new ProductBatchSaveRequest { DeleteItems = [item] }));

    /// <summary>成功复用唯一行回执，失败保留批量内核的错误信息，不返回已回滚的行结果。</summary>
    private static ServiceResult<SaveItemResult> ToSingleSaveResult(ServiceResult<ProductBatchSaveResponse> result)
        => result.Success
            ? ServiceResult<SaveItemResult>.Ok(result.Data.Items.Single(), result.Message)
            : new ServiceResult<SaveItemResult> { Success = false, Message = result.Message, Error = result.Error };

    /// <summary>由数据库判定本次产品的当前引用与候选历史组成，角色变化使用全部候选产品校验直接受影响关系。</summary>
    private async Task<string?> ValidateBomSaveBoundaryAsync(ProductBatchSaveRequest request)
    {
        var changedIds = request.UpdateItems.Select(item => item.Uid)
            .Concat(request.DeleteItems.Select(item => item.Uid)).Distinct().ToArray();
        if (changedIds.Length == 0) return null;

        var references = await bomRepository.GetCurrentReferencesForProductsAsync(changedIds);
        var relatedIds = changedIds.Concat(references.SelectMany(line =>
                new[] { line.ParentProductId, line.ComponentProductId })
            .Where(uid => uid.HasValue && uid.Value != Guid.Empty).Select(uid => uid!.Value))
            .Distinct().ToArray();
        var existing = (await productRepository.GetByIdListAsync(relatedIds))
            .Where(product => !product.IsDeleted).ToDictionary(product => product.Uid);
        // 编号等值沿用实际数据库排序规则，不能用 C# 字符串比较重新过滤数据库确认的当前引用。
        var currentLines = references.ToArray();

        var deletingIds = request.DeleteItems.Select(item => item.Uid)
            .Concat(request.UpdateItems.Where(item => item.Data.IsDeleted).Select(item => item.Uid)).ToHashSet();
        // 以修改前当前组成阻断删除，不能在同批通过清父编号或 DTO 软删绕过引用保护。
        foreach (var line in currentLines)
        {
            if ((line.ParentProductId.HasValue && deletingIds.Contains(line.ParentProductId.Value))
                || (line.ComponentProductId.HasValue && deletingIds.Contains(line.ComponentProductId.Value)))
                return "删除失败：产品仍被当前有效 BOM 引用，请先调整 BOM 组成。";
        }

        var candidates = new Dictionary<Guid, Product>(existing);
        var roleChangedIds = new HashSet<Guid>();
        foreach (var item in request.UpdateItems)
        {
            if (!existing.TryGetValue(item.Uid, out var oldProduct))
                return "修改失败：产品不存在或已删除，请刷新后重试。";
            var candidate = item.Data.Adapt<Product>();
            candidate.Uid = item.Uid;
            candidate.Version = item.Version;
            candidates[item.Uid] = candidate;
            var identityChanged = oldProduct.BomVersion != candidate.BomVersion
                || !SameBomNo(oldProduct.BomNo, candidate.BomNo);
            if (identityChanged)
            {
                if (currentLines.Any(line => line.ParentProductId == item.Uid))
                    return $"产品 {oldProduct.Sku} 已有有效 BOM，普通保存不能更改 BOM 编号或版本。";
                if (!candidate.IsDeleted && !string.IsNullOrWhiteSpace(candidate.BomNo) && candidate.BomVersion > 0)
                {
                    var history = await bomRepository.GetByBomNoAndVersionAsync(candidate.BomNo, candidate.BomVersion);
                    if (history.Any(line => line.ParentProductId == item.Uid))
                        return $"产品 {oldProduct.Sku} 的目标 BOM 编号或版本存在历史组成，普通保存不能重新激活。";
                }
            }
            if (!string.Equals(oldProduct.MaterialType, candidate.MaterialType, StringComparison.Ordinal)
                || !string.Equals(oldProduct.SupplyType, candidate.SupplyType, StringComparison.Ordinal))
                roleChangedIds.Add(item.Uid);
        }

        // 角色变化不会新增边；仅复用已有父件和组成策略，不用局部数据冒充完整图做环检测。
        foreach (var line in currentLines.Where(line =>
            (line.ParentProductId.HasValue && roleChangedIds.Contains(line.ParentProductId.Value))
            || (line.ComponentProductId.HasValue && roleChangedIds.Contains(line.ComponentProductId.Value))))
        {
            var parent = candidates[line.ParentProductId!.Value];
            var parentError = CustomerSuppliedMaterialPolicy.ValidateBomParent(parent);
            if (parentError is not null) return parentError;
            if (!line.ComponentProductId.HasValue
                || !candidates.TryGetValue(line.ComponentProductId.Value, out var child) || child.IsDeleted)
                return $"BOM 第 {line.LineNumber} 行的子产品不存在或已删除。";
            var lineError = CustomerSuppliedMaterialPolicy.ValidateBomLine(line, child);
            if (lineError is not null) return lineError;
        }
        return null;
    }

    /// <summary>识别请求是否改变编号；数据库负责组成归属，存储值前导空格不能被请求规范化掩盖。</summary>
    private static bool SameBomNo(string? left, string? right)
        => string.Equals(left?.TrimEnd(' '), right?.TrimEnd(' '), StringComparison.OrdinalIgnoreCase);
}
