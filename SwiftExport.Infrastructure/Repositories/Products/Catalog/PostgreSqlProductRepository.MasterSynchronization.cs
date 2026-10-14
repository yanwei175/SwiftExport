using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>物料主动获取主型号资料的 PostgreSQL 分部；仅更新明确选集并复用外层事务。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>仅所选产品拉取其有效主型号资料；C 比较保留大小写、尾空格及 NULL 的精确差异。</summary>
    public async Task<int> PullSelectedFieldsFromMastersAsync(IReadOnlyCollection<Guid> productIds,
        ProductMasterSyncFields fields, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (Transaction is null) throw new InvalidOperationException("物料获取主型号资料必须在应用服务事务内执行。");
        if (productIds.Count == 0 || fields == ProductMasterSyncFields.None || (fields & ~ProductMasterSyncFields.All) != 0)
            throw new ArgumentException("标识集合或资料组无效。");
        var parameters = PostgreSqlParameters.Ids(productIds);
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
              AND p.uid=ANY(@Uids)
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

    /// <summary>拉取用公共审计服务打戳并保留本地墙上时间，不接管事务或依赖其他分部私有辅助。</summary>
    private void AddProductSynchronizationStamp(PostgreSqlParameters parameters)
    {
        var stamp = new ProductMaster();
        AuditStamp.StampForUpdate(stamp);
        parameters.AddTimestamp("UpdatedDate", stamp.UpdatedDate);
        parameters.Add("UpdatedBy", stamp.UpdatedBy);
    }
}
