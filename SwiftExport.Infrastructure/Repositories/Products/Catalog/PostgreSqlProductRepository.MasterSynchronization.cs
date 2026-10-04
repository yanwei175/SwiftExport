using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>产品 PostgreSQL 仓储的主型号同步分部；复用外层事务，仅实际资料变化递增版本。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>从数据库有效主型号下发指定资料组，包含停用但排除软删产品，不改型号编码及关联身份。</summary>
    public Task<int> SyncSelectedFieldsFromMastersAsync(IReadOnlyCollection<Guid> productMasterIds,
        ProductMasterSyncFields fields, CancellationToken cancellationToken = default)
        => SynchronizeSelectedProductFieldsAsync(productMasterIds, fields, false, cancellationToken);

    /// <summary>仅所选产品拉取其有效主型号资料，不更新同主型号下其他产品。</summary>
    public Task<int> PullSelectedFieldsFromMastersAsync(IReadOnlyCollection<Guid> productIds,
        ProductMasterSyncFields fields, CancellationToken cancellationToken = default)
        => SynchronizeSelectedProductFieldsAsync(productIds, fields, true, cancellationToken);

    /// <summary>两种集合入口共用固定字段规则；C 比较保留大小写、尾空格及 NULL 的精确差异。</summary>
    private async Task<int> SynchronizeSelectedProductFieldsAsync(IReadOnlyCollection<Guid> ids,
        ProductMasterSyncFields fields, bool byProduct, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (Transaction is null) throw new InvalidOperationException("主型号资料下发必须在应用服务事务内执行。");
        if (ids.Count == 0 || fields == ProductMasterSyncFields.None || (fields & ~ProductMasterSyncFields.All) != 0)
            throw new ArgumentException("标识集合或资料组无效。");
        var parameters = PostgreSqlParameters.Ids(ids);
        parameters.Add("ByProduct", byProduct);
        parameters.Add("Descriptions", fields.HasFlag(ProductMasterSyncFields.Descriptions));
        parameters.Add("Features", fields.HasFlag(ProductMasterSyncFields.Features));
        parameters.Add("Supplier", fields.HasFlag(ProductMasterSyncFields.Supplier));
        parameters.Add("WeightVolume", fields.HasFlag(ProductMasterSyncFields.WeightVolume));
        AddProductSynchronizationStamp(parameters);
        // UPDATE FROM 仅匹配既有关联；毛重和体积直接映射，不按每箱装率换算，也不写 Cbm 包装列。
        const string sql = """
            UPDATE public.product AS p
            SET description_en=CASE WHEN @Descriptions THEN m.default_description_en ELSE p.description_en END,
                description_zh=CASE WHEN @Descriptions THEN m.default_description_zh ELSE p.description_zh END,
                features_en=CASE WHEN @Features THEN m.default_features_en ELSE p.features_en END,
                features_zh=CASE WHEN @Features THEN m.default_features_zh ELSE p.features_zh END,
                default_supplier_id=CASE WHEN @Supplier THEN NULLIF(m.default_supplier_id,@EmptyGuid) ELSE p.default_supplier_id END,
                unit_weight=CASE WHEN @WeightVolume THEN m.default_gross_weight ELSE p.unit_weight END,
                unit_cbm=CASE WHEN @WeightVolume THEN m.default_cbm ELSE p.unit_cbm END,
                updated_date=CAST(@UpdatedDate AS timestamp without time zone),updated_by=CAST(@UpdatedBy AS text),version=p.version+1
            FROM public.product_master AS m
            WHERE m.uid=p.product_master_id AND m.is_deleted=FALSE AND p.is_deleted=FALSE
              AND ((@ByProduct AND p.uid=ANY(@Uids)) OR (NOT @ByProduct AND m.uid=ANY(@Uids)))
              AND ((@Descriptions AND (p.description_en COLLATE "C" IS DISTINCT FROM m.default_description_en COLLATE "C"
                                     OR p.description_zh COLLATE "C" IS DISTINCT FROM m.default_description_zh COLLATE "C"))
                OR (@Features AND (p.features_en COLLATE "C" IS DISTINCT FROM m.default_features_en COLLATE "C"
                                  OR p.features_zh COLLATE "C" IS DISTINCT FROM m.default_features_zh COLLATE "C"))
                OR (@Supplier AND p.default_supplier_id IS DISTINCT FROM NULLIF(m.default_supplier_id,@EmptyGuid))
                OR (@WeightVolume AND (p.unit_weight IS DISTINCT FROM m.default_gross_weight OR p.unit_cbm IS DISTINCT FROM m.default_cbm)))
            """;
        parameters.Add("EmptyGuid", Guid.Empty);
        return await Connection.ExecuteAsync(new CommandDefinition(sql, parameters, Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>按稳定主型号身份同步型号编码及勾选资料；来源无需数据库有效性联接，包含停用且排除软删产品。</summary>
    public async Task<int> SyncFromProductMasterAsync(ProductMaster source, bool updateDescriptions,
        bool updateDefaultSupplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Uid == Guid.Empty) throw new ArgumentException("同步主型号标识不能为空。", nameof(source));
        if (Transaction is null) throw new InvalidOperationException("主型号同步必须在保存事务内执行。");
        var parameters = new PostgreSqlParameters();
        parameters.Add("ProductMasterId", source.Uid);
        parameters.Add("MasterSku", source.MasterSku);
        parameters.Add("DescriptionEn", source.DefaultDescriptionEn);
        parameters.Add("DescriptionZh", source.DefaultDescriptionZh);
        parameters.Add("FeaturesEn", source.DefaultFeaturesEn);
        parameters.Add("FeaturesZh", source.DefaultFeaturesZh);
        parameters.Add("SupplierId", source.DefaultSupplierId == Guid.Empty ? null : (Guid?)source.DefaultSupplierId);
        parameters.Add("UpdateDescriptions", updateDescriptions);
        parameters.Add("UpdateDefaultSupplier", updateDefaultSupplier);
        AddProductSynchronizationStamp(parameters);
        // 同步源值不再次 Trim；C 差异判断对应源 SQL 的二进制 EXCEPT，NULL 可以明确清空。
        const string sql = """
            UPDATE public.product AS p
            SET master_sku=CAST(@MasterSku AS text),
                description_en=CASE WHEN @UpdateDescriptions THEN CAST(@DescriptionEn AS text) ELSE p.description_en END,
                description_zh=CASE WHEN @UpdateDescriptions THEN CAST(@DescriptionZh AS text) ELSE p.description_zh END,
                features_en=CASE WHEN @UpdateDescriptions THEN CAST(@FeaturesEn AS text) ELSE p.features_en END,
                features_zh=CASE WHEN @UpdateDescriptions THEN CAST(@FeaturesZh AS text) ELSE p.features_zh END,
                default_supplier_id=CASE WHEN @UpdateDefaultSupplier THEN CAST(@SupplierId AS uuid) ELSE p.default_supplier_id END,
                updated_date=CAST(@UpdatedDate AS timestamp without time zone),updated_by=CAST(@UpdatedBy AS text),version=p.version+1
            WHERE p.product_master_id=@ProductMasterId AND p.is_deleted=FALSE
              AND (p.master_sku COLLATE "C" IS DISTINCT FROM CAST(@MasterSku AS text) COLLATE "C"
                OR (@UpdateDescriptions AND (p.description_en COLLATE "C" IS DISTINCT FROM CAST(@DescriptionEn AS text) COLLATE "C"
                    OR p.description_zh COLLATE "C" IS DISTINCT FROM CAST(@DescriptionZh AS text) COLLATE "C"
                    OR p.features_en COLLATE "C" IS DISTINCT FROM CAST(@FeaturesEn AS text) COLLATE "C"
                    OR p.features_zh COLLATE "C" IS DISTINCT FROM CAST(@FeaturesZh AS text) COLLATE "C"))
                OR (@UpdateDefaultSupplier AND p.default_supplier_id IS DISTINCT FROM CAST(@SupplierId AS uuid)))
            """;
        return await Connection.ExecuteAsync(new CommandDefinition(sql, parameters, Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>同步方法用公共审计服务打戳并保留本地墙上时间，不接管事务或依赖其他分部私有辅助。</summary>
    private void AddProductSynchronizationStamp(PostgreSqlParameters parameters)
    {
        var stamp = new ProductMaster();
        AuditStamp.StampForUpdate(stamp);
        parameters.AddTimestamp("UpdatedDate", stamp.UpdatedDate);
        parameters.Add("UpdatedBy", stamp.UpdatedBy);
    }
}
