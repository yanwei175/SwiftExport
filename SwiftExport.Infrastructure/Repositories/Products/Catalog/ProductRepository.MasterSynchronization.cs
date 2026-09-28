using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

public partial class ProductRepository
{
    /// <summary>在调用方事务内从数据库主型号现值一次性下发所选资料组；只更新值发生变化的关联产品。</summary>
    public Task<int> SyncSelectedFieldsFromMastersAsync(
        IReadOnlyCollection<Guid> productMasterIds,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default)
        => SyncSelectedMasterFieldsAsync(productMasterIds, fields, false, cancellationToken);

    /// <summary>仅按所选产品 UID 拉取其有效关联主型号资料，不触碰同主型号下其他产品。</summary>
    public Task<int> PullSelectedFieldsFromMastersAsync(
        IReadOnlyCollection<Guid> productIds,
        ProductMasterSyncFields fields,
        CancellationToken cancellationToken = default)
        => SyncSelectedMasterFieldsAsync(productIds, fields, true, cancellationToken);

    /// <summary>两种入口共用同一字段映射和差异判断，区别仅在来源或目标 UID 限定。</summary>
    private async Task<int> SyncSelectedMasterFieldsAsync(
        IReadOnlyCollection<Guid> ids,
        ProductMasterSyncFields fields,
        bool byProduct,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (Transaction is null) throw new InvalidOperationException("主型号资料下发必须在应用服务事务内执行。");
        if (ids.Count == 0 || fields == ProductMasterSyncFields.None || (fields & ~ProductMasterSyncFields.All) != 0)
            throw new ArgumentException("标识集合或资料组无效。");

        var stamp = new ProductMaster();
        AuditStamp.StampForUpdate(stamp);
        // 仅以现有关联匹配，包含停用产品；四组字段分别由服务端选定，型号编码及关联本身不变。
        // 主型号每箱毛重/体积按本用例直接写入 Product.UnitWeight/UnitCBM，不按装箱数换算。
        const string sql = """
UPDATE p
SET DescriptionEn = target.DescriptionEn,
    DescriptionZh = target.DescriptionZh,
    FeaturesEn = target.FeaturesEn,
    FeaturesZh = target.FeaturesZh,
    DefaultSupplierId = target.DefaultSupplierId,
    UnitWeight = target.UnitWeight,
    UnitCBM = target.UnitCBM,
    UpdatedDate = @UpdatedDate,
    UpdatedBy = @UpdatedBy,
    Version = p.Version + 1
FROM dbo.Product AS p
INNER JOIN dbo.ProductMaster AS m ON m.Uid = p.ProductMasterId AND m.IsDeleted = 0
CROSS APPLY (SELECT
    CASE WHEN @Descriptions = 1 THEN m.DefaultDescriptionEn ELSE p.DescriptionEn END AS DescriptionEn,
    CASE WHEN @Descriptions = 1 THEN m.DefaultDescriptionZh ELSE p.DescriptionZh END AS DescriptionZh,
    CASE WHEN @Features = 1 THEN m.DefaultFeaturesEn ELSE p.FeaturesEn END AS FeaturesEn,
    CASE WHEN @Features = 1 THEN m.DefaultFeaturesZh ELSE p.FeaturesZh END AS FeaturesZh,
    CASE WHEN @Supplier = 1 THEN NULLIF(m.DefaultSupplierId, '00000000-0000-0000-0000-000000000000') ELSE p.DefaultSupplierId END AS DefaultSupplierId,
    CASE WHEN @WeightVolume = 1 THEN m.DefaultGrossWeight ELSE p.UnitWeight END AS UnitWeight,
    CASE WHEN @WeightVolume = 1 THEN m.DefaultCbm ELSE p.UnitCBM END AS UnitCBM
) AS target
WHERE ((@ByProduct = 1 AND p.Uid IN @ProductIds) OR (@ByProduct = 0 AND m.Uid IN @ProductMasterIds))
  AND p.IsDeleted = 0
AND EXISTS (
    SELECT CONVERT(varbinary(max), p.DescriptionEn), CONVERT(varbinary(max), p.DescriptionZh),
           CONVERT(varbinary(max), p.FeaturesEn), CONVERT(varbinary(max), p.FeaturesZh),
           p.DefaultSupplierId, p.UnitWeight, p.UnitCBM
    EXCEPT
    SELECT CONVERT(varbinary(max), target.DescriptionEn), CONVERT(varbinary(max), target.DescriptionZh),
           CONVERT(varbinary(max), target.FeaturesEn), CONVERT(varbinary(max), target.FeaturesZh),
           target.DefaultSupplierId, target.UnitWeight, target.UnitCBM
);
""";
        return await Connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ProductIds = ids.ToArray(),
            ProductMasterIds = ids.ToArray(),
            ByProduct = byProduct,
            Descriptions = fields.HasFlag(ProductMasterSyncFields.Descriptions),
            Features = fields.HasFlag(ProductMasterSyncFields.Features),
            Supplier = fields.HasFlag(ProductMasterSyncFields.Supplier),
            WeightVolume = fields.HasFlag(ProductMasterSyncFields.WeightVolume),
            stamp.UpdatedDate,
            stamp.UpdatedBy
        }, transaction: Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>在调用方事务内按稳定主型号关联更新物料编码及所选资料组，只写实际变化的未删除物料。</summary>
    public async Task<int> SyncFromProductMasterAsync(
        ProductMaster source,
        bool updateDescriptions,
        bool updateDefaultSupplier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Uid == Guid.Empty) throw new ArgumentException("同步主型号标识不能为空。", nameof(source));
        if (Transaction is null) throw new InvalidOperationException("主型号同步必须在保存事务内执行。");

        var stamp = new ProductMaster();
        AuditStamp.StampForUpdate(stamp);
        // 只按 ProductMasterId 找关联；停用产品仍可维护资料，不写关联、价格或其他物料字段。
        // CASE 分别控制四个描述/配置字段和供应商 ID；型号编码始终取主型号，未勾选的资料组保留物料当前值。
        // EXCEPT 处理 NULL；字符串按二进制比较，避免大小写及尾部空格差异被数据库排序规则忽略。
        const string sql = """
                            UPDATE p
                            SET MasterSku = target.MasterSku,
                                DescriptionEn = target.DescriptionEn,
                                DescriptionZh = target.DescriptionZh,
                                FeaturesEn = target.FeaturesEn,
                                FeaturesZh = target.FeaturesZh,
                                DefaultSupplierId = target.DefaultSupplierId,
                                UpdatedDate = @UpdatedDate,
                                UpdatedBy = @UpdatedBy,
                                Version = p.Version + 1
                            FROM dbo.Product AS p
                            CROSS APPLY (SELECT
                                @MasterSku AS MasterSku,
                                CASE WHEN @UpdateDescriptions = 1 THEN @DescriptionEn ELSE p.DescriptionEn END AS DescriptionEn,
                                CASE WHEN @UpdateDescriptions = 1 THEN @DescriptionZh ELSE p.DescriptionZh END AS DescriptionZh,
                                CASE WHEN @UpdateDescriptions = 1 THEN @FeaturesEn ELSE p.FeaturesEn END AS FeaturesEn,
                                CASE WHEN @UpdateDescriptions = 1 THEN @FeaturesZh ELSE p.FeaturesZh END AS FeaturesZh,
                                CASE WHEN @UpdateDefaultSupplier = 1 THEN @SupplierId ELSE p.DefaultSupplierId END AS DefaultSupplierId
                            ) AS target
                            WHERE p.ProductMasterId = @ProductMasterId AND p.IsDeleted = 0
                            AND EXISTS (
                                SELECT CONVERT(varbinary(max), p.MasterSku), CONVERT(varbinary(max), p.DescriptionEn),
                                       CONVERT(varbinary(max), p.DescriptionZh), CONVERT(varbinary(max), p.FeaturesEn),
                                       CONVERT(varbinary(max), p.FeaturesZh), p.DefaultSupplierId
                                EXCEPT
                                SELECT CONVERT(varbinary(max), target.MasterSku), CONVERT(varbinary(max), target.DescriptionEn),
                                       CONVERT(varbinary(max), target.DescriptionZh), CONVERT(varbinary(max), target.FeaturesEn),
                                       CONVERT(varbinary(max), target.FeaturesZh), target.DefaultSupplierId
                            );
                            """;
        return await Connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            ProductMasterId = source.Uid,
            source.MasterSku,
            DescriptionEn = source.DefaultDescriptionEn,
            DescriptionZh = source.DefaultDescriptionZh,
            FeaturesEn = source.DefaultFeaturesEn,
            FeaturesZh = source.DefaultFeaturesZh,
            // 空 Guid 表示明确清空物料供应商；本 SQL 仅同步 ID，不更新供应商代码或显示名称。
            SupplierId = source.DefaultSupplierId == Guid.Empty ? (Guid?)null : source.DefaultSupplierId,
            UpdateDescriptions = updateDescriptions,
            UpdateDefaultSupplier = updateDefaultSupplier,
            stamp.UpdatedDate,
            stamp.UpdatedBy
        }, transaction: Transaction, cancellationToken: cancellationToken));
    }
}
