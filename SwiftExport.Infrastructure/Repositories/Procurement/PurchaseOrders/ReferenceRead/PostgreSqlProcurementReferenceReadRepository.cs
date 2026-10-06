using Dapper;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.ReferenceRead;

/// <summary>在调用方 PG 事务内读取采购主数据资格与快照；保护已读行，不改变原资格或补造缺失数据。</summary>
public sealed class PostgreSqlProcurementReferenceReadRepository(RepositoryBaseContext context) : IProcurementReferenceReadRepository
{
    /// <summary>读取原客户五字段及未删资格，对已有记录取得共享行锁。</summary>
    public Task<Customer?> GetCustomerAsync(Guid uid, CancellationToken ct) => ReadAsync<Customer>(uid, ["Uid", "Version", "IsActive", "CustomerCode", "CustomerName"], ct);

    /// <summary>读取原供应商五字段，不在仓储新增活动校验。</summary>
    public Task<Supplier?> GetSupplierAsync(Guid uid, CancellationToken ct) => ReadAsync<Supplier>(uid, ["Uid", "Version", "IsActive", "SupplierCode", "SupplierName"], ct);

    /// <summary>读取产品执行属性和当前 BOM 身份，缺失返回空，由原服务判断资格。</summary>
    public Task<Product?> GetProductAsync(Guid uid, CancellationToken ct) => ReadAsync<Product>(uid, ["Uid", "Version", "IsActive", "Sku", "DescriptionZh", "Unit", "MaterialType", "SupplyType", "BomNo", "BomVersion"], ct);

    /// <summary>保留产品、颜色及首条客户/供应商关系快照；外连缺失仍返回空文本，不新增关系资格。</summary>
    public Task<ProcurementProductCandidateDto?> GetProductCandidateAsync(Guid productId, Guid customerId, Guid supplierId, CancellationToken ct)
    {
        var sql = $"""
            SELECT {C<Product>("p", "Uid")} AS "Uid",{C<Product>("p", "Sku")} AS "Sku",
                COALESCE({C<Product>("p", "MainImage")},'') AS "MainImage",COALESCE(cp."CustomerSku",'') AS "CustomerSku",
                COALESCE({C<Product>("p", "LinkSku")},'') AS "LinkSku",COALESCE(sp."SupplierSku",'') AS "SupplierSku",
                COALESCE({C<Product>("p", "DescriptionZh")},'') AS "DescriptionZh",COALESCE({C<Product>("p", "DescriptionEn")},'') AS "DescriptionEn",
                COALESCE(color."ColorNameZh",'') AS "FinishZh",COALESCE({C<Product>("p", "FeaturesZh")},'') AS "FeaturesZh",
                COALESCE({C<Product>("p", "MaterialType")},'') AS "MaterialType",COALESCE({C<Product>("p", "SupplyType")},'') AS "SupplyType",
                COALESCE({C<Product>("p", "Unit")},'') AS "Unit"
            FROM {Table<Product>()} p
            LEFT JOIN LATERAL (
                SELECT {C<ProductColor>("c", "ColorNameZh")} AS "ColorNameZh" FROM {Table<ProductColor>()} c
                WHERE {C<ProductColor>("c", "Uid")}={C<Product>("p", "ColorId")} AND COALESCE({C<ProductColor>("c", "IsDeleted")},FALSE)=FALSE FOR SHARE OF c
            ) color ON TRUE
            LEFT JOIN LATERAL (
                SELECT {C<CustomerProduct>("r", "CustomerSku")} AS "CustomerSku" FROM {Table<CustomerProduct>()} r
                WHERE {C<CustomerProduct>("r", "CustomerId")}=@customerId AND {C<CustomerProduct>("r", "SkuId")}={C<Product>("p", "Uid")}
                    AND {C<CustomerProduct>("r", "IsActive")}=TRUE AND COALESCE({C<CustomerProduct>("r", "IsDeleted")},FALSE)=FALSE
                ORDER BY {UuidOrder(C<CustomerProduct>("r", "Uid"))} LIMIT 1 FOR SHARE OF r
            ) cp ON TRUE
            LEFT JOIN LATERAL (
                SELECT {C<SupplierProduct>("r", "SupplierSku")} AS "SupplierSku" FROM {Table<SupplierProduct>()} r
                WHERE {C<SupplierProduct>("r", "SupplierId")}=@supplierId AND {C<SupplierProduct>("r", "SkuId")}={C<Product>("p", "Uid")}
                    AND COALESCE({C<SupplierProduct>("r", "IsDeleted")},FALSE)=FALSE
                ORDER BY {UuidOrder(C<SupplierProduct>("r", "Uid"))} LIMIT 1 FOR SHARE OF r
            ) sp ON TRUE
            WHERE {C<Product>("p", "Uid")}=@productId AND {C<Product>("p", "IsActive")}=TRUE
                AND COALESCE({C<Product>("p", "IsDeleted")},FALSE)=FALSE FOR SHARE OF p;
            """;
        // FOR SHARE 放在各非空源查询内，避免锁 LEFT JOIN 的可空侧；不存在的关系不假称已得到 PG 行锁。
        return context.Session.Connection.QuerySingleOrDefaultAsync<ProcurementProductCandidateDto>(Command(sql, new { productId, customerId, supplierId }, ct));
    }

    /// <summary>原单位输入只转大写，存储值仅修剪 ASCII 空格；启用匹配恰好一条才返回规范值。</summary>
    public async Task<string?> GetUnitAsync(string unit, CancellationToken ct)
    {
        var sql = $"""
            SELECT btrim({C<SysDictData>("d", "DataNameEn")},' ') FROM {Table<SysDictData>()} d
            WHERE rtrim({C<SysDictData>("d", "DictCode")},' ') COLLATE public.erp_dictionary_ci='Unit'
                AND {C<SysDictData>("d", "IsEnabled")}=TRUE AND COALESCE({C<SysDictData>("d", "IsDeleted")},FALSE)=FALSE
                AND upper(btrim({C<SysDictData>("d", "DataNameEn")},' ')) COLLATE public.erp_dictionary_ci
                    =rtrim(upper(@unit),' ') COLLATE public.erp_dictionary_ci FOR SHARE OF d;
            """;
        var values = (await context.Session.Connection.QueryAsync<string>(Command(sql, new { unit }, ct))).AsList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>原主数据固定投影显式映射属性，按 UID 读取并保护已有记录，不开启事务。</summary>
    private Task<T?> ReadAsync<T>(Guid uid, string[] fields, CancellationToken ct) => context.Session.Connection.QuerySingleOrDefaultAsync<T>(Command($"""
        SELECT {string.Join(',', fields.Select(field => C<T>("t", field) + " AS " + PostgreSqlNames.Quote(field)))} FROM {Table<T>()} t
        WHERE {C<T>("t", "Uid")}=@uid AND COALESCE({C<T>("t", "IsDeleted")},FALSE)=FALSE FOR SHARE OF t;
        """, new { uid }, ct));

    /// <summary>沿源采购持久化事务要求和取消令牌，不替调用方提交或回滚。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken ct) => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <summary>只从现有实体元数据获取表名，所有输入身份与文本均参数化。</summary>
    private static string Table<T>()
    {
        var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        return PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    }

    /// <summary>固定字段转换为转义的 PG 列引用。</summary>
    private static string C<T>(string alias, string property) => alias + "." + PostgreSqlNames.Quote(PostgreSqlEntityMetadata.GetMetadata(typeof(T)).GetProperty(property).ColumnName);

    /// <summary>保持 SQL Server uniqueidentifier 选择首条关系时的排序权重。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
