using Dapper;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.QualityControl.FlowManagement;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.QualityControl.FlowManagement;

/// <summary>流量组专用仓储；查询与可取消写入共享实体元数据、审计打戳和调用方事务，不扩展公共仓储。</summary>
public sealed class PostgreSqlFlowRepository(RepositoryBaseContext context) : IFlowRepository
{
    private System.Data.IDbConnection Connection => context.Session.Connection;
    private System.Data.IDbTransaction? Transaction => context.Session.Transaction;

    /// <summary>参数化分页查询，不依赖当前物料或字典，因此历史快照不会因基础资料停用而消失。</summary>
    public async Task<PagedResponseBase<FlowListItemDto>> GetPagedAsync(FlowSearchRequest request, CancellationToken token)
    {
        var (where, parameters) = BuildSearch(request);
        parameters.Add("Limit", request.PageSize);
        parameters.Add("Offset", ((long)request.PageIndex - 1) * request.PageSize);
        var sql = $"SELECT COUNT(*) FROM public.flow_header h WHERE {where}; " +
                  $"SELECT {Projection<FlowHeader>("h")} FROM public.flow_header h WHERE {where} " +
                  "ORDER BY h.test_date DESC,h.uid DESC LIMIT @Limit OFFSET @Offset;";
        using var results = await Connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, Transaction, cancellationToken: token));
        var count = await results.ReadSingleAsync<int>();
        var items = (await results.ReadAsync<FlowListItemDto>()).ToList();
        return new() { Items = items, TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>一次 LEFT JOIN 获取一致的头行快照；没有未删行的历史头仍能返回。</summary>
    public async Task<FlowDetailDto?> GetDetailAsync(Guid id, CancellationToken token)
    {
        FlowDetailDto? detail = null;
        var sql = $"SELECT {Projection<FlowHeader>("h")},{Projection<FlowLine>("l")} " +
                  "FROM public.flow_header h LEFT JOIN public.flow_line l ON l.header_id=h.uid AND l.is_deleted=FALSE " +
                  "WHERE h.uid=@Id AND h.is_deleted=FALSE ORDER BY l.sort_no,l.uid;";
        await Connection.QueryAsync<FlowDetailDto, FlowLineDto, FlowDetailDto>(
            new CommandDefinition(sql, new { Id = id }, Transaction, cancellationToken: token),
            (header, line) =>
            {
                detail ??= header;
                if (line is not null && line.Uid != Guid.Empty) detail.Lines.Add(line);
                return detail;
            }, splitOn: "Uid");
        return detail;
    }

    /// <summary>更新和删除先锁头再读取行，所有组写入口遵守相同锁顺序。</summary>
    public Task<FlowHeader?> ReadHeaderAsync(Guid id, bool forUpdate, CancellationToken token)
    {
        if (forUpdate) RequireTransaction();
        return Connection.QuerySingleOrDefaultAsync<FlowHeader>(new CommandDefinition(
            $"SELECT {Projection<FlowHeader>("h")} FROM public.flow_header h WHERE h.uid=@Id AND h.is_deleted=FALSE" +
            (forUpdate ? " FOR UPDATE" : ""), new { Id = id }, Transaction, cancellationToken: token));
    }

    /// <summary>维护时按行身份排序取锁，防止同组行增改删与其他组写交错。</summary>
    public async Task<IReadOnlyList<FlowLine>> ReadLinesAsync(Guid headerId, CancellationToken token)
    {
        RequireTransaction();
        return (await Connection.QueryAsync<FlowLine>(new CommandDefinition(
            $"SELECT {Projection<FlowLine>("l")} FROM public.flow_line l " +
            "WHERE l.header_id=@HeaderId AND l.is_deleted=FALSE ORDER BY l.uid FOR UPDATE",
            new { HeaderId = headerId }, Transaction, cancellationToken: token))).ToArray();
    }

    /// <summary>共享锁定物料，保证新增时校验的活动资格与 SKU 在本次提交前不被改变。</summary>
    public Task<Product?> ReadMaterialAsync(Guid id, CancellationToken token)
    {
        RequireTransaction();
        return Connection.QuerySingleOrDefaultAsync<Product>(new CommandDefinition(
            $"SELECT {Projection<Product>("p")} FROM public.product p WHERE p.uid=@Id AND p.is_deleted=FALSE FOR SHARE",
            new { Id = id }, Transaction, cancellationToken: token));
    }

    /// <summary>先按身份锁分类再锁明细，与现有字典维护的分类先行顺序一致，快照采用期间禁止资料被修改。</summary>
    public async Task<IReadOnlyList<SysDictData>> ReadDictionaryItemsAsync(IEnumerable<Guid> ids, CancellationToken token)
    {
        RequireTransaction();
        var values = ids.Distinct().Order().ToArray();
        if (values.Length == 0) return [];
        // 分类先于明细取锁，避免与字典管理先锁分类、再改明细的路径形成相反锁序。
        await Connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT t.uid FROM public.sys_dict_type t WHERE t.is_deleted=FALSE " +
            "AND rtrim(t.dict_code,' ')=ANY(@Codes) ORDER BY t.uid FOR SHARE",
            new { Codes = new[] { DictCodes.FlowTestFunction, DictCodes.FlowLimiterSpecification } },
            Transaction, cancellationToken: token));
        return (await Connection.QueryAsync<SysDictData>(new CommandDefinition(
            $"SELECT {Projection<SysDictData>("d")} FROM public.sys_dict_data d " +
            "JOIN public.sys_dict_type t ON t.dict_code=d.dict_code AND t.is_deleted=FALSE " +
            "WHERE d.uid=ANY(@Uids) AND d.is_deleted=FALSE ORDER BY d.uid FOR SHARE OF d",
            PostgreSqlParameters.Ids(values), Transaction, cancellationToken: token))).ToArray();
    }

    /// <summary>新增流量头并生成审计及身份。</summary>
    public Task<RepositoryWriteResult> AddHeaderAsync(FlowHeader entity, CancellationToken token) => WriteEntityAsync(entity, true, token);
    /// <summary>按预期版本更新流量头，整组任何行变更也必须递增头版本。</summary>
    public Task<RepositoryWriteResult> UpdateHeaderAsync(FlowHeader entity, CancellationToken token) => WriteEntityAsync(entity, false, token);
    /// <summary>保留头部历史并软删除。</summary>
    public Task<RepositoryWriteResult> DeleteHeaderAsync(Guid id, int version, CancellationToken token) => SoftDeleteAsync<FlowHeader>(id, version, token);
    /// <summary>新增一条功能测试结果。</summary>
    public Task<RepositoryWriteResult> AddLineAsync(FlowLine entity, CancellationToken token) => WriteEntityAsync(entity, true, token);
    /// <summary>按预期版本更新功能测试结果。</summary>
    public Task<RepositoryWriteResult> UpdateLineAsync(FlowLine entity, CancellationToken token) => WriteEntityAsync(entity, false, token);
    /// <summary>保留行的历史数据并软删除。</summary>
    public Task<RepositoryWriteResult> DeleteLineAsync(Guid id, int version, CancellationToken token) => SoftDeleteAsync<FlowLine>(id, version, token);

    /// <summary>本模块头行可取消写入；复用公共元数据及参数映射，保留审计、Version 和未删条件。</summary>
    private async Task<RepositoryWriteResult> WriteEntityAsync<T>(T entity, bool create, CancellationToken token) where T : BaseEntity
    {
        RequireTransaction();
        token.ThrowIfCancellationRequested();
        var metadata = EntityMetadataCache.GetMetadata<T>();
        var table = Qualified(metadata);
        if (create)
        {
            entity.Uid = GuidSequentialGenerator.NewGuid();
            entity.Version = 1;
            entity.IsDeleted = false;
            context.AuditStamp.StampForInsert(entity);
            var columns = string.Join(",", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
            var parameters = string.Join(",", metadata.Columns.Select(c => "@" + c.ParameterName));
            var affected = await Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {table} ({columns}) VALUES ({parameters})",
                PostgreSqlParameters.Entity(entity, metadata), Transaction, cancellationToken: token));
            return affected == 1 ? RepositoryWriteResult.Ok(entity.Uid, null, 1) :
                RepositoryWriteResult.Fail(entity.Uid, null, ServiceErrorCodes.InsertFailed, "流量记录新增失败。");
        }
        var expectedVersion = entity.Version;
        context.AuditStamp.StampForUpdate(entity);
        var set = string.Join(",", metadata.Columns.Where(c => c.PropertyName is not nameof(BaseEntity.Uid) and not nameof(BaseEntity.Version))
            .Select(c => $"{PostgreSqlNames.Quote(c.ColumnName)}=@{c.ParameterName}"));
        var args = PostgreSqlParameters.Entity(entity, metadata);
        args.Add("ExpectedVersion", expectedVersion);
        var version = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            $"UPDATE {table} SET {set},version=version+1 WHERE uid=@Uid AND version=@ExpectedVersion AND is_deleted=FALSE RETURNING version",
            args, Transaction, cancellationToken: token));
        if (!version.HasValue) return RepositoryWriteResult.Fail(entity.Uid, expectedVersion, ServiceErrorCodes.ConcurrencyConflict, "流量记录已被修改或删除。");
        entity.Version = version.Value;
        return RepositoryWriteResult.Ok(entity.Uid, expectedVersion, version.Value);
    }

    /// <summary>沿用公共仓储软删除语义：匹配版本并打戳，不提供物理清理入口。</summary>
    private async Task<RepositoryWriteResult> SoftDeleteAsync<T>(Guid id, int version, CancellationToken token) where T : BaseEntity
    {
        RequireTransaction();
        var sql = $"UPDATE {Qualified(EntityMetadataCache.GetMetadata<T>())} SET is_deleted=TRUE,updated_date=@UpdatedDate,updated_by=@UpdatedBy " +
                  "WHERE uid=@Id AND version=@Version AND is_deleted=FALSE RETURNING version";
        var found = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(sql,
            new { Id = id, Version = version, UpdatedDate = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified), UpdatedBy = context.CurrentUser.UserName },
            Transaction, cancellationToken: token));
        return found.HasValue ? RepositoryWriteResult.Ok(id, version, found.Value) :
            RepositoryWriteResult.Fail(id, version, ServiceErrorCodes.ConcurrencyConflict, "流量记录已被修改或删除。");
    }

    /// <summary>为固定实体生成带 CLR 别名的完整投影，不拼接用户输入，也不使用 SELECT *。</summary>
    private static string Projection<T>(string alias) where T : BaseEntity => string.Join(",", EntityMetadataCache.GetMetadata<T>().Columns
        .Select(c => $"{alias}.{PostgreSqlNames.Quote(c.ColumnName)} AS {PostgreSqlNames.Quote(c.PropertyName)}"));
    private static string Qualified(EntityMetadata metadata) => $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";

    /// <summary>SKU 按字面子串搜索，百分号等字符不会变成通配符；日期上下界均包含当天。</summary>
    internal static (string Where, DynamicParameters Parameters) BuildSearch(FlowSearchRequest request)
    {
        var clauses = new List<string> { "h.is_deleted=FALSE" };
        var args = new DynamicParameters();
        if (request.MaterialId.HasValue) { clauses.Add("h.material_id=@MaterialId"); args.Add("MaterialId", request.MaterialId.Value); }
        var sku = string.IsNullOrWhiteSpace(request.SkuSnapshot) ? request.Keyword : request.SkuSnapshot;
        if (!string.IsNullOrWhiteSpace(sku)) { clauses.Add("strpos(lower(h.sku_snapshot),lower(@Sku))>0"); args.Add("Sku", sku.Trim()); }
        if (request.TestDateFrom.HasValue) { clauses.Add("h.test_date>=@DateFrom"); args.Add("DateFrom", DateTime.SpecifyKind(request.TestDateFrom.Value.Date, DateTimeKind.Unspecified)); }
        if (request.TestDateTo.HasValue) { clauses.Add("h.test_date<=@DateTo"); args.Add("DateTo", DateTime.SpecifyKind(request.TestDateTo.Value.Date, DateTimeKind.Unspecified)); }
        return (string.Join(" AND ", clauses), args);
    }

    /// <summary>仓储不创建业务事务，维护接口缺少调用方事务时立即拒绝。</summary>
    private void RequireTransaction()
    {
        if (Transaction is null) throw new InvalidOperationException("流量维护必须使用调用方事务。");
    }
}
