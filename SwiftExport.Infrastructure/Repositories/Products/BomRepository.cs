using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class BomRepository(RepositoryBaseContext context) : DapperRepository<Bom>(context), IBomRepository
{
    private static readonly string ProductTable = EntityMetadataCache.GetTableName<Product>();
    private static readonly string BomTable = EntityMetadataCache.GetTableName<Bom>();
    private static readonly string ProductColorTable = EntityMetadataCache.GetTableName<ProductColor>();

    public async Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo)
    {
        var sql = $@"SELECT 
                        t1.*, 
                        t2.{nameof(Product.DescriptionZh)}  as {nameof(BomDto.Description)}, 
                        t3.{nameof(ProductColor.ColorNameZh)} as {nameof(BomDto.Finish)},
                        t2.{nameof(Product.FeaturesZh)} as {nameof(BomDto.Features)},
                        t2.{nameof(Product.MainImage)} as {nameof(BomDto.MainImage)}
                    FROM {BomTable} t1
                    LEFT JOIN {ProductTable} t2 ON t1.{nameof(Bom.SubSkuId)} = t2.{nameof(Product.Uid)}
                    LEFT JOIN {ProductColorTable} t3 ON t2.{nameof(Product.ColorId)} = t3.{nameof(ProductColor.Uid)}
                    WHERE t1.{nameof(Bom.BomNo)} = @BomNo";

        return await Connection.QueryAsync<BomDto>(sql, new { BomNo = bomNo });
    }
    public async Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo)
    {
        var sql = $"SELECT * FROM  [{BomTable}] Where {nameof(Bom.BomNo)} = @BomNo";
        return await Connection.QueryAsync<Bom>(sql, new { BomNo = bomNo });
    }

    public async Task<IReadOnlyList<Bom>> GetByBomNoAndVersionAsync(string bomNo, int version)
    {
        if (string.IsNullOrWhiteSpace(bomNo) || version <= 0)
            return [];

        var sql = $@"SELECT
                        [Uid], [Version], [IsDeleted], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy],
                        [BomNo], [BomVersion], [ParentSkuId], [ParentSku], [LineNumber],
                        [SubSkuId], [SubSku], [Numerator], [Denominator], [Note]
                    FROM [{BomTable}]
                    WHERE [BomNo] = @BomNo
                      AND [BomVersion] = @BomVersion
                      AND [IsDeleted] = 0
                    ORDER BY [LineNumber], [Uid];";

        var rows = await Connection.QueryAsync<Bom>(
            sql,
            new { BomNo = bomNo.Trim(), BomVersion = version },
            transaction: Transaction);
        return [.. rows];
    }

    public Task<bool> HasActiveBomForProductAsync(Guid productId, string? bomNo = null)
    {
        const string sql = @"SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM [Bom]
                WHERE [IsDeleted] = 0
                  AND ((@ProductId <> '00000000-0000-0000-0000-000000000000' AND [ParentSkuId] = @ProductId)
                       OR (@BomNo IS NOT NULL AND [BomNo] = @BomNo))
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        return Connection.ExecuteScalarAsync<bool>(
            sql,
            new { ProductId = productId, BomNo = string.IsNullOrWhiteSpace(bomNo) ? null : bomNo.Trim() },
            transaction: Transaction);
    }
    public async Task<Dictionary<string, IEnumerable<Bom>>> GetBomsCollectionByBomNoListAsync(IEnumerable<string> bomNos)
    {
        if (bomNos == null || !bomNos.Any()) return [];

        // 【核心优化】使用 WHERE IN 进行单次查询
        var sql = $@"SELECT * FROM [{BomTable}] WHERE {nameof(Bom.BomNo)} IN @BomNos";

        // Dapper 会自动将 IEnumerable<string> BomNos 转换为 SQL 参数列表

        var allBoms = await Connection.QueryAsync<Bom>(
                                 sql,
                                 new { BomNos = bomNos });
        // 使用 LINQ 将结果分组并转换为字典
        return allBoms.GroupBy(b => b.BomNo)
                      .ToDictionary(g => g.Key, g => g.AsEnumerable());

    }
    public async Task<int> DeleteBomByBomNoAsync(string bomNo)
    {
        var sql = $"DELETE FROM [{BomTable}] WHERE {nameof(Bom.BomNo)} = @BomNo";
        // 使用基类已有的字段
        return await Connection.ExecuteAsync(sql, new { BomNo = bomNo }, transaction: Transaction);
    }
    public async Task<bool> IsBomItemExistAsync(string bomNo, string productSku)
    {
        return await Connection.ExecuteScalarAsync<bool>(
            $"SELECT COUNT(1) FROM [{BomTable}] WHERE {nameof(Bom.BomNo)} = @BomNo AND {nameof(Bom.SubSku)} = @SubSku",
            new { BomNo = bomNo, SubSku = productSku },
            transaction: Transaction
        );
    }

}
