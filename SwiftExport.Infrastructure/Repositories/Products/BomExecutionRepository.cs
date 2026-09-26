using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>在采购调用方事务内锁定产品、组成及单位范围，防止执行依据读取期间漂移。</summary>
public sealed class BomExecutionRepository(RepositoryBaseContext context) : IBomExecutionRepository
{
    /// <summary>按产品 Uid 读取执行字段并持锁，缺失记录也由范围锁保护。</summary>
    public Task<Product?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        const string sql = """
            SELECT Uid,Version,IsDeleted,IsActive,Sku,Unit,MaterialType,SupplyType,BomNo,BomVersion
            FROM dbo.Product WITH(UPDLOCK,HOLDLOCK)
            WHERE Uid=@ProductId;
            """;
        return context.Session.Connection.QuerySingleOrDefaultAsync<Product>(new CommandDefinition(
            sql, new { ProductId = productId }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>按父 Uid 锁定全部组成范围；不预过滤版本或删除字段，防止掩盖非法终端组成。</summary>
    public async Task<IReadOnlyList<Bom>> GetLinesAsync(Guid parentProductId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        const string sql = """
            SELECT Uid,Version,IsDeleted,BomNo,BomVersion,ParentProductId,ParentSkuSnapshot,
                   LineNumber,ComponentProductId,ComponentSkuSnapshot,Numerator,Denominator
            FROM dbo.Bom WITH(UPDLOCK,HOLDLOCK)
            WHERE ParentProductId=@ParentProductId
            ORDER BY Uid;
            """;
        return (await context.Session.Connection.QueryAsync<Bom>(new CommandDefinition(
            sql, new { ParentProductId = parentProductId }, context.Session.Transaction, cancellationToken: cancellationToken))).ToArray();
    }

    /// <summary>锁定单位字典范围，完整保留启停及删除事实供新承诺准入校验。</summary>
    public async Task<IReadOnlyList<SysDictData>> GetUnitsAsync(CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        const string sql = """
            SELECT Uid,Version,IsDeleted,DictCode,DataNameEn,IsEnabled
            FROM dbo.SysDictData WITH(UPDLOCK,HOLDLOCK)
            WHERE DictCode=N'Unit'
            ORDER BY Uid;
            """;
        return (await context.Session.Connection.QueryAsync<SysDictData>(new CommandDefinition(
            sql, transaction: context.Session.Transaction, cancellationToken: cancellationToken))).ToArray();
    }

    /// <summary>没有调用方事务就拒绝读取，避免锁在单次查询结束后释放形成混合快照。</summary>
    private void RequireTransaction()
    {
        if (context.Session.Transaction is null)
            throw new InvalidOperationException("BOM 执行依据必须在调用方事务内读取。");
    }
}
