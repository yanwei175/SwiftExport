using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class BomRepository(RepositoryBaseContext context) : DapperRepository<Bom>(context), IBomRepository
{
    private static readonly string ProductTable = EntityMetadataCache.GetTableName<Product>();
    private static readonly string BomTable = EntityMetadataCache.GetTableName<Bom>();
    private static readonly string ProductColorTable = EntityMetadataCache.GetTableName<ProductColor>();

    public async Task<IEnumerable<BomDto>> GetBomDtoByBomNoAsync(string bomNo)
    {
        var sql = $@"SELECT
                        b.[Uid],b.[Version],b.[IsDeleted],b.[CreatedDate],b.[CreatedBy],b.[UpdatedDate],b.[UpdatedBy],
                        b.[BomNo],b.[ComponentProductId] AS [SubSkuId],b.[ComponentSkuSnapshot] AS [SubSku],
                        b.[Numerator],b.[Denominator],b.[Note],
                        child.{nameof(Product.DescriptionZh)} AS {nameof(BomDto.Description)},
                        color.{nameof(ProductColor.ColorNameZh)} AS {nameof(BomDto.Finish)},
                        child.{nameof(Product.FeaturesZh)} AS {nameof(BomDto.Features)},
                        child.{nameof(Product.MainImage)} AS {nameof(BomDto.MainImage)}
                    FROM [{BomTable}] b
                    INNER JOIN [{ProductTable}] parent
                        ON b.{nameof(Bom.ParentProductId)} = parent.{nameof(Product.Uid)}
                       AND b.{nameof(Bom.BomNo)} = parent.{nameof(Product.BomNo)}
                       AND b.{nameof(Bom.BomVersion)} = parent.{nameof(Product.BomVersion)}
                       AND parent.{nameof(Product.IsDeleted)} = 0
                    LEFT JOIN [{ProductTable}] child
                        ON b.{nameof(Bom.ComponentProductId)} = child.{nameof(Product.Uid)}
                       AND child.{nameof(Product.IsDeleted)} = 0
                    LEFT JOIN [{ProductColorTable}] color
                        ON child.{nameof(Product.ColorId)} = color.{nameof(ProductColor.Uid)}
                       AND color.{nameof(ProductColor.IsDeleted)} = 0
                    WHERE b.{nameof(Bom.BomNo)} = @BomNo
                      AND b.{nameof(Bom.IsDeleted)} = 0
                    ORDER BY b.{nameof(Bom.LineNumber)}, b.{nameof(Bom.Uid)}";

        return await Connection.QueryAsync<BomDto>(sql, new { BomNo = bomNo.Trim() }, transaction: Transaction);
    }
    public async Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo)
    {
        var sql = $@"SELECT b.*
                    FROM [{BomTable}] b
                    INNER JOIN [{ProductTable}] parent
                        ON b.{nameof(Bom.ParentProductId)} = parent.{nameof(Product.Uid)}
                       AND b.{nameof(Bom.BomNo)} = parent.{nameof(Product.BomNo)}
                       AND b.{nameof(Bom.BomVersion)} = parent.{nameof(Product.BomVersion)}
                       AND parent.{nameof(Product.IsDeleted)} = 0
                    WHERE b.{nameof(Bom.BomNo)} = @BomNo
                      AND b.{nameof(Bom.IsDeleted)} = 0
                    ORDER BY b.{nameof(Bom.LineNumber)}, b.{nameof(Bom.Uid)}";
        return await Connection.QueryAsync<Bom>(sql, new { BomNo = bomNo.Trim() }, transaction: Transaction);
    }

    public async Task<IReadOnlyList<Bom>> GetByBomNoAndVersionAsync(string bomNo, int version)
    {
        if (string.IsNullOrWhiteSpace(bomNo) || version <= 0)
            return [];

        var sql = $@"SELECT
                        [Uid], [Version], [IsDeleted], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy],
                        [BomNo], [BomVersion], [ParentProductId], [ParentSkuSnapshot], [LineNumber],
                        [ComponentProductId], [ComponentSkuSnapshot], [Numerator], [Denominator], [Note]
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
                  AND ((@ProductId <> '00000000-0000-0000-0000-000000000000' AND [ParentProductId] = @ProductId)
                       OR (@BomNo IS NOT NULL AND [BomNo] = @BomNo))
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        return Connection.ExecuteScalarAsync<bool>(
            sql,
            new { ProductId = productId, BomNo = string.IsNullOrWhiteSpace(bomNo) ? null : bomNo.Trim() },
            transaction: Transaction);
    }

    public async Task<IReadOnlyList<Bom>> GetCurrentVersionBomGraphAsync()
    {
        var sql = $@"SELECT b.*
                    FROM [{BomTable}] b WITH(UPDLOCK,HOLDLOCK)
                    INNER JOIN [{ProductTable}] parent
                        ON b.{nameof(Bom.ParentProductId)} = parent.{nameof(Product.Uid)}
                       AND b.{nameof(Bom.BomNo)} = parent.{nameof(Product.BomNo)}
                       AND b.{nameof(Bom.BomVersion)} = parent.{nameof(Product.BomVersion)}
                       AND parent.{nameof(Product.IsDeleted)} = 0
                    WHERE b.{nameof(Bom.IsDeleted)} = 0;";
        var rows = await Connection.QueryAsync<Bom>(sql, transaction: Transaction);
        return [.. rows];
    }

    public async Task<PagedResponseBase<BomDto>> GetPagedAsync(BomSearchRequest request)
    {
        request ??= new BomSearchRequest();
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 500);

        var parameters = new DynamicParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder($@"WHERE b.{nameof(Bom.IsDeleted)} = 0
                                          AND parent.{nameof(Product.IsDeleted)} = 0");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.AppendLine($@"AND (
                   b.{nameof(Bom.BomNo)} LIKE @Keyword ESCAPE '~'
                OR b.{nameof(Bom.ParentSkuSnapshot)} LIKE @Keyword ESCAPE '~'
                OR b.{nameof(Bom.ComponentSkuSnapshot)} LIKE @Keyword ESCAPE '~'
                OR child.{nameof(Product.DescriptionEn)} LIKE @Keyword ESCAPE '~'
                OR child.{nameof(Product.DescriptionZh)} LIKE @Keyword ESCAPE '~'
                OR child.{nameof(Product.FeaturesEn)} LIKE @Keyword ESCAPE '~'
                OR child.{nameof(Product.FeaturesZh)} LIKE @Keyword ESCAPE '~'
                OR color.{nameof(ProductColor.ColorCode)} LIKE @Keyword ESCAPE '~'
                OR color.{nameof(ProductColor.ColorNameEn)} LIKE @Keyword ESCAPE '~'
                OR color.{nameof(ProductColor.ColorNameZh)} LIKE @Keyword ESCAPE '~'
            )");
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }

        var batchKeywords = request.BatchKeywords?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(300)
            .ToArray() ?? [];
        if (batchKeywords.Length > 0)
        {
            where.AppendLine($@"AND (
                   b.{nameof(Bom.BomNo)} IN @BatchKeywords
                OR b.{nameof(Bom.ParentSkuSnapshot)} IN @BatchKeywords
                OR b.{nameof(Bom.ComponentSkuSnapshot)} IN @BatchKeywords
            )");
            parameters.Add("BatchKeywords", batchKeywords);
        }

        var from = $@"FROM [{BomTable}] b WITH(NOLOCK)
                      INNER JOIN [{ProductTable}] parent WITH(NOLOCK)
                          ON b.{nameof(Bom.ParentProductId)} = parent.{nameof(Product.Uid)}
                         AND b.{nameof(Bom.BomNo)} = parent.{nameof(Product.BomNo)}
                         AND b.{nameof(Bom.BomVersion)} = parent.{nameof(Product.BomVersion)}
                      LEFT JOIN [{ProductTable}] child WITH(NOLOCK)
                          ON b.{nameof(Bom.ComponentProductId)} = child.{nameof(Product.Uid)}
                         AND child.{nameof(Product.IsDeleted)} = 0
                      LEFT JOIN [{ProductColorTable}] color WITH(NOLOCK)
                          ON child.{nameof(Product.ColorId)} = color.{nameof(ProductColor.Uid)}
                         AND color.{nameof(ProductColor.IsDeleted)} = 0";

        var sql = $@"SELECT COUNT(1)
                     {from}
                     {where};

                     SELECT
                         b.[Uid],b.[Version],b.[IsDeleted],b.[CreatedDate],b.[CreatedBy],b.[UpdatedDate],b.[UpdatedBy],
                         b.[BomNo],b.[ComponentProductId] AS [SubSkuId],b.[ComponentSkuSnapshot] AS [SubSku],
                         b.[Numerator],b.[Denominator],b.[Note],
                         child.{nameof(Product.DescriptionZh)} AS {nameof(BomDto.Description)},
                         color.{nameof(ProductColor.ColorNameZh)} AS {nameof(BomDto.Finish)},
                         child.{nameof(Product.FeaturesZh)} AS {nameof(BomDto.Features)},
                         child.{nameof(Product.MainImage)} AS {nameof(BomDto.MainImage)}
                     {from}
                     {where}
                     ORDER BY b.{nameof(Bom.BomNo)}, b.{nameof(Bom.LineNumber)}, b.{nameof(Bom.Uid)}
                     OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<BomDto>();
        return new PagedResponseBase<BomDto>
        {
            Items = items.ToList(),
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
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
            $"SELECT COUNT(1) FROM [{BomTable}] WHERE {nameof(Bom.BomNo)} = @BomNo AND {nameof(Bom.ComponentSkuSnapshot)} = @SubSku",
            new { BomNo = bomNo, SubSku = productSku },
            transaction: Transaction
        );
    }

}
