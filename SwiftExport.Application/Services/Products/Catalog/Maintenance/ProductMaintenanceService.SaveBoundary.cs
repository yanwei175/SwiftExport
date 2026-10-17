using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Application.Services.Products.Catalog.Maintenance;

/// <summary>统一普通产品写入入口，并保护产品资料变化直接影响的当前 BOM 组成。</summary>
public partial class ProductMaintenanceService
{
    /// <summary>只在调用方事务中写入；未声明更新的单件自重、未携带的单品体积及启停字段保留数据库值。</summary>
    private async Task WriteBatchInCurrentTransactionAsync(ProductBatchSaveRequest request,
        ProductBatchSaveResponse response, IReadOnlyDictionary<Guid, Product>? detailCandidates = null,
        CancellationToken cancellationToken = default)
    {
        if (_session.Transaction is null) throw new InvalidOperationException("产品写入缺少调用方事务。");
        foreach (var item in request.CreateItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = item.Data.Adapt<Product>();
            // 旧列表用空 Guid 表示未选颜色，实体的可空关联必须保存为 null，才能继续使用详情写契约。
            if (item.Data.ColorId == Guid.Empty) created.ColorId = null;
            var result = await _repository.AddAsync(created);
            response.Items.Add(SwiftExport.Contracts.Common.SaveItemResultFactory.FromCreate(item, result));
            EnsureWriteSucceeded(result, "新增产品失败。");
        }
        foreach (var item in request.UpdateItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Product entity;
            if (detailCandidates is not null && detailCandidates.TryGetValue(item.Uid, out var candidate))
            {
                // 详情候选保留可空价格、颜色和单品尺寸，公共校验规范化后的字符串则回填候选。
                entity = candidate;
                entity.Sku = item.Data.Sku;
                entity.LinkSku = item.Data.LinkSku;
                entity.Unit = item.Data.Unit;
                entity.ManufacturerModel = item.Data.ManufacturerModel;
            }
            else
            {
                entity = await _repository.GetByIdAsync(item.Uid)
                    ?? throw new ProductBatchWriteException("产品不存在或已删除。");
                var originalWeight = entity.UnitWeight;
                item.Data.Adapt(entity);
                if (item.Data.ColorId == Guid.Empty) entity.ColorId = null;
                // 旧列表和集成调用方未声明更新自重时保持原值，明确提交则允许清空。
                if (!item.Data.UpdateUnitWeight) entity.UnitWeight = originalWeight;
            }
            entity.Uid = item.Uid;
            entity.Version = item.Version;
            var result = await _repository.UpdateAsync(entity);
            response.Items.Add(SwiftExport.Contracts.Common.SaveItemResultFactory.FromUpdate(item, result));
            EnsureWriteSucceeded(result, "更新产品失败。");
            if (entity.IsDeleted) await RemoveProductSelectionsAsync(item.Uid, cancellationToken);
        }
        foreach (var item in request.DeleteItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
            response.Items.Add(SwiftExport.Contracts.Common.SaveItemResultFactory.FromDelete(item, result));
            EnsureWriteSucceeded(result, "删除产品失败。");
            await RemoveProductSelectionsAsync(item.Uid, cancellationToken);
        }
    }

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

    /// <summary>先检查需要 BOM 的成品资格，再保护既有共享绑定；正标记空编号允许待建立，已绑定成品不能直接解除。</summary>
    private async Task<string?> ValidateBomSaveBoundaryAsync(ProductBatchSaveRequest request)
    {
        // 资格不依赖编号或组成是否已录入；放在图查询跳过判断之前，阻止非成品声明需要 BOM。
        foreach (var data in request.CreateItems.Select(item => item.Data).Concat(request.UpdateItems.Select(item => item.Data)))
        {
            var error = BomParentPolicy.ValidateBinding(data.Adapt<Product>());
            if (error is not null) return error;
        }
        var changedIds = request.UpdateItems.Select(item => item.Uid)
            .Concat(request.DeleteItems.Select(item => item.Uid)).Distinct().ToArray();
        var existing = (await productRepository.GetByIdListAsync(changedIds))
            .Where(product => !product.IsDeleted).ToDictionary(product => product.Uid);
        // 这里只判断完全相同的资料可跳过图查询；不以 C# 等值关系确认共享归属。
        var needsGraph = request.DeleteItems.Count > 0
            || request.CreateItems.Any(item => item.Data.BomVersion > 0)
            || request.UpdateItems.Any(item => !existing.TryGetValue(item.Uid, out var old)
                || item.Data.IsDeleted || old.BomVersion != item.Data.BomVersion
                || !string.Equals(old.BomNo, item.Data.BomNo, StringComparison.Ordinal)
                || !string.Equals(old.MaterialType, item.Data.MaterialType, StringComparison.Ordinal)
                || !string.Equals(old.SupplyType, item.Data.SupplyType, StringComparison.Ordinal));
        if (!needsGraph) return null;
        var requestedNos = request.CreateItems.Select(item => item.Data.BomNo ?? string.Empty)
            .Concat(request.UpdateItems.Select(item => item.Data.BomNo ?? string.Empty)).ToArray();
        var facts = await bomRepository.GetSharedSaveFactsAsync(requestedNos, withUpdLock: false);
        var byInput = facts.SelectMany(group => group.InputIndexes.Select(index => (index, group)))
            .ToDictionary(item => item.index, item => item.group);
        var byParent = facts.SelectMany(group => group.Products.Select(product => (product.Uid, Group: group)))
            .ToDictionary(item => item.Uid, item => item.Group);
        var deletingIds = request.DeleteItems.Select(item => item.Uid)
            .Concat(request.UpdateItems.Where(item => item.Data.IsDeleted).Select(item => item.Uid)).ToHashSet();
        foreach (var group in facts.Where(group => group.Products.Count > 0 && group.Lines.Count > 0))
        {
            // 按全部共享父及有效子身份阻断，不能依赖来源父，也不能用 DTO 软删或同批换组绕过。
            if (group.Products.Any(product => deletingIds.Contains(product.Uid))
                || group.Lines.Any(line => line.ComponentProductId.HasValue && deletingIds.Contains(line.ComponentProductId.Value)))
                return "删除失败：产品仍被当前有效 BOM 引用，请先调整 BOM 组成。";
        }
        var candidates = facts.SelectMany(group => group.Products).Concat(existing.Values)
            .GroupBy(product => product.Uid).ToDictionary(group => group.Key, group => group.Last());
        var candidateGroupById = byParent.ToDictionary(item => item.Key, item => item.Value.GroupKey);
        var affectedKeys = new HashSet<long>();
        var roleChangedIds = new HashSet<Guid>();
        for (var index = 0; index < request.UpdateItems.Count; index++)
        {
            var item = request.UpdateItems[index];
            if (!existing.TryGetValue(item.Uid, out var oldProduct))
                return "修改失败：产品不存在或已删除，请刷新后重试。";
            var candidate = item.Data.Adapt<Product>();
            candidate.Uid = item.Uid;
            candidate.Version = item.Version;
            candidates[item.Uid] = candidate;
            byInput.TryGetValue(request.CreateItems.Count + index, out var targetGroup);
            var wasBound = oldProduct.BomVersion > 0 && !string.IsNullOrWhiteSpace(oldProduct.BomNo);
            var willBind = candidate.BomVersion > 0 && !string.IsNullOrWhiteSpace(candidate.BomNo);
            if (wasBound)
            {
                if (!byParent.TryGetValue(item.Uid, out var oldGroup) || targetGroup is null)
                    return "无法确认产品的共享 BOM 归属，请刷新后重试。";
                if (!willBind || oldGroup.GroupKey != targetGroup.GroupKey)
                    return $"产品 {oldProduct.Sku} 已有有效 BOM，普通保存不能更换共享编号或停用 BOM 标记。";
                // 标记修改触及共享组时仍检查非法非正旧行；不同正数不代表不同组成版本。
                if (oldProduct.BomVersion != candidate.BomVersion && oldGroup.Lines.Count > 0)
                    affectedKeys.Add(oldGroup.GroupKey);
            }
            else if (!candidate.IsDeleted && willBind)
            {
                var bindingError = ValidateNewBinding(candidate, targetGroup, affectedKeys);
                if (bindingError is not null) return bindingError;
            }
            if (willBind && !candidate.IsDeleted && targetGroup is not null)
                candidateGroupById[item.Uid] = targetGroup.GroupKey;
            else candidateGroupById.Remove(item.Uid);
            if (!string.Equals(oldProduct.MaterialType, candidate.MaterialType, StringComparison.Ordinal)
                || !string.Equals(oldProduct.SupplyType, candidate.SupplyType, StringComparison.Ordinal))
                roleChangedIds.Add(item.Uid);
        }
        for (var index = 0; index < request.CreateItems.Count; index++)
        {
            var candidate = request.CreateItems[index].Data.Adapt<Product>();
            // 仓储会重新生成正式身份；临时新身份只用于资格和候选图检查，不能使用客户端传入身份。
            candidate.Uid = Guid.NewGuid();
            candidate.IsDeleted = false;
            candidates[candidate.Uid] = candidate;
            // 待建立只有需要标记，不加入任何共享组，也不要求先有编号。
            if (candidate.BomVersion <= 0 || string.IsNullOrWhiteSpace(candidate.BomNo)) continue;
            byInput.TryGetValue(index, out var targetGroup);
            var bindingError = ValidateNewBinding(candidate, targetGroup, affectedKeys);
            if (bindingError is not null) return bindingError;
            candidateGroupById[candidate.Uid] = targetGroup!.GroupKey;
        }

        foreach (var id in deletingIds)
        {
            candidateGroupById.Remove(id);
            candidates.Remove(id);
        }
        foreach (var id in roleChangedIds)
        {
            if (candidateGroupById.TryGetValue(id, out var key))
            {
                var group = facts.Single(group => group.GroupKey == key);
                if (group.Lines.Count > 0) affectedKeys.Add(key);
                else
                {
                    var parentError = BomParentPolicy.ValidateParent(candidates[id]);
                    if (parentError is not null) return parentError;
                }
            }
            foreach (var group in facts.Where(group => group.Products.Count > 0
                && group.Lines.Any(line => line.ComponentProductId == id)))
                affectedKeys.Add(group.GroupKey);
        }
        if (affectedKeys.Count == 0) return null;
        var finalGroups = facts.Select(group => new SharedBomGroup
        {
            GroupKey = group.GroupKey, BomNo = group.BomNo, InputIndexes = group.InputIndexes, Lines = group.Lines,
            Products = candidateGroupById.Where(item => item.Value == group.GroupKey)
                .Select(item => candidates[item.Key]).ToArray()
        }).ToArray();
        var componentIds = finalGroups.Where(group => affectedKeys.Contains(group.GroupKey))
            .SelectMany(group => group.Lines).Where(line => line.ComponentProductId.HasValue)
            .Select(line => line.ComponentProductId!.Value).Where(id => !candidates.ContainsKey(id))
            .Distinct().ToArray();
        foreach (var product in await productRepository.GetByIdListAsync(componentIds))
            if (!product.IsDeleted) candidates[product.Uid] = product;
        return new SharedBomStructureValidator().Validate(finalGroups, affectedKeys, candidates);
    }

    /// <summary>既有组成只能绑定当前被有效产品使用的正标记共享组；新空编号可准备父产品，孤立或非正旧行不得借此激活。</summary>
    internal static string? ValidateNewBinding(Product product, SharedBomGroup? group, HashSet<long> affectedKeys)
    {
        if (string.IsNullOrWhiteSpace(product.BomNo) || group is null)
            return $"产品 {product.Sku} 的有效 BOM 标记必须有可确认的编号。";
        var parentError = BomParentPolicy.ValidateBinding(product);
        if (parentError is not null) return parentError;
        if (group.Lines.Any(line => !line.IsDeleted && line.BomVersion <= 0))
            return $"BOM {group.BomNo} 存在异常组成有效标记，普通绑定不能启用旧数据。";
        if (group.Lines.Count == 0)
            return group.Products.Count == 0 ? null : $"BOM {group.BomNo} 当前没有有效组成，不能作为已有共享 BOM 绑定。";
        if (group.Products.Count == 0)
            return $"BOM {group.BomNo} 仅有孤立历史组成，普通绑定不能重新激活。";
        affectedKeys.Add(group.GroupKey);
        return null;
    }
}
