using Dapper;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.Common;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>提供包装行读写、稳定物料锁及包材引用事实，沿用调用方事务，不负责业务拒绝或提交。</summary>
public partial class PostgreSqlProductRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Product>> LockProductRowsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        if (Transaction is null) throw new InvalidOperationException("包装与物料身份锁必须位于调用方事务。");
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var parameters = new PostgreSqlParameters(); parameters.AddIds("Ids", ids);
        return (await Connection.QueryAsync<Product>(new CommandDefinition($"""
            SELECT {CatalogEntityColumns} FROM public.product p
            WHERE p.uid=ANY(@Ids) ORDER BY p.uid FOR UPDATE OF p
            """, parameters, Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductPackageDto>> GetPackagesAsync(Guid ownerProductId, bool forUpdate = false, CancellationToken cancellationToken = default)
    {
        if (forUpdate && Transaction is null) throw new InvalidOperationException("包装行锁必须位于调用方事务。");
        var rows = await Connection.QueryAsync<ProductPackageWriteDto, ProductPackageDto, ProductPackageDto>(new CommandDefinition($"""
            SELECT pp.package_type AS "PackageType",pp.packaging_material_id AS "PackagingMaterialId",
                pp.pcs_per_package AS "PcsPerPackage",pp.length_cm AS "Length_CM",pp.width_cm AS "Width_CM",pp.height_cm AS "Height_CM",
                pp.nw_kg AS "Nw_KG",pp.gw_kg AS "Gw_KG",pp.cbm AS "Cbm",pp.note AS "Note",
                pp.uid AS "Uid",pp.version AS "Version",pp.owner_product_id AS "OwnerProductId",
                material.sku AS "PackagingMaterialSku",material.description_zh AS "PackagingMaterialDescription",material.unit AS "PackagingMaterialUnit",
                (pp.packaging_material_id IS NULL OR (material.uid IS NOT NULL AND material.is_deleted=FALSE
                    AND material.is_active=TRUE AND material.material_type=@PackagingType)) AS "IsPackagingMaterialValid"
            FROM public.product_package pp LEFT JOIN public.product material ON material.uid=pp.packaging_material_id
            WHERE pp.owner_product_id=@Owner AND pp.is_deleted=FALSE ORDER BY pp.uid {(forUpdate ? "FOR UPDATE OF pp" : "")}
            """, new { Owner = ownerProductId, PackagingType = MaterialTypesEnum.包材 }, Transaction, cancellationToken: cancellationToken),
            (data, row) => { row.Data = data; return row; }, splitOn: "Uid");
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<RepositoryWriteResult> AddPackageAsync(ProductPackage package, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Transaction is null) throw new InvalidOperationException("包装新增缺少调用方事务。");
        AuditStamp.StampForInsert(package); package.Uid = GuidSequentialGenerator.NewGuid(); package.Version = 1; package.IsDeleted = false;
        var metadata = EntityMetadataCache.GetMetadata<ProductPackage>();
        var columns = string.Join(',', metadata.Columns.Select(column => '"' + column.ColumnName + '"'));
        var values = string.Join(',', metadata.Columns.Select(column => '@' + column.ParameterName));
        var written = await Connection.ExecuteAsync(new CommandDefinition($"INSERT INTO public.product_package ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(package, metadata), Transaction, cancellationToken: cancellationToken));
        return written == 1 ? RepositoryWriteResult.Ok(package.Uid, null, 1, "包装已新增。")
            : RepositoryWriteResult.Fail(package.Uid, null, ServiceErrorCodes.WriteFailed, "包装新增失败。");
    }

    /// <inheritdoc />
    public async Task<RepositoryWriteResult> UpdatePackageAsync(ProductPackage package, CancellationToken cancellationToken = default)
    {
        if (Transaction is null) throw new InvalidOperationException("包装更新缺少调用方事务。");
        AuditStamp.StampForUpdate(package);
        var parameters = PostgreSqlParameters.Entity(package, EntityMetadataCache.GetMetadata<ProductPackage>());
        var nextVersion = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            UPDATE public.product_package SET package_type=@PackageType,packaging_material_id=@PackagingMaterialId,
                pcs_per_package=@PcsPerPackage,length_cm=@Length_CM,width_cm=@Width_CM,height_cm=@Height_CM,
                nw_kg=@Nw_KG,gw_kg=@Gw_KG,cbm=@Cbm,note=@Note,is_deleted=@IsDeleted,
                updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND owner_product_id=@OwnerProductId AND version=@Version AND is_deleted=FALSE RETURNING version
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return nextVersion.HasValue ? RepositoryWriteResult.Ok(package.Uid, package.Version, nextVersion, "包装已保存。")
            : RepositoryWriteResult.Fail(package.Uid, package.Version, ServiceErrorCodes.VersionConflict, "包装版本或所属物料已变化，请刷新。");
    }

    /// <inheritdoc />
    public async Task<int> StagePackageTypeChangesAsync(Guid ownerProductId, IReadOnlyCollection<Guid> packageIds, CancellationToken cancellationToken = default)
    {
        if (Transaction is null) throw new InvalidOperationException("包装类型暂存缺少调用方事务。");
        if (packageIds.Count == 0) return 0;
        var parameters = new PostgreSqlParameters(); parameters.AddIds("Ids", packageIds.ToArray());
        parameters.Add("Owner", ownerProductId); parameters.Add("Token", Guid.NewGuid().ToString("N"));
        // 局部唯一索引无法延迟校验；事务内暂存改型行，外部快照不可见，后续失败则整体回滚。
        return await Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE public.product_package SET package_type='__editing__'||@Token||'_'||replace(uid::text,'-','')
            WHERE owner_product_id=@Owner AND uid=ANY(@Ids) AND is_deleted=FALSE
            """, parameters, Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>以稳定物料身份查未删除包装引用；沿用调用方 Serializable 事务的谓词读，不锁无关业务表。</summary>
    public async Task<IReadOnlyList<Guid>> GetReferencedPackagingMaterialIdsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("Ids", ids);
        return (await Connection.QueryAsync<Guid>("""
            SELECT DISTINCT packaging_material_id FROM public.product_package
            WHERE packaging_material_id=ANY(@Ids) AND is_deleted=FALSE ORDER BY packaging_material_id
            """, parameters, Transaction)).AsList();
    }
}
