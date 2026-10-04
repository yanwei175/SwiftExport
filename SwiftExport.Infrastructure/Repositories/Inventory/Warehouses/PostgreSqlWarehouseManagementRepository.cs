using System.Data;
using Dapper;
using Npgsql;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

/// <summary>在共享 PostgreSQL 事务中锁定仓库及供应商真实行，执行虚拟仓版本写入；事务和库存父行锁协议由调用方拥有。</summary>
public sealed class PostgreSqlWarehouseManagementRepository(RepositoryBaseContext context) : IWarehouseManagementRepository
{
    private const string Columns = """
        uid AS "Uid", version AS "Version", is_deleted AS "IsDeleted",
        created_date AS "CreatedDate", created_by AS "CreatedBy", updated_date AS "UpdatedDate", updated_by AS "UpdatedBy",
        code AS "Code", name AS "Name", site_type AS "SiteType", supplier_id AS "SupplierId",
        supplier_code_snapshot AS "SupplierCodeSnapshot", supplier_name_snapshot AS "SupplierNameSnapshot", is_active AS "IsActive"
        """;

    /// <summary>在调用方事务中锁定未删除仓库；库存过账和仓库生命周期共用该父行锁。</summary>
    public Task<WarehouseSite?> GetForUpdateAsync(Guid uid) => context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSite>(
        $"SELECT {Columns} FROM public.warehouse_site WHERE uid=@Uid AND COALESCE(is_deleted,FALSE)=FALSE FOR UPDATE;",
        new { Uid = uid }, RequireTransaction());

    /// <summary>锁定活动未删除供应商的真实行，返回建仓时冻结的权威编码与名称；传递调用方取消信号。</summary>
    public Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSupplierLookupDto>(new CommandDefinition("""
            SELECT uid AS "Uid", supplier_code AS "SupplierCode", supplier_name AS "SupplierName"
            FROM public.supplier WHERE uid=@Uid AND COALESCE(is_deleted,FALSE)=FALSE AND is_active=TRUE FOR UPDATE;
            """, new { Uid = supplierId }, RequireTransaction(), cancellationToken: cancellationToken));

    /// <summary>按 ICU 与末尾空格比较锁定首个未删除仓库；空结果不锁不存在的编码范围，普通 code 唯一索引仍有比较口径缺口。</summary>
    public Task<WarehouseSite?> FindByCodeForUpdateAsync(string code) => context.Session.Connection.QueryFirstOrDefaultAsync<WarehouseSite>(
        $"""
        SELECT {Columns} FROM public.warehouse_site
        WHERE rtrim(code,' ') COLLATE public.erp_dictionary_ci=rtrim(CAST(@Code AS text),' ') COLLATE public.erp_dictionary_ci
          AND COALESCE(is_deleted,FALSE)=FALSE
        ORDER BY uid LIMIT 1 FOR UPDATE;
        """, new { Code = code }, RequireTransaction());

    /// <summary>在当前事务中锁定供应商已有的活动虚拟仓；空结果的并发唯一性仍由数据库索引兜底。</summary>
    public Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId) =>
        context.Session.Connection.QueryFirstOrDefaultAsync<WarehouseSite>($"""
            SELECT {Columns} FROM public.warehouse_site
            WHERE supplier_id=@SupplierId AND site_type=0 AND is_active=TRUE AND COALESCE(is_deleted,FALSE)=FALSE
            ORDER BY uid LIMIT 1 FOR UPDATE;
            """, new { SupplierId = supplierId }, RequireTransaction());

    /// <summary>新增全部仓库字段并打审计戳，枚举按整数、时间按无时区列绑定；任何唯一冲突返回零，调用方必须立即回滚。</summary>
    public async Task<int> InsertAsync(WarehouseSite site)
    {
        context.AuditStamp.StampForInsert(site);
        var parameters = PostgreSqlParameters.Entity(site, PostgreSqlEntityMetadata.GetMetadata(typeof(WarehouseSite)));
        try
        {
            return await context.Session.Connection.ExecuteAsync("""
                INSERT INTO public.warehouse_site
                    (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,code,name,site_type,
                     supplier_id,supplier_code_snapshot,supplier_name_snapshot,is_active)
                VALUES (@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@Code,@Name,@SiteType,
                        @SupplierId,@SupplierCodeSnapshot,@SupplierNameSnapshot,@IsActive);
                """, parameters, context.Session.Transaction);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // 沿用原合同，主键冲突也返回零；事务已中止，不能在此继续查询、重试或自行恢复。
            return 0;
        }
    }

    /// <summary>仅按身份和期望版本更新未删除仓库名称，递增版本并记录当前操作者和本地墙上时间。</summary>
    public Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion)
    {
        var parameters = WriteParameters(site.Uid, expectedVersion);
        parameters.Add("Name", site.Name);
        return context.Session.Connection.ExecuteAsync("""
            UPDATE public.warehouse_site SET name=@Name,version=version+1,updated_date=@Now,updated_by=@UserName
            WHERE uid=@Uid AND version=@ExpectedVersion AND COALESCE(is_deleted,FALSE)=FALSE;
            """, parameters, context.Session.Transaction);
    }

    /// <summary>按身份与版本停用未删除仓库；库存阻断条件由持有父行锁的调用方先行核验。</summary>
    public Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion) => context.Session.Connection.ExecuteAsync("""
        UPDATE public.warehouse_site SET is_active=FALSE,version=version+1,updated_date=@Now,updated_by=@UserName
        WHERE uid=@Uid AND version=@ExpectedVersion AND COALESCE(is_deleted,FALSE)=FALSE;
        """, WriteParameters(site.Uid, expectedVersion), context.Session.Transaction);

    /// <summary>按身份与版本启用未删除仓库；任何唯一冲突返回零并交由调用方立即终止事务，其他异常原样抛出。</summary>
    public async Task<int> ActivateAsync(WarehouseSite site, int expectedVersion)
    {
        try
        {
            return await context.Session.Connection.ExecuteAsync("""
                UPDATE public.warehouse_site SET is_active=TRUE,version=version+1,updated_date=@Now,updated_by=@UserName
                WHERE uid=@Uid AND version=@ExpectedVersion AND COALESCE(is_deleted,FALSE)=FALSE;
                """, WriteParameters(site.Uid, expectedVersion), context.Session.Transaction);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // 保留唯一冲突返回零的合同，不按约束名另造业务分类，也不在已中止事务内重试。
            return 0;
        }
    }

    /// <summary>按身份与版本软删除仓库，同时停用、递增版本并更新审计字段；不物理删除库存历史。</summary>
    public Task<int> DeleteAsync(Guid uid, int expectedVersion) => context.Session.Connection.ExecuteAsync("""
        UPDATE public.warehouse_site SET is_deleted=TRUE,is_active=FALSE,version=version+1,updated_date=@Now,updated_by=@UserName
        WHERE uid=@Uid AND version=@ExpectedVersion AND COALESCE(is_deleted,FALSE)=FALSE;
        """, WriteParameters(uid, expectedVersion), context.Session.Transaction);

    /// <summary>仅以未删除的非零库存余额阻断软删，并锁定命中的真实余额行；调用方须已持有仓库父行锁，空余额范围不视为已锁。</summary>
    public async Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default)
    {
        // 库存过账先取得相同仓库父行锁，因此空范围的并发保护来自父行协议，不依赖 FOR UPDATE 锁住不存在的余额。
        var rows = await context.Session.Connection.QueryAsync<string>(new CommandDefinition("""
            SELECT '库存余额非零' AS "Blocker"
            WHERE EXISTS (SELECT 1 FROM public.inventory_balance
                WHERE warehouse_site_id=@WarehouseSiteId AND is_deleted=FALSE AND on_hand_qty<>0 FOR UPDATE);
            """, new { WarehouseSiteId = warehouseSiteId }, RequireTransaction(), cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>按既有停用口径检查非零未删除余额或任何历史流水；调用方已持有仓库父行锁，不过滤历史流水删除状态。</summary>
    public Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId) => context.Session.Connection.ExecuteScalarAsync<bool>("""
        SELECT EXISTS (SELECT 1 FROM public.inventory_balance
            WHERE warehouse_site_id=@WarehouseSiteId AND COALESCE(is_deleted,FALSE)=FALSE AND on_hand_qty<>0)
            OR EXISTS (SELECT 1 FROM public.inventory_ledger_entry WHERE warehouse_site_id=@WarehouseSiteId);
        """, new { WarehouseSiteId = warehouseSiteId }, context.Session.Transaction);

    /// <summary>拒绝没有调用方事务的锁查询，防止自动提交使读取锁在业务校验前释放。</summary>
    private IDbTransaction RequireTransaction() => context.Session.Transaction
        ?? throw new InvalidOperationException("仓库管理锁查询必须处于调用方事务中。");

    /// <summary>绑定版本写入的身份、审计人及无时区时间，不改变实体或接管事务。</summary>
    private PostgreSqlParameters WriteParameters(Guid uid, int expectedVersion)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        parameters.Add("ExpectedVersion", expectedVersion);
        parameters.AddTimestamp("Now", DateTime.Now);
        parameters.Add("UserName", context.CurrentUser.UserName);
        return parameters;
    }
}
