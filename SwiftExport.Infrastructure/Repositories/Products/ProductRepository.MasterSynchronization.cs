using Dapper;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Infrastructure.Repositories.Products;

public partial class ProductRepository
{
    /// <inheritdoc/>
    public async Task<int> SyncFromProductMasterAsync(
        ProductMaster source,
        string? supplierCode,
        bool updateDescriptions,
        bool updateDefaultSupplier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Uid == Guid.Empty) throw new ArgumentException("同步主型号标识不能为空。", nameof(source));
        if (Transaction is null) throw new InvalidOperationException("主型号同步必须在保存事务内执行。");

        var stamp = new ProductMaster();
        AuditStamp.StampForUpdate(stamp);
        // 只按稳定 UID 找关联；停用产品仍可维护资料，不写关联、价格或其他物料字段。
        // EXCEPT 处理 NULL；字符串按二进制比较，避免大小写及尾部空格差异被数据库排序规则忽略。
        const string sql = """
UPDATE p
SET MasterSku = target.MasterSku,
    DescriptionEn = target.DescriptionEn,
    DescriptionZh = target.DescriptionZh,
    FeaturesEn = target.FeaturesEn,
    FeaturesZh = target.FeaturesZh,
    DefaultSupplierId = target.DefaultSupplierId,
    DefaultSupplierCode = target.DefaultSupplierCode,
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
    CASE WHEN @UpdateDefaultSupplier = 1 THEN @SupplierId ELSE p.DefaultSupplierId END AS DefaultSupplierId,
    CASE WHEN @UpdateDefaultSupplier = 1 THEN @SupplierCode ELSE p.DefaultSupplierCode END AS DefaultSupplierCode
) AS target
WHERE p.ProductMasterId = @ProductMasterId AND p.IsDeleted = 0
AND EXISTS (
    SELECT CONVERT(varbinary(max), p.MasterSku), CONVERT(varbinary(max), p.DescriptionEn),
           CONVERT(varbinary(max), p.DescriptionZh), CONVERT(varbinary(max), p.FeaturesEn),
           CONVERT(varbinary(max), p.FeaturesZh), p.DefaultSupplierId, CONVERT(varbinary(max), p.DefaultSupplierCode)
    EXCEPT
    SELECT CONVERT(varbinary(max), target.MasterSku), CONVERT(varbinary(max), target.DescriptionEn),
           CONVERT(varbinary(max), target.DescriptionZh), CONVERT(varbinary(max), target.FeaturesEn),
           CONVERT(varbinary(max), target.FeaturesZh), target.DefaultSupplierId, CONVERT(varbinary(max), target.DefaultSupplierCode)
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
            SupplierId = source.DefaultSupplierId == Guid.Empty ? (Guid?)null : source.DefaultSupplierId,
            SupplierCode = supplierCode,
            UpdateDescriptions = updateDescriptions,
            UpdateDefaultSupplier = updateDefaultSupplier,
            stamp.UpdatedDate,
            stamp.UpdatedBy
        }, transaction: Transaction, cancellationToken: cancellationToken));
    }
}
