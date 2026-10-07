using SwiftExport.Contracts.DTOs.Errors;
using Dapper;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>产品 PostgreSQL 仓储的默认供应商专用写入分部，不缓存供应商编码或接管事务。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>按身份、版本和未删除状态设置供应商；空 Guid 原样保存，相同值仍递增版本，不新增强制事务条件。</summary>
    public async Task<RepositoryWriteResult> SetDefaultSupplierAsync(Guid productId, int expectedVersion, Guid supplierId)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("ProductId", productId);
        parameters.Add("ExpectedVersion", expectedVersion);
        parameters.Add("SupplierId", supplierId);
        parameters.AddTimestamp("Now", DateTime.Now);
        parameters.Add("UserName", CurrentUser.UserName);
        const string sql = """
            UPDATE public.product
            SET default_supplier_id=@SupplierId,updated_date=CAST(@Now AS timestamp without time zone),
                updated_by=CAST(@UserName AS text),version=version+1
            WHERE uid=@ProductId AND version=@ExpectedVersion AND is_deleted=FALSE
            RETURNING version
            """;
        var version = await Connection.QuerySingleOrDefaultAsync<int?>(sql, parameters, Transaction);
        return version.HasValue
            ? RepositoryWriteResult.Ok(productId, expectedVersion, version.Value, "默认供应商设置成功。")
            : RepositoryWriteResult.Fail(productId, expectedVersion, ServiceErrorCodes.ConcurrencyConflict, "产品已被其他用户修改，请刷新后重试。");
    }
}
