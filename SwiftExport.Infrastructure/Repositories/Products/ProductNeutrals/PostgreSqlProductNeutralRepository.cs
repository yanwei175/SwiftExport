using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;

/// <summary>中性产品 PostgreSQL 仓储；复用调用方事务，保留聚合明细及级联软删语义，不接管父行锁协议。</summary>
public sealed class PostgreSqlProductNeutralRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<ProductNeutral>(context), IProductNeutralRepository
{
    private static readonly string Table = $"{PostgreSqlNames.Quote(Metadata.SchemaName)}.{PostgreSqlNames.Quote(Metadata.TableName)}";
    private static readonly EntityMetadata ColorMetadata = PostgreSqlEntityMetadata.GetMetadata(typeof(ProductColor));
    private static readonly string ColorTable = $"{PostgreSqlNames.Quote(ColorMetadata.SchemaName)}.{PostgreSqlNames.Quote(ColorMetadata.TableName)}";
    private static readonly string Columns = string.Join(", ", Metadata.Columns.Select(c =>
        $"pn.{PostgreSqlNames.Quote(c.ColumnName)} AS {PostgreSqlNames.Quote(c.PropertyName)}"));

    /// <summary>新增委托通用仓储，只把两个指定中性唯一索引冲突转换成固定业务回执。</summary>
    public override async Task<RepositoryWriteResult> AddAsync(ProductNeutral entity)
    {
        try { return await base.AddAsync(entity); }
        catch (PostgresException exception) when (PostgreSqlProductNeutralWriteConflict.IsKnown(exception))
        {
            return PostgreSqlProductNeutralWriteConflict.CreateResult(entity.Uid, null, exception);
        }
    }

    /// <summary>更新先保存基类使用的旧版本，唯一冲突回执不误用实体已递增的版本。</summary>
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

    /// <summary>读取严格未软删明细及原颜色名称回退；显式锁读仅锁 pn，不锁外连接颜色行，要求调用方事务。</summary>
    public async Task<IReadOnlyList<ProductNeutralDto>> GetByProductMasterIdAsync(Guid productMasterId,
        bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locking = GetLockHint(withUpdLock);
        if (withUpdLock) locking += " OF pn";
        var rows = await Connection.QueryAsync<Projection>(new CommandDefinition($"""
            SELECT {Columns}, COALESCE(CASE WHEN rtrim(pc.color_name_zh, ' ') COLLATE "C" = '' THEN NULL ELSE pc.color_name_zh END, pc.color_name_en, pc.color_code) AS "ColorName"
            FROM {Table} pn
            LEFT JOIN {ColorTable} pc ON pc.uid=pn.color_id AND pc.is_deleted=FALSE
            WHERE pn.product_master_id=@ProductMasterId AND pn.is_deleted=FALSE
            ORDER BY pn.product_code COLLATE public.erp_dictionary_ci, pn.uid{locking};
            """, new { ProductMasterId = productMasterId }, Transaction, cancellationToken: cancellationToken));
        return rows.Select(ToDto).ToList();
    }

    /// <summary>编码过滤空白、Trim 并去重，返回有效占用者原文本；ICU 查询比较不代表编码唯一索引已适配。</summary>
    public async Task<IReadOnlyList<ProductNeutral>> GetByProductCodesAsync(IEnumerable<string> productCodes,
        bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = productCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        var locking = GetLockHint(withUpdLock);
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", values);
        return (await Connection.QueryAsync<ProductNeutral>(new CommandDefinition($"""
            SELECT {Columns} FROM {Table} pn
            WHERE pn.is_deleted=FALSE AND EXISTS (SELECT 1 FROM unnest(@Codes) AS requested(code)
                WHERE rtrim(pn.product_code, ' ') COLLATE public.erp_dictionary_ci = rtrim(requested.code, ' ') COLLATE public.erp_dictionary_ci)
            ORDER BY pn.uid{locking};
            """, parameters, Transaction, cancellationToken: cancellationToken))).AsList();
    }

    /// <summary>在当前会话内级联软删父型号的有效明细，递增各行版本并刷新原审计字段；零行也按原契约成功。</summary>
    public async Task<RepositoryWriteResult> SoftDeleteByProductMasterIdAsync(Guid productMasterId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("ProductMasterId", productMasterId);
        // 审计列是无时区本地时间；保留源 DateTime.Now 墙上时间并明确 Kind，不让驱动推断成 timestamptz。
        parameters.Add("UpdatedDate", DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified));
        parameters.Add("UpdatedBy", CurrentUser.UserName);
        await Connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE {Table} SET is_deleted=TRUE, version=version+1,
                updated_date=CAST(@UpdatedDate AS timestamp without time zone), updated_by=CAST(@UpdatedBy AS text)
            WHERE product_master_id=@ProductMasterId AND is_deleted=FALSE;
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return RepositoryWriteResult.Ok(productMasterId, null, null, "中性产品已级联软删除。");
    }

    /// <summary>保留 DTO 的聚合身份、版本、审计及价格图片字段，不把显示颜色名写回实体。</summary>
    private static ProductNeutralDto ToDto(Projection row) => new()
    {
        Uid = row.Uid, Version = row.Version, IsDeleted = row.IsDeleted, CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy, UpdatedDate = row.UpdatedDate, UpdatedBy = row.UpdatedBy,
        ProductMasterId = row.ProductMasterId, ColorName = row.ColorName,
        Data = new ProductNeutralWriteDto { ProductCode = row.ProductCode, ColorId = row.ColorId,
            DefaultSalesPriceUsd = row.DefaultSalesPriceUsd, DefaultExchangeRate = row.DefaultExchangeRate,
            ProfitMargin = row.ProfitMargin, DefaultPurchasePriceCny = row.DefaultPurchasePriceCny,
            ImagePath = row.ImagePath, IsMainImage = row.IsMainImage }
    };

    /// <summary>内部投影附加实时颜色名称，不增加持久化实体字段。</summary>
    private sealed class Projection : ProductNeutral
    {
        public string? ColorName { get; set; }
    }
}
