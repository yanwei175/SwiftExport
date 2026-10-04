using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;
using System.Data;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>实现 SQL Server BOM 组成查询与持久化，复用仓储上下文连接及调用方事务。</summary>
public class BomRepository(RepositoryBaseContext context) : DapperRepository<Bom>(context), IBomRepository
{
    private static readonly string ProductTable = EntityMetadataCache.GetTableName<Product>();
    private static readonly string BomTable = EntityMetadataCache.GetTableName<Bom>();
    private static readonly string ProductColorTable = EntityMetadataCache.GetTableName<ProductColor>();

    /// <summary>按编号读取共享组成及当前展示资料，每个物理行只返回一次，资格不依赖存储父身份。</summary>
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
                    LEFT JOIN [{ProductTable}] child
                        ON b.{nameof(Bom.ComponentProductId)} = child.{nameof(Product.Uid)}
                       AND child.{nameof(Product.IsDeleted)} = 0
                    LEFT JOIN [{ProductColorTable}] color
                        ON child.{nameof(Product.ColorId)} = color.{nameof(ProductColor.Uid)}
                       AND color.{nameof(ProductColor.IsDeleted)} = 0
                    WHERE b.{nameof(Bom.BomNo)} = @BomNo
                      AND b.{nameof(Bom.IsDeleted)} = 0
                      AND {OrdinaryCurrentQualification(false)}
                    ORDER BY b.{nameof(Bom.LineNumber)}, b.{nameof(Bom.Uid)}";

        return await Connection.QueryAsync<BomDto>(sql, new { BomNo = bomNo.Trim() }, transaction: Transaction);
    }
    /// <summary>按编号完整读取共享组成，任意有效未删引用产品提供资格，不复制或改写存储父身份。</summary>
    public async Task<IEnumerable<Bom>> GetBomByBomNoAsync(string bomNo)
    {
        var sql = $@"SELECT
                        b.[Uid], b.[Version], b.[IsDeleted], b.[CreatedDate], b.[CreatedBy], b.[UpdatedDate], b.[UpdatedBy],
                        b.[BomNo], b.[BomVersion], b.[ParentProductId], b.[ParentSkuSnapshot], b.[LineNumber],
                        b.[ComponentProductId], b.[ComponentSkuSnapshot], b.[Numerator], b.[Denominator], b.[Note]
                    FROM [{BomTable}] b
                    WHERE b.{nameof(Bom.BomNo)} = @BomNo
                      AND b.{nameof(Bom.IsDeleted)} = 0
                      AND {OrdinaryCurrentQualification(false)}
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

    /// <summary>按本次产品身份读取当前父子引用，由数据库排序规则判定编号等值；完整映射实体且不增加锁。</summary>
    public async Task<IReadOnlyList<Bom>> GetCurrentReferencesForProductsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds?.Where(uid => uid != Guid.Empty).Distinct().ToArray() ?? [];
        if (ids.Length == 0) return [];
        var rows = await Connection.QueryAsync<Bom>($"""
            SELECT b.[Uid], b.[Version], b.[IsDeleted], b.[CreatedDate], b.[CreatedBy], b.[UpdatedDate], b.[UpdatedBy],
                b.[BomNo], b.[BomVersion], b.[ParentProductId], b.[ParentSkuSnapshot], b.[LineNumber],
                b.[ComponentProductId], b.[ComponentSkuSnapshot], b.[Numerator], b.[Denominator], b.[Note]
            FROM [{BomTable}] b
            INNER JOIN [{ProductTable}] parent ON b.[ParentProductId]=parent.[Uid]
                AND b.[BomNo]=parent.[BomNo] AND b.[BomVersion]=parent.[BomVersion] AND parent.[IsDeleted]=0
            WHERE b.[IsDeleted]=0 AND (b.[ParentProductId] IN @ProductIds OR b.[ComponentProductId] IN @ProductIds)
            ORDER BY b.[Uid]
            """, new { ProductIds = ids }, transaction: Transaction);
        return [.. rows];
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

    /// <summary>分页展示共享组成，以存在性判定资格，计数和结果均不因多个引用产品放大，保留既有搜索与锁提示。</summary>
    public async Task<PagedResponseBase<BomDto>> GetPagedAsync(BomSearchRequest request)
    {
        request ??= new BomSearchRequest();
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 500);

        var parameters = new DynamicParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder($@"WHERE b.{nameof(Bom.IsDeleted)} = 0
                                          AND {OrdinaryCurrentQualification(true)}");
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

    /// <summary>仅供三个普通读取入口判定共享资格，保留分页原锁提示；数值相等只是临时兼容校验，不代表业务选版。</summary>
    private static string OrdinaryCurrentQualification(bool withNoLock) => $@"EXISTS (
        SELECT 1 FROM [{ProductTable}] parent {(withNoLock ? "WITH(NOLOCK)" : "")}
        WHERE b.[BomNo] = parent.[BomNo] AND parent.[IsDeleted] = 0 AND parent.[BomVersion] > 0
          AND b.[BomVersion] = parent.[BomVersion])";

    /// <summary>在调用方事务中单语句读取共享保存事实，由数据库分组编号；保留全部未删组成和正标记产品，不预先掩盖异常标记。</summary>
    public Task<IReadOnlyList<SharedBomGroup>> GetSharedSaveFactsAsync(IReadOnlyList<string> requestedBomNos, bool withUpdLock = true)
    {
        ArgumentNullException.ThrowIfNull(requestedBomNos);
        var transaction = Transaction ?? throw new InvalidOperationException("共享 BOM 保存事实必须在调用方事务内读取。");
        var numbers = requestedBomNos.ToArray();
        var parameters = new DynamicParameters();
        var inputRows = new List<string>();
        for (var index = 0; index < numbers.Length; index++)
        {
            parameters.Add("RequestedBomNo" + index, numbers[index], DbType.String, size: -1);
            inputRows.Add($"({index},CAST(@RequestedBomNo{index} AS nvarchar(max)))");
        }
        var inputs = inputRows.Count == 0
            ? "SELECT CAST(NULL AS int) AS InputIndex, CAST(NULL AS nvarchar(max)) AS BomNo WHERE 1=0"
            : $"SELECT input.InputIndex,input.BomNo FROM (VALUES {string.Join(",", inputRows)}) input(InputIndex,BomNo)";
        // Uid 必须是每个实体投影的首列，Dapper 才能识别 LEFT JOIN 的空实体。
        var bomColumns = SharedFactColumns(Metadata, "b");
        var productColumns = SharedFactColumns(EntityMetadataCache.GetMetadata<Product>(), "p");
        // 产品资料边界只读取事实，不能借复用查询扩大成全图更新锁；组成保存沿用已有保护。
        var compositionHint = withUpdLock ? "WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
        var sql = $"""
            WITH inputs AS ({inputs}), source AS (
                SELECT 0 AS Kind,b0.[Uid] AS EntityUid,CAST(NULL AS int) AS InputIndex,b0.[BomNo] AS BomNo
                FROM [{BomTable}] b0 {compositionHint} WHERE b0.[IsDeleted]=0
                UNION ALL
                SELECT 1,p0.[Uid],CAST(NULL AS int),p0.[BomNo]
                FROM [{ProductTable}] p0 WHERE p0.[IsDeleted]=0 AND p0.[BomVersion]>0
                    AND p0.[BomNo] IS NOT NULL AND LTRIM(RTRIM(p0.[BomNo]))<>N''
                UNION ALL
                SELECT 2,CAST(NULL AS uniqueidentifier),InputIndex,BomNo FROM inputs
            ), ranked AS (
                SELECT Kind,EntityUid,InputIndex,BomNo,DENSE_RANK() OVER(ORDER BY RTRIM(BomNo)) AS GroupKey FROM source
            )
            SELECT r.GroupKey,r.BomNo,r.Kind,r.InputIndex,{bomColumns},{productColumns}
            FROM ranked r
            LEFT JOIN [{BomTable}] b {compositionHint} ON r.Kind=0 AND r.EntityUid=b.[Uid]
            LEFT JOIN [{ProductTable}] p ON r.Kind=1 AND r.EntityUid=p.[Uid]
            ORDER BY r.GroupKey,r.Kind,r.EntityUid,r.InputIndex
            """;
        return BomSharedSaveFactsReader.ReadAsync(Connection, transaction, sql, parameters);
    }

    /// <summary>完整投影提供者元数据字段，Uid 显式排首且别名保留 CLR 名称供三类型映射。</summary>
    private static string SharedFactColumns(EntityMetadata metadata, string alias) => string.Join(",", metadata.Columns
        .OrderBy(column => column.PropertyName == "Uid" ? 0 : 1)
        .Select(column => $"{alias}.[{column.ColumnName.Replace("]", "]]", StringComparison.Ordinal)}] AS [{column.PropertyName}]"));

    /// <summary>按原始编号集合读取全部历史及软删组成，复用调用方事务；数据库筛选编号，结果按存储编号精确分组。</summary>
    public async Task<Dictionary<string, IEnumerable<Bom>>> GetBomsCollectionByBomNoListAsync(IEnumerable<string> bomNos)
    {
        var numbers = bomNos?.ToArray() ?? [];
        if (numbers.Length == 0) return [];

        // 输入只枚举一次且不修剪；不把普通集合读取收紧为当前结构或有效父产品查询。
        var sql = $@"SELECT
                        [Uid], [Version], [IsDeleted], [CreatedDate], [CreatedBy], [UpdatedDate], [UpdatedBy],
                        [BomNo], [BomVersion], [ParentProductId], [ParentSkuSnapshot], [LineNumber],
                        [ComponentProductId], [ComponentSkuSnapshot], [Numerator], [Denominator], [Note]
                    FROM [{BomTable}]
                    WHERE [BomNo] IN @BomNos
                    ORDER BY [Uid]";

        var allBoms = await Connection.QueryAsync<Bom>(sql, new { BomNos = numbers }, transaction: Transaction);
        // 数据库等值编号可能具有不同存储文本，不能在返回字典时合并这些原始键。
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
