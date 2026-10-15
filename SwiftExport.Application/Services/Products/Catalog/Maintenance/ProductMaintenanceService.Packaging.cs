using Mapster;
using SwiftExport.Application.Services.Systems.Dictionaries;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.Products.Catalog.Maintenance;

/// <summary>物料详情的包装子用例：所有值先验证，再在详情父事务中写入，不拥有提交权。</summary>
public partial class ProductMaintenanceService
{
    /// <summary>防止空行、身份复用和跨操作重复；null/空变更保持既有包装，不采用整表替换。</summary>
    private static void ValidatePackagingRequest(ProductPackageChangesRequest? changes)
    {
        if (changes is null) return;
        if (changes.CreateItems is null || changes.UpdateItems is null || changes.DeleteItems is null
            || changes.CreateItems.Any(item => item?.Data is null) || changes.UpdateItems.Any(item => item?.Data is null)
            || changes.DeleteItems.Any(item => item is null))
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装变更存在空行或无效集合。");
        var ids = changes.UpdateItems.Select(item => item.Uid).Concat(changes.DeleteItems.Select(item => item.Uid)).ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length
            || changes.UpdateItems.Any(item => item.Version <= 0) || changes.DeleteItems.Any(item => item.Version <= 0 || !item.IsSoftDelete))
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装行身份、版本或删除方式无效，同一行不能重复更新和删除。");
    }

    /// <summary>锁内校验完整最终集合和改选包材资格，支持删除后新建同类型及类型互换，保持未编辑失效引用。</summary>
    private async Task SavePackagingAsync(SaveProductDetailRequest request, IReadOnlyDictionary<Guid, Product> lockedProducts,
        CancellationToken cancellationToken)
    {
        var changes = request.PackagingChanges;
        if (changes is null || !changes.HasChanges) return;
        var current = (await productRepository.GetPackagesAsync(request.ProductId, true, cancellationToken)).ToDictionary(row => row.Uid);
        foreach (var item in changes.UpdateItems.Select(item => (item.Uid, item.Version))
                     .Concat(changes.DeleteItems.Select(item => (item.Uid, item.Version))))
        {
            if (!current.TryGetValue(item.Uid, out var row) || row.OwnerProductId != request.ProductId)
                throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装行不属于当前物料，不能修改其他物料的包装关系。");
            if (row.Version != item.Version)
                throw new ProductDetailWriteException(ServiceErrorCodes.VersionConflict, "包装行已被其他用户修改，当前编辑未保存，请刷新。");
        }
        var dictionaries = await dictDataRepository.GetByCodeAsync(DictCodes.ProductPackageType);
        var codes = BuiltInDictionaryCatalog.MergeData(dictionaries)
            .Where(row => row.DictCode == DictCodes.ProductPackageType && row.IsEnabled && !row.IsDeleted)
            .Select(row => row.DataNameEn).Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(code => code!.Trim(), code => code!.Trim(), StringComparer.OrdinalIgnoreCase);
        var creates = changes.CreateItems.Select(item => Prepare(item.Data, null)).ToArray();
        var updates = changes.UpdateItems.Select(item => Prepare(item.Data, current[item.Uid])).ToArray();
        var deletedIds = changes.DeleteItems.Select(item => item.Uid).ToHashSet();
        var finalTypes = current.Values.Where(row => !deletedIds.Contains(row.Uid) && updates.All(item => item.Uid != row.Uid))
            .Select(row => row.Data.PackageType).Concat(updates.Select(row => row.PackageType)).Concat(creates.Select(row => row.PackageType));
        if (finalTypes.GroupBy(code => code, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "同一物料只能保留一条相同包装类型的有效规格。");

        // 先释放明确删除的类型，再暂存改型行，解决局部唯一索引即时校验下的类型互换。
        foreach (var item in changes.DeleteItems.OrderBy(item => item.Uid))
        {
            var entity = current[item.Uid].Data.Adapt<ProductPackage>(); entity.OwnerProductId = request.ProductId;
            entity.Uid = item.Uid; entity.Version = item.Version; entity.IsDeleted = true;
            await Write(await productRepository.UpdatePackageAsync(entity, cancellationToken));
        }
        var changingTypes = updates.Where(row => row.PackageType != current[row.Uid].Data.PackageType).Select(row => row.Uid).ToArray();
        if (await productRepository.StagePackageTypeChangesAsync(request.ProductId, changingTypes, cancellationToken) != changingTypes.Length)
            throw new ProductDetailWriteException(ServiceErrorCodes.VersionConflict, "包装类型已变化，未提交任何修改。");
        foreach (var row in updates.OrderBy(row => row.Uid)) await Write(await productRepository.UpdatePackageAsync(row, cancellationToken));
        foreach (var row in creates) await Write(await productRepository.AddPackageAsync(row, cancellationToken));

        ProductPackage Prepare(ProductPackageWriteDto data, ProductPackageDto? existing)
        {
            if (string.IsNullOrWhiteSpace(data.PackageType) || !codes.TryGetValue(data.PackageType.Trim(), out var type))
                throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装类型必须是当前有效字典的英文代码。");
            if (data.PcsPerPackage <= 0) throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装装率必须为正整数。");
            foreach (var measure in new[] { data.Length_CM, data.Width_CM, data.Height_CM, data.Nw_KG, data.Gw_KG })
                ValidateMeasure(measure, 4, 99999999999999.9999m);
            ValidateMeasure(data.Cbm, 6, 999999999999.999999m);
            if (data.Nw_KG.HasValue && data.Gw_KG.HasValue && data.Gw_KG < data.Nw_KG)
                throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装毛重不能小于净重。");
            if (data.Note?.Length > 2000 || type.Length > 300 || data.PackagingMaterialId == Guid.Empty)
                throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装备注、类型代码长度或包材身份无效。");
            if (data.PackagingMaterialId.HasValue && (existing is null || existing.Data.PackagingMaterialId != data.PackagingMaterialId))
            {
                if (!lockedProducts.TryGetValue(data.PackagingMaterialId.Value, out var material) || material.IsDeleted || !material.IsActive
                    || material.MaterialType != MaterialTypesEnum.包材)
                    throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "新选或替换的包材必须是未删除且启用的包材物料。");
            }
            var entity = data.Adapt<ProductPackage>(); entity.OwnerProductId = request.ProductId; entity.PackageType = type;
            if (existing is not null) { entity.Uid = existing.Uid; entity.Version = existing.Version; }
            return entity;
        }
        static void ValidateMeasure(decimal? value, int scale, decimal maximum)
        {
            if (value < 0 || value > maximum || value.HasValue && decimal.Round(value.Value, scale) != value.Value)
                throw new ProductDetailWriteException(ServiceErrorCodes.Validation, "包装尺寸、重量和体积必须非负且符合数据库精度。");
        }
        static Task Write(SwiftExport.Contracts.Wrappers.RepositoryWriteResult result)
        {
            if (!result.Success) throw new ProductDetailWriteException(ServiceErrorCodes.VersionConflict, result.Message ?? "包装写入失败，未提交任何修改。");
            return Task.CompletedTask;
        }
    }
}
