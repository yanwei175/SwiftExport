using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Errors;
using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.ProductMasters;

/// <summary>主型号 PostgreSQL 仓储，提供稳定分页及聚合头部；事务和必要主型号行锁由调用方控制。</summary>
public sealed class PostgreSqlProductMasterRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<ProductMaster>(context), IProductMasterRepository
{
    private const string EntityColumns = """
        pm."uid" AS "Uid",
        pm."version" AS "Version",
        pm."is_deleted" AS "IsDeleted",
        pm."created_date" AS "CreatedDate",
        pm."created_by" AS "CreatedBy",
        pm."updated_date" AS "UpdatedDate",
        pm."updated_by" AS "UpdatedBy",
        pm."master_sku" AS "MasterSku",
        pm."default_description_en" AS "DefaultDescriptionEn",
        pm."default_description_zh" AS "DefaultDescriptionZh",
        pm."default_features_en" AS "DefaultFeaturesEn",
        pm."default_features_zh" AS "DefaultFeaturesZh",
        pm."default_pcs_per_package" AS "DefaultPcsPerPackage",
        pm."default_net_weight" AS "DefaultNetWeight",
        pm."default_gross_weight" AS "DefaultGrossWeight",
        pm."default_cbm" AS "DefaultCbm",
        pm."product_type" AS "ProductType",
        pm."product_tag" AS "ProductTag",
        pm."category_id" AS "CategoryId",
        pm."default_supplier_id" AS "DefaultSupplierId"
    """;
    private const string Joins = """
        LEFT JOIN public.product_category pc ON pc.uid=pm.category_id AND pc.is_deleted=FALSE
        LEFT JOIN public.supplier s ON s.uid=pm.default_supplier_id AND s.is_deleted=FALSE
        """;
    private const string DisplayColumns = """
        CASE WHEN pm.category_id IS NULL OR pm.category_id=@EmptyGuid THEN '未分类'
             ELSE COALESCE(pc.category_name,'分类已失效') END AS "CategoryName",
        s.supplier_name AS "DefaultSupplierName"
        """;

    /// <summary>新增委托通用仓储，仅把 public 主型号编码索引的唯一冲突转换为固定业务回执。</summary>
    public override async Task<RepositoryWriteResult> AddAsync(ProductMaster entity)
    {
        try { return await base.AddAsync(entity); }
        catch (PostgresException exception) when (IsSkuConflict(exception))
        {
            return RepositoryWriteResult.Fail(entity.Uid, null, ProductErrorCodes.ProductMasterSkuOccupied, "主型号编码已被占用");
        }
    }

    /// <summary>更新保留基类实际使用的原版本，编码冲突不把基类已递增的实体版本误当旧版本。</summary>
    public override async Task<RepositoryWriteResult> UpdateAsync(ProductMaster entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var oldVersion = entity.Version <= 0 ? 1 : entity.Version;
        try { return await base.UpdateAsync(entity); }
        catch (PostgresException exception) when (IsSkuConflict(exception))
        {
            return RepositoryWriteResult.Fail(entity.Uid, oldVersion, ProductErrorCodes.ProductMasterSkuOccupied, "主型号编码已被占用");
        }
    }

    /// <summary>精确限定 SQLSTATE、schema、表和索引；主键、其他作用域及其他技术异常保持原样抛出。</summary>
    private static bool IsSkuConflict(PostgresException exception) => exception.SqlState == "23505"
        && exception.SchemaName == "public" && exception.TableName == "product_master"
        && exception.ConstraintName == "ux_product_master_master_sku";

    /// <summary>按主型号及有效中性编码搜索；LATERAL 稳定选择一张主图，EXISTS 不扩大分页行数。</summary>
    public async Task<PagedResponseBase<ProductMasterListItemDto>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default)
    {
        request ??= new ProductMasterSearchRequest();
        var pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var categoryIds = request.CategoryIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        var parameters = PostgreSqlParameters.Ids(categoryIds);
        parameters.Add("EmptyGuid", Guid.Empty);
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("Limit", pageSize);
        var where = BuildFilter(request, parameters);
        var count = await Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM public.product_master pm {where}", parameters, Transaction, cancellationToken: cancellationToken));
        var rows = await Connection.QueryAsync<ProductMasterProjection>(new CommandDefinition($"""
            SELECT {EntityColumns},{DisplayColumns},main_image.image_path AS "MainImagePath",
                main_image.default_sales_price_usd AS "DefaultSalesPriceUsd",main_image.default_purchase_price_cny AS "DefaultPurchasePriceCny"
            FROM public.product_master pm {Joins}
            LEFT JOIN LATERAL (
                SELECT pn.image_path,pn.default_sales_price_usd,pn.default_purchase_price_cny
                FROM public.product_neutral pn WHERE pn.product_master_id=pm.uid AND pn.is_deleted=FALSE AND pn.is_main_image=TRUE
                ORDER BY pn.product_code COLLATE public.erp_dictionary_ci ASC NULLS FIRST,pn.uid LIMIT 1
            ) main_image ON TRUE
            {where} ORDER BY pm.master_sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,pm.uid
            LIMIT @Limit OFFSET @Offset
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<ProductMasterListItemDto>
        { Items = rows.Select(ToListItem).ToList(), TotalCount = count, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>构造分页筛选，只拼接固定 SQL，全部用户值通过参数传递。</summary>
    private static string BuildFilter(ProductMasterSearchRequest request, PostgreSqlParameters parameters)
    {
        var categoryIds = request.CategoryIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        var where = "WHERE pm.is_deleted=FALSE";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where += """
                 AND (pm.master_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR pm.default_description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR pm.default_description_zh COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR pm.product_type COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR pm.product_tag COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR EXISTS (SELECT 1 FROM public.product_neutral pn WHERE pn.product_master_id=pm.uid
                        AND pn.is_deleted=FALSE AND pn.product_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'))
                """;
        }
        // 空标识只代表未分类，不进入真实分类数组；排除条件继续独立作用于组合筛选。
        if (request.IncludeUncategorized && categoryIds.Length > 0)
            where += " AND (pm.category_id=ANY(@Uids) OR pm.category_id=@EmptyGuid OR pm.category_id IS NULL)";
        else if (request.IncludeUncategorized)
            where += " AND (pm.category_id=@EmptyGuid OR pm.category_id IS NULL)";
        else if (categoryIds.Length > 0)
            where += " AND pm.category_id=ANY(@Uids)";
        if (request.ExcludeCategoryId != Guid.Empty)
        {
            parameters.Add("ExcludeCategoryId", request.ExcludeCategoryId);
            where += " AND (pm.category_id IS NULL OR pm.category_id<>@ExcludeCategoryId)";
        }
        return where;
    }

    /// <summary>读取实时分类供应商名称；更新锁仅作用于 pm，不对可空联表侧取锁。</summary>
    public async Task<ProductMasterDetailDto?> GetDetailHeaderAsync(Guid productMasterId, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var lockClause = LockClause(withUpdLock);
        var row = await Connection.QuerySingleOrDefaultAsync<ProductMasterProjection>(new CommandDefinition($"""
            SELECT {EntityColumns},{DisplayColumns} FROM public.product_master pm {Joins}
            WHERE pm.uid=@ProductMasterId AND pm.is_deleted=FALSE{lockClause}
            """, new { ProductMasterId = productMasterId, EmptyGuid = Guid.Empty }, Transaction, cancellationToken: cancellationToken));
        return row is null ? null : ToDetail(row);
    }

    /// <summary>输入 Trim 及忽略大小写去重沿用原规则；编码等值只规范化存储码尾部 ASCII 空格，空集直接返回。</summary>
    public async Task<IReadOnlyList<ProductMaster>> GetByMasterSkusAsync(IEnumerable<string> masterSkus, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var values = masterSkus.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Values", values);
        var lockClause = LockClause(withUpdLock);
        var rows = await Connection.QueryAsync<ProductMaster>(new CommandDefinition($"""
            SELECT {EntityColumns} FROM public.product_master pm WHERE pm.is_deleted=FALSE
            AND EXISTS (SELECT 1 FROM unnest(@Values) input(code)
                WHERE rtrim(pm.master_sku,' ') COLLATE public.erp_dictionary_ci = rtrim(input.code,' ') COLLATE public.erp_dictionary_ci)
            ORDER BY pm.uid{lockClause}
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>只在有效行、预期版本一致且分类实际变化时写分类、审计和递增版本；未命中保留原并发错误。</summary>
    public async Task<RepositoryWriteResult> AssignCategoryAsync(Guid productMasterId, int expectedVersion, Guid targetCategoryId, CancellationToken cancellationToken = default)
    {
        var stamp = new ProductMaster();
        AuditStamp.StampForUpdate(stamp);
        var parameters = new PostgreSqlParameters();
        parameters.Add("ProductMasterId", productMasterId);
        parameters.Add("ExpectedVersion", expectedVersion);
        parameters.Add("TargetCategoryId", targetCategoryId);
        // 墙上审计时间保持 timestamp 语义，不让驱动根据 Local/Utc Kind 改变参数解释。
        parameters.Add("UpdatedDate", stamp.UpdatedDate.HasValue ? DateTime.SpecifyKind(stamp.UpdatedDate.Value, DateTimeKind.Unspecified) : null);
        parameters.Add("UpdatedBy", stamp.UpdatedBy);
        var version = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            UPDATE public.product_master SET category_id=@TargetCategoryId,updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@ProductMasterId AND version=@ExpectedVersion AND is_deleted=FALSE
                AND (category_id IS NULL OR category_id<>@TargetCategoryId)
            RETURNING version
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return version.HasValue
            ? RepositoryWriteResult.Ok(productMasterId, expectedVersion, version.Value, "主型号归类成功。")
            : RepositoryWriteResult.Fail(productMasterId, expectedVersion, ServiceErrorCodes.ConcurrencyConflict, "主型号已被其他用户修改、删除或已归入目标分类，请刷新后重试。");
    }

    /// <summary>需要维护锁时必须使用调用方事务，不创建隐式事务或扩大到分类供应商侧。</summary>
    private string LockClause(bool requested)
    {
        if (requested && Transaction is null) throw new InvalidOperationException("主型号更新锁查询需要调用方事务。");
        return requested ? " FOR UPDATE OF pm" : "";
    }

    /// <summary>列表保持原头部审计、可写数据及主图价格投影，空文本不会自动补为空串。</summary>
    private static ProductMasterListItemDto ToListItem(ProductMasterProjection row) => new()
    {
        Uid = row.Uid,
        Version = row.Version,
        IsDeleted = row.IsDeleted,
        CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy,
        UpdatedDate = row.UpdatedDate,
        UpdatedBy = row.UpdatedBy,
        CategoryName = row.CategoryName,
        DefaultSupplierName = row.DefaultSupplierName,
        MainImagePath = row.MainImagePath,
        DefaultSalesPriceUsd = row.DefaultSalesPriceUsd,
        DefaultPurchasePriceCny = row.DefaultPurchasePriceCny,
        Data = ToWriteDto(row)
    };

    /// <summary>详情保持原头部与主型号写字段，不把分页主图字段混入详情。</summary>
    private static ProductMasterDetailDto ToDetail(ProductMasterProjection row) => new()
    {
        Uid = row.Uid,
        Version = row.Version,
        IsDeleted = row.IsDeleted,
        CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy,
        UpdatedDate = row.UpdatedDate,
        UpdatedBy = row.UpdatedBy,
        CategoryName = row.CategoryName,
        DefaultSupplierName = row.DefaultSupplierName,
        Master = ToWriteDto(row)
    };

    /// <summary>逐项还原原可写 DTO，继承实体不泄露为对外聚合投影。</summary>
    private static ProductMasterWriteDto ToWriteDto(ProductMaster row) => new()
    {
        MasterSku = row.MasterSku,
        DefaultDescriptionEn = row.DefaultDescriptionEn,
        DefaultDescriptionZh = row.DefaultDescriptionZh,
        DefaultFeaturesEn = row.DefaultFeaturesEn,
        DefaultFeaturesZh = row.DefaultFeaturesZh,
        DefaultPcsPerPackage = row.DefaultPcsPerPackage,
        DefaultNetWeight = row.DefaultNetWeight,
        DefaultGrossWeight = row.DefaultGrossWeight,
        DefaultCbm = row.DefaultCbm,
        ProductType = row.ProductType,
        ProductTag = row.ProductTag,
        CategoryId = row.CategoryId,
        DefaultSupplierId = row.DefaultSupplierId
    };

    /// <summary>仓储内部承接实体别名及实时展示字段，不注册全局 Dapper 映射。</summary>
    private sealed class ProductMasterProjection : ProductMaster
    {
        public string? CategoryName { get; set; }
        public string? DefaultSupplierName { get; set; }
        public string? MainImagePath { get; set; }
        public decimal? DefaultSalesPriceUsd { get; set; }
        public decimal? DefaultPurchasePriceCny { get; set; }
    }
}
