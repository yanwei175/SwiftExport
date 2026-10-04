using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;

/// <summary>中性产品 PostgreSQL 管理仓储；保留跨主型号分页及编码占用范围，事务由调用方持有。</summary>
public sealed class PostgreSqlProductNeutralManagerRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<ProductNeutral>(context), IProductNeutralManagerRepository
{
    private static readonly string Table = Qualified(typeof(ProductNeutral));
    private static readonly string MasterTable = Qualified(typeof(ProductMaster));
    private static readonly string CategoryTable = Qualified(typeof(ProductCategory));
    private static readonly string ColorTable = Qualified(typeof(ProductColor));
    private static readonly string Columns = string.Join(", ", Metadata.Columns.Select(c =>
        $"pn.{PostgreSqlNames.Quote(c.ColumnName)} AS {PostgreSqlNames.Quote(c.PropertyName)}"));

    /// <summary>管理入口新增仍委托通用仓储，只转换指定中性编码及主型号颜色唯一冲突。</summary>
    public override async Task<RepositoryWriteResult> AddAsync(ProductNeutral entity)
    {
        try { return await base.AddAsync(entity); }
        catch (PostgresException exception) when (PostgreSqlProductNeutralWriteConflict.IsKnown(exception))
        {
            return PostgreSqlProductNeutralWriteConflict.CreateResult(entity.Uid, null, exception);
        }
    }

    /// <summary>管理入口更新保存归一化原版本，唯一冲突回执不使用基类递增后的实体版本。</summary>
    public override async Task<RepositoryWriteResult> UpdateAsync(ProductNeutral entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var oldVersion = entity.Version <= 0 ? 1 : entity.Version;
        try { return await base.UpdateAsync(entity); }
        catch (PostgresException exception) when (PostgreSqlProductNeutralWriteConflict.IsKnown(exception))
        {
            return PostgreSqlProductNeutralWriteConflict.CreateResult(entity.Uid, oldVersion, exception);
        }
    }

    /// <summary>只分页读取未删除中性产品及有效父型号；名称左联接不影响计数，默认及无上限行为沿用源仓储。</summary>
    public async Task<PagedResponseBase<ProductNeutralManagerListItemDto>> GetPagedAsync(
        ProductNeutralManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        request ??= new ProductNeutralManagerSearchRequest();
        var index = request.PageIndex > 0 ? request.PageIndex : 1;
        var size = request.PageSize > 0 ? request.PageSize : 50;
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (index - 1) * size);
        parameters.Add("Limit", size);
        parameters.Add("EmptyGuid", Guid.Empty);
        var where = new StringBuilder("WHERE pn.is_deleted=FALSE AND pm.is_deleted=FALSE");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where.Append(" AND (pn.product_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR pm.master_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR pm.default_description_zh COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR pm.default_description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')");
        }
        var categoryIds = request.CategoryIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        // 真实分类与未分类保持同一 OR 分组，不把空 Guid 混入真实分类集合。
        if (categoryIds.Length > 0)
        {
            parameters.Add("CategoryIds", categoryIds);
            where.Append(request.IncludeUncategorized
                ? " AND (pm.category_id=ANY(@CategoryIds) OR pm.category_id=@EmptyGuid OR pm.category_id IS NULL)"
                : " AND pm.category_id=ANY(@CategoryIds)");
        }
        else if (request.IncludeUncategorized)
            where.Append(" AND (pm.category_id=@EmptyGuid OR pm.category_id IS NULL)");
        var colorIds = request.ColorIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        if (colorIds.Length > 0)
        {
            parameters.Add("ColorIds", colorIds);
            where.Append(" AND pn.color_id=ANY(@ColorIds)");
        }
        var from = $"FROM {Table} pn INNER JOIN {MasterTable} pm ON pm.uid=pn.product_master_id";
        var total = await Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) {from} {where}", parameters, Transaction, cancellationToken: cancellationToken));
        // SQL Server NULLIF(name,'') 也将仅尾部 ASCII 空格视为空；C 比较避免 ICU 忽略字符被当成空值。
        var sql = $"""
            SELECT {Columns}, pm.master_sku AS "MasterSku", pm.default_description_zh AS "ProductMasterDescriptionZh",
                   pm.category_id AS "CategoryId", pc.category_name AS "CategoryName",
                   COALESCE(CASE WHEN rtrim(color.color_name_zh, ' ') COLLATE "C" = '' THEN NULL ELSE color.color_name_zh END,
                            color.color_name_en, color.color_code) AS "ColorName"
            {from}
            LEFT JOIN {CategoryTable} pc ON pc.uid=pm.category_id AND pc.is_deleted=FALSE
            LEFT JOIN {ColorTable} color ON color.uid=pn.color_id AND color.is_deleted=FALSE
            {where}
            ORDER BY pn.product_code COLLATE public.erp_dictionary_ci ASC NULLS FIRST, pn.uid
            LIMIT @Limit OFFSET @Offset
            """;
        var rows = await Connection.QueryAsync<ProductNeutralManagerProjection>(new CommandDefinition(
            sql, parameters, Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<ProductNeutralManagerListItemDto>
        { Items = rows.Select(ToListItem).ToList(), TotalCount = total, PageIndex = index, PageSize = size };
    }

    /// <summary>规范化编码读取严格未删除占用者；不依赖父型号有效性，仅显式锁入口要求外层事务并按身份锁本实体命中行。</summary>
    public async Task<IReadOnlyList<ProductNeutral>> GetByProductCodesAsync(
        IEnumerable<string> productCodes, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var values = productCodes.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        var locking = GetLockHint(withUpdLock);
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Values", values);
        var sql = $"SELECT {Columns} FROM {Table} pn WHERE pn.is_deleted=FALSE AND EXISTS (SELECT 1 FROM unnest(@Values) AS requested(code) WHERE rtrim(pn.product_code, ' ') COLLATE public.erp_dictionary_ci = rtrim(requested.code, ' ') COLLATE public.erp_dictionary_ci) ORDER BY pn.uid{locking}";
        return (await Connection.QueryAsync<ProductNeutral>(new CommandDefinition(
            sql, parameters, Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>固定实体类型通过公共映射解析限定表名，不依赖其他具体业务仓储。</summary>
    private static string Qualified(Type type)
    {
        var metadata = PostgreSqlEntityMetadata.GetMetadata(type);
        return $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
    }

    /// <summary>还原管理页现有可写数据和审计投影；主图标记保持实体内部字段，不扩大传输契约。</summary>
    private static ProductNeutralManagerListItemDto ToListItem(ProductNeutralManagerProjection row) => new()
    {
        Uid = row.Uid, Version = row.Version, IsDeleted = row.IsDeleted,
        CreatedDate = row.CreatedDate, CreatedBy = row.CreatedBy, UpdatedDate = row.UpdatedDate, UpdatedBy = row.UpdatedBy,
        MasterSku = row.MasterSku, ProductMasterDescriptionZh = row.ProductMasterDescriptionZh,
        CategoryId = row.CategoryId, CategoryName = row.CategoryName, ColorName = row.ColorName,
        Data = new ProductNeutralManagerWriteDto
        {
            ProductMasterId = row.ProductMasterId, ProductCode = row.ProductCode, ColorId = row.ColorId,
            DefaultSalesPriceUsd = row.DefaultSalesPriceUsd, DefaultExchangeRate = row.DefaultExchangeRate,
            ProfitMargin = row.ProfitMargin, DefaultPurchasePriceCny = row.DefaultPurchasePriceCny, ImagePath = row.ImagePath
        }
    };

    /// <summary>仅供本管理查询映射联表名称，不污染持久化实体或共享契约。</summary>
    private sealed class ProductNeutralManagerProjection : ProductNeutral
    {
        public string? MasterSku { get; set; }
        public string? ProductMasterDescriptionZh { get; set; }
        public Guid CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? ColorName { get; set; }
    }
}
