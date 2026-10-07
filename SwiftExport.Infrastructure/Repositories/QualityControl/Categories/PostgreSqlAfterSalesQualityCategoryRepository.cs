using Dapper;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl.Categories;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.QualityControl.Categories;

/// <summary>售后分类 PostgreSQL 仓储；沿用公共 CRUD、版本及审计，所有操作借用调用方会话。</summary>
public sealed class PostgreSqlAfterSalesQualityCategoryRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<AfterSalesQualityCategory>(context), IAfterSalesQualityCategoryRepository
{
    private const string Columns = """
        c.uid AS "Uid", c.version AS "Version", c.is_deleted AS "IsDeleted",
        c.created_date AS "CreatedDate", c.created_by AS "CreatedBy",
        c.updated_date AS "UpdatedDate", c.updated_by AS "UpdatedBy",
        c.category_name AS "CategoryName", c.parent_id AS "ParentId",
        c.fake_name AS "FakeName", c.category_code AS "CategoryCode",
        c.sort_no AS "SortNo", c.is_enabled AS "IsEnabled"
        """;

    /// <summary>普通读取保持稳定显示顺序；维护时按身份取锁，Serializable 事务保护新增节点造成的整图幻读。</summary>
    public async Task<IReadOnlyList<AfterSalesQualityCategory>> ReadAllAsync(bool forUpdate, CancellationToken cancellationToken)
    {
        if (forUpdate && Transaction is null) throw new InvalidOperationException("分类维护读取需要调用方事务。");
        var order = forUpdate ? "c.uid FOR UPDATE" : "c.sort_no,c.category_name,c.uid";
        return (await Connection.QueryAsync<AfterSalesQualityCategory>(new CommandDefinition(
            $"SELECT {Columns} FROM public.after_sales_quality_category c WHERE c.is_deleted=FALSE ORDER BY {order}",
            transaction: Transaction, cancellationToken: cancellationToken))).ToArray();
    }

    /// <summary>引用检查读取未删售后头；与售后草稿的 Serializable 引用读取共同保护并发新增引用。</summary>
    public async Task<IReadOnlyList<Guid>> GetReferencedCategoryIdsAsync(IEnumerable<Guid> categoryIds, CancellationToken cancellationToken)
    {
        var ids = categoryIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        if (Transaction is null) throw new InvalidOperationException("分类引用检查需要调用方事务。");
        return (await Connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT quality_category_id FROM public.after_sales_header
            WHERE is_deleted=FALSE AND quality_category_id=ANY(@Uids)
            """, PostgreSqlParameters.Ids(ids), Transaction, cancellationToken: cancellationToken))).ToArray();
    }
}
