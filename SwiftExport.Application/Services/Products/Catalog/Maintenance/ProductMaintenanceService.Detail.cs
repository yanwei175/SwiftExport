using System.Data;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Services.Products.Catalog.Maintenance;

/// <summary>物料详情保存用例；模板来自主型号，选择只归属当前物料，BOM 和包装资料不在可写边界中。</summary>
public partial class ProductMaintenanceService
{
    /// <summary>在唯一 Serializable 事务中保存详情和标签；失败保留原资料，提交确认丢失不自动重试。</summary>
    public async Task<ServiceResult<SaveProductDetailResponse>> SaveDetailAsync(SaveProductDetailRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.ProductId == Guid.Empty || request.ExpectedVersion <= 0 || request.Data is null)
            return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.Validation, "物料标识、预期版本或详情数据无效。");
        if (request.Data.CNYPrice < 0 || request.Data.USDPrice < 0 || request.Data.UnitWeight < 0 || request.Data.UnitCBM < 0)
            return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.Validation, "价格、单品重量和体积不能小于零。");
        if (request.SelectedOptionIds is { } inputs && (inputs.Any(id => id == Guid.Empty) || inputs.Distinct().Count() != inputs.Count))
            return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.Validation, "标签选项标识无效或重复。");
        if (_session.Transaction is not null)
            return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.StateBlocked, "当前请求已有事务，不能重复开始物料详情保存。");

        IDbTransaction? transaction = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction = _session.Connection.BeginTransaction(IsolationLevel.Serializable);
            _session.Transaction = transaction;
            var locked = await LockProductDetailAsync(request, cancellationToken);
            var candidate = locked.Product;
            request.Data.Adapt(candidate);
            // 详情候选由持久化实体建立，适配仅触及写 DTO；空 Guid 不是“未选择”的有效身份。
            if (candidate.ColorId == Guid.Empty || candidate.DefaultSupplierId == Guid.Empty)
                throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "颜色或默认供应商标识不能是空 Guid。");
            var batch = new ProductBatchSaveRequest
            {
                UpdateItems = [new UpdateItemRequest<ProductDto>
                { Uid = request.ProductId, Version = request.ExpectedVersion, Data = candidate.Adapt<ProductDto>() }]
            };
            var validation = await ValidateBatchSaveAsync(batch);
            if (!string.IsNullOrWhiteSpace(validation))
                throw new ProductDetailWriteException(ServiceErrorCodes.BusinessBlocked, validation);
            await ValidateProductSelectionsAsync(locked.ProfileId, locked.SelectedOptionIds, cancellationToken);
            await WriteBatchInCurrentTransactionAsync(batch, new ProductBatchSaveResponse { TotalCount = 1 },
                new Dictionary<Guid, Product> { [request.ProductId] = candidate }, cancellationToken);
            if (request.SelectedOptionIds is not null)
                await SaveProductSelectionsAsync(request.ProductId, locked.SelectedOptionIds, cancellationToken);

            var detail = await productRepository.GetDetailAsync(request.ProductId, cancellationToken)
                ?? throw new ProductDetailWriteException(ServiceErrorCodes.WriteFailed, "无法读取保存后的物料详情。");
            detail.SelectedOptionIds = (await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product,
                [request.ProductId], cancellationToken)).Select(row => row.OptionId).ToList();
            detail.AttributeSelections = (await attributeRepository.GetSelectionDetailsAsync(ProductAttributeOwnerType.Product,
                [request.ProductId], cancellationToken)).ToList();
            cancellationToken.ThrowIfCancellationRequested();
            try { transaction.Commit(); }
            catch (Exception exception)
            {
                TryRollbackTransaction(transaction);
                TryLogSaveFailure(LogLevel.Error, exception, "物料详情保存提交失败，已尝试清理事务。");
                return _session.Errors.Classify(exception) == DatabaseFailureKind.SerializationFailure
                    ? ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.ServiceUnavailable, "并发变化导致事务终止，本次未提交，请刷新后重新保存。")
                    : ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.OutcomeUnknown, "物料详情提交结果无法确认，请刷新并核实结果后再决定后续操作。");
            }
            return ServiceResult<SaveProductDetailResponse>.Ok(new SaveProductDetailResponse { Detail = detail }, "物料详情已保存。");
        }
        catch (ProductDetailWriteException exception)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            return ServiceResult<SaveProductDetailResponse>.Fail(exception.Code,
                rolledBack ? exception.Message : exception.Message + "事务回滚未能确认，请先核实结果。");
        }
        catch (ProductBatchWriteException exception)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.WriteFailed,
                rolledBack ? exception.Message : exception.Message + "事务回滚未能确认，请先核实结果。");
        }
        catch (Exception exception)
        {
            var rolledBack = TryRollbackTransaction(transaction);
            TryLogSaveFailure(LogLevel.Error, exception, "物料详情保存失败，已尝试清理事务。");
            if (_session.Errors.Classify(exception) == DatabaseFailureKind.SerializationFailure)
                return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.ServiceUnavailable, "并发变化导致事务终止，本次未提交，请刷新后重新保存。");
            return ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.Unexpected,
                rolledBack ? "物料详情保存失败，所有修改已回滚，请刷新后重试。" : "物料详情保存失败，事务回滚未能确认，请先核实结果。");
        }
        finally
        {
            if (transaction is not null)
            {
                try { transaction.Dispose(); }
                catch (Exception exception)
                {
                    TryLogSaveFailure(LogLevel.Error, exception, "物料详情事务释放失败，关闭连接。");
                    try { _session.Connection.Dispose(); }
                    catch (Exception connectionException) { TryLogSaveFailure(LogLevel.Error, connectionException, "物料详情连接释放失败。"); }
                }
                finally { _session.Transaction = null; }
            }
        }
    }

    /// <summary>先观察来源和组范围，再依次锁模板、组、来源主型号和物料；锁后范围变化直接冲突，不逆序补锁。</summary>
    private async Task<(Product Product, Guid? ProfileId, IReadOnlyList<Guid> SelectedOptionIds)> LockProductDetailAsync(
        SaveProductDetailRequest request, CancellationToken cancellationToken)
    {
        var observed = await productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (observed is null || observed.IsDeleted)
            throw new ProductDetailWriteException(ServiceErrorCodes.NotFound, "物料不存在或已删除。");
        var observedMaster = observed.ProductMasterId.HasValue
            ? await masterRepository.GetByIdAsync(observed.ProductMasterId.Value) : null;
        var profileId = observedMaster is { IsDeleted: false } ? observedMaster.ProductAttributeProfileId : null;
        var oldSelections = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product, [request.ProductId], cancellationToken);
        var selectedIds = request.SelectedOptionIds?.ToArray() ?? oldSelections.Select(row => row.OptionId).ToArray();
        var lockedGroups = (await attributeRepository.GetGroupIdsForOptionsAsync(oldSelections.Select(row => row.OptionId)
            .Concat(selectedIds).Distinct(), cancellationToken)).ToHashSet();
        var profiles = await attributeRepository.GetProfilesAsync(profileId.HasValue ? [profileId.Value] : [], true, cancellationToken);
        await attributeRepository.LockGroupsForSelectionAsync(lockedGroups.OrderBy(id => id), cancellationToken);
        // 当前资料引用沿用颜色、供应商及仓库的读取顺序；只读校验没有引入反向父锁。
        var referenceError = await ValidateMasterDataReferencesAsync([(request.Data.Adapt<ProductDto>(), observed)]);
        if (referenceError is not null) throw new ProductDetailWriteException(ServiceErrorCodes.BusinessBlocked, referenceError);
        var lockedMaster = observed.ProductMasterId.HasValue
            ? await masterRepository.GetByIdAsync(observed.ProductMasterId.Value, true) : null;
        if (lockedMaster?.IsDeleted != observedMaster?.IsDeleted
            || lockedMaster?.ProductAttributeProfileId != observedMaster?.ProductAttributeProfileId)
            throw new ProductDetailWriteException(ServiceErrorCodes.VersionConflict, "来源主型号或属性模板已变化，请刷新后重新保存。");
        var product = await productRepository.GetByIdAsync(request.ProductId, true);
        if (product is null || product.IsDeleted)
            throw new ProductDetailWriteException(ServiceErrorCodes.NotFound, "物料不存在或已删除。");
        if (product.Version != request.ExpectedVersion || product.ProductMasterId != observed.ProductMasterId)
            throw new ProductDetailWriteException(ServiceErrorCodes.VersionConflict, "物料版本或所属主型号已变化，请刷新后重新保存。");
        var currentSelections = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product, [request.ProductId], cancellationToken);
        var finalIds = request.SelectedOptionIds?.ToArray() ?? currentSelections.Select(row => row.OptionId).ToArray();
        var requiredGroups = (await attributeRepository.GetGroupIdsForOptionsAsync(currentSelections.Select(row => row.OptionId)
            .Concat(finalIds).Distinct(), cancellationToken)).ToHashSet();
        if (!requiredGroups.SetEquals(lockedGroups))
            throw new ProductDetailWriteException(ServiceErrorCodes.VersionConflict, "标签定义或物料选择已变化，请刷新后重新保存。");
        if (finalIds.Length > 0 && (!profileId.HasValue || !profiles.Any(row => row.Uid == profileId.Value && !row.IsDeleted
            && row.IsEnabled && string.Equals(row.DictCode?.Trim(), DictCodes.ProductAttributeProfile, StringComparison.OrdinalIgnoreCase))))
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "来源主型号没有有效属性模板，不能保存非空标签选择。");
        return (product, profileId, finalIds);
    }

    /// <summary>在模板和组锁下重验有效选项及单选限制，不信任前端定义缓存。</summary>
    private async Task ValidateProductSelectionsAsync(Guid? profileId, IReadOnlyList<Guid> selectedIds, CancellationToken cancellationToken)
    {
        if (selectedIds.Any(id => id == Guid.Empty) || selectedIds.Distinct().Count() != selectedIds.Count)
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "标签选项标识无效或重复。");
        if (selectedIds.Count == 0) return;
        if (!profileId.HasValue || profileId == Guid.Empty)
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "物料没有来源属性模板，不能选择标签。");
        var groups = await attributeRepository.GetDefinitionsAsync(profileId, cancellationToken);
        var choices = groups.SelectMany(group => group.Options.Select(option => (Group: group, Option: option)))
            .Where(row => selectedIds.Contains(row.Option.Uid)).ToArray();
        if (choices.Length != selectedIds.Count)
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "部分标签选项已失效或不属于来源属性模板，请刷新标签后明确处理。");
        if (choices.GroupBy(row => row.Group.Uid).Any(group => group.First().Group.SelectionMode == ProductAttributeSelectionMode.Single && group.Count() > 1))
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "单选标签组只能选择一个值。");
    }

    /// <summary>仅替换物料类型的选择差集；同 Uid 的其他产品对象选择保持不变。</summary>
    private async Task SaveProductSelectionsAsync(Guid productId, IReadOnlyList<Guid> selectedIds, CancellationToken cancellationToken)
    {
        var current = await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product, [productId], cancellationToken);
        var finalIds = selectedIds.ToHashSet();
        foreach (var row in current.Where(row => !finalIds.Contains(row.OptionId)).OrderBy(row => row.Uid))
            EnsureWriteSucceeded(await attributeRepository.DeleteSelectionAsync(row.Uid, row.Version), "删除物料标签失败。");
        foreach (var id in finalIds.Except(current.Select(row => row.OptionId)).OrderBy(id => id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWriteSucceeded(await attributeRepository.AddSelectionAsync(new ProductAttributeSelection
            { OwnerType = ProductAttributeOwnerType.Product, OwnerId = productId, OptionId = id }), "保存物料标签失败。");
        }
    }

    /// <summary>物料删除与其选择清理共用事务，明确对象类型避免误删其他对象同身份关系。</summary>
    private Task RemoveProductSelectionsAsync(Guid productId, CancellationToken cancellationToken)
        => SaveProductSelectionsAsync(productId, [], cancellationToken);

    /// <summary>保留详情保存失败类别，避免将版本或适用性冲突映射成内部错误。</summary>
    private sealed class ProductDetailWriteException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
