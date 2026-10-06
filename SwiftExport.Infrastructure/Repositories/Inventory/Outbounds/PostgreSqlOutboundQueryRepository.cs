using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;

/// <summary>出库五方法查询仓储；普通读取在本文件，选品在独立partial一次物化，完整接口的运行DI另行验收。</summary>
public sealed partial class PostgreSqlOutboundQueryRepository(RepositoryBaseContext context)
{
    private static readonly EntityMetadata HeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(OutboundDocument));
    private static readonly EntityMetadata LineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(OutboundDocumentLine));
    private static readonly EntityMetadata ProductMap=PostgreSqlEntityMetadata.GetMetadata(typeof(Product));
    private static readonly EntityMetadata BalanceMap=PostgreSqlEntityMetadata.GetMetadata(typeof(InventoryBalance));

    /// <summary>一次多结果分别读取当前计数与页；原分页值和SQL技术失败保留，不新增生命周期或自动单过滤。</summary>
    public async Task<PagedResponseBase<OutboundManagerListItemDto>> SearchAsync(OutboundManagerSearchRequest request,CancellationToken token)
    {
        var p=new PostgreSqlParameters();
        p.Add("DocumentType",request.DocumentType.HasValue?(int)request.DocumentType.Value:null);
        p.Add("PostingStatus",request.PostingStatus.HasValue?(int)request.PostingStatus.Value:null);
        p.Add("Keyword",Like(request.Keyword));
        // 减1先在int中计算再提升long，与源表达式相同；不裁剪原页号或页大小。
        p.Add("Skip",(long)(request.PageIndex-1)*request.PageSize);
        p.Add("Take",request.PageSize);
        using var reader=await context.Session.Connection.QueryMultipleAsync(Command(SearchSql(),new QueryParameters(p),token));
        return new(){TotalCount=await reader.ReadSingleAsync<int>(),Items=(await reader.ReadAsync<OutboundManagerListItemDto>()).ToList(),PageIndex=request.PageIndex,PageSize=request.PageSize};
    }

    /// <summary>头只未删，行仍独立联接头身份并全缓冲后判缺头；当前产品不额外排删除，余额缺失回零。</summary>
    public async Task<OutboundManagerDetailDto?> GetDetailAsync(Guid uid,CancellationToken token)
    {
        var p=new PostgreSqlParameters();p.Add("Uid",uid);
        using var reader=await context.Session.Connection.QueryMultipleAsync(Command(DetailSql(),new QueryParameters(p),token));
        var header=await reader.ReadSingleOrDefaultAsync<OutboundHeaderDetailDto>();
        var lines=(await reader.ReadAsync<OutboundLineDetailDto>()).ToList();
        return header is null?null:new(){Header=header,Lines=lines};
    }

    /// <summary>读取未删产品的六个身份字段，不筛停用或余额；空集合先返回，原分支不因取消令牌而拒绝。</summary>
    public async Task<IReadOnlyList<OutboundItemCandidateDto>> GetProductsAsync(IReadOnlyList<Guid> ids,CancellationToken token)
    {
        if(ids.Count==0)return [];
        var p=new PostgreSqlParameters();p.AddIds("Ids",ids);
        return (await context.Session.Connection.QueryAsync<OutboundItemCandidateDto>(Command($"""
            SELECT {P("Uid")} AS "ProductId",{P("Sku")} AS "Sku",{P("LinkSku")} AS "LinkSku",
                {P("DescriptionZh")} AS "Description",{P("MainImage")} AS "ImagePath",{P("Unit")} AS "Unit"
            FROM {Table(ProductMap)} p WHERE {P("Uid")}=ANY(@Ids) AND {P("IsDeleted")}=FALSE;
            """,new QueryParameters(p),token))).ToList();
    }

    /// <summary>仅按产品身份、启用及未删判断新增引用资格，不要求单位、仓库或正库存。</summary>
    public Task<bool> IsProductActiveAsync(Guid productId,CancellationToken token)
    {
        var p=new PostgreSqlParameters();p.Add("ProductId",productId);
        return context.Session.Connection.ExecuteScalarAsync<bool>(Command($"""
            SELECT EXISTS(SELECT 1 FROM {Table(ProductMap)} p WHERE {P("Uid")}=@ProductId AND {P("IsActive")}=TRUE AND {P("IsDeleted")}=FALSE);
            """,new QueryParameters(p),token));
    }

    /// <summary>所有命令借用同会话可空事务及原令牌，不持有读写事务或执行锁。</summary>
    private CommandDefinition Command(string sql,object args,CancellationToken token)=>new(sql,args,context.Session.Transaction,cancellationToken:token);

    /// <summary>同过滤的计数与页各自观察当前事实；行COUNT/SUM独立子查询避免倍增。</summary>
    private static string SearchSql()
    {
        var filter=$"""
            FROM {Table(HeadMap)} h WHERE {H("IsDeleted")}=FALSE
                AND (@DocumentType IS NULL OR {H("DocumentType")}=@DocumentType)
                AND (@PostingStatus IS NULL OR {H("PostingStatus")}=@PostingStatus)
                AND (@Keyword IS NULL OR {H("DocumentNo")} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR {H("WarehouseNameSnapshot")} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR EXISTS(SELECT 1 FROM {Table(LineMap)} l WHERE {L("OutboundDocumentId")}={H("Uid")}
                        AND {L("IsDeleted")}=FALSE AND {L("SkuSnapshot")} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'))
            """;
        // PG LIMIT 0会成功，但源FETCH<=0拒绝；局部SQL负LIMIT保持技术失败，不转换业务校验。
        return $"""
            SELECT COUNT(*)::integer {filter};
            SELECT {H("Uid")} AS "Uid",{H("Version")} AS "Version",{H("DocumentNo")} AS "DocumentNo",
                {H("DocumentType")} AS "DocumentType",{H("LifecycleStatus")} AS "LifecycleStatus",{H("PostingStatus")} AS "PostingStatus",
                {H("WarehouseSiteId")} AS "WarehouseSiteId",{H("WarehouseNameSnapshot")} AS "WarehouseNameSnapshot",
                {H("ShipmentDate")} AS "ShipmentDate",{H("Remark")} AS "Remark",{H("IssueReason")} AS "IssueReason",
                (SELECT COUNT(*)::integer FROM {Table(LineMap)} l WHERE {L("OutboundDocumentId")}={H("Uid")} AND {L("IsDeleted")}=FALSE) AS "LineCount",
                COALESCE((SELECT SUM({L("Quantity")}) FROM {Table(LineMap)} l WHERE {L("OutboundDocumentId")}={H("Uid")} AND {L("IsDeleted")}=FALSE),0) AS "TotalQuantity"
            {filter}
            ORDER BY {H("ShipmentDate")} DESC NULLS LAST,{UuidOrder(H("Uid"))} DESC NULLS LAST
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip;
            """;
    }

    /// <summary>第二结果只复核行未删和头身份；头已删仍缓冲其有效行，不套其他详情的有效产品过滤。</summary>
    private static string DetailSql()=>$"""
        SELECT {H("Uid")} AS "Uid",
            {H("Version")} AS "Version",
            {H("DocumentNo")} AS "DocumentNo",
            {H("DocumentType")} AS "DocumentType",
            {H("LifecycleStatus")} AS "LifecycleStatus",
            {H("PostingStatus")} AS "PostingStatus",
            {H("WarehouseSiteId")} AS "WarehouseSiteId",
            {H("WarehouseCodeSnapshot")} AS "WarehouseCodeSnapshot",
            {H("WarehouseNameSnapshot")} AS "WarehouseNameSnapshot",
            {H("ShipmentDate")} AS "ShipmentDate",
            {H("Remark")} AS "Remark",
            {H("IssueReason")} AS "IssueReason",
            {H("PostedDate")} AS "PostedDate",
            {H("PostedBy")} AS "PostedBy",
            {H("ReversedDate")} AS "ReversedDate",
            {H("ReversedBy")} AS "ReversedBy",
            {H("ReverseReason")} AS "ReverseReason",
            {H("TriggerPackingListId")} AS "TriggerPackingListId",
            {H("CommandId")} AS "CommandId",
            {H("CreatedDate")} AS "CreatedDate",
            {H("CreatedBy")} AS "CreatedBy",
            {H("UpdatedDate")} AS "UpdatedDate",
            {H("UpdatedBy")} AS "UpdatedBy",
            {H("IsDeleted")} AS "IsDeleted"
        FROM {Table(HeadMap)} h WHERE {H("Uid")}=@Uid AND {H("IsDeleted")}=FALSE;
        SELECT {L("Uid")} AS "Uid",
            {L("Version")} AS "Version",
            {L("OutboundDocumentId")} AS "OutboundDocumentId",
            {L("LineNumber")} AS "LineNumber",
            {L("ProductId")} AS "ProductId",
            {L("SkuSnapshot")} AS "SkuSnapshot",
            {L("DescriptionSnapshot")} AS "DescriptionSnapshot",
            {L("UnitSnapshot")} AS "UnitSnapshot",
            {L("Quantity")} AS "Quantity",
            {L("CommandItemId")} AS "CommandItemId",
            {L("OriginalLineId")} AS "OriginalLineId",
            {L("CreatedDate")} AS "CreatedDate",
            {L("CreatedBy")} AS "CreatedBy",
            {L("UpdatedDate")} AS "UpdatedDate",
            {L("UpdatedBy")} AS "UpdatedBy",
            {P("LinkSku")} AS "LinkSku",{P("MainImage")} AS "ImagePath",COALESCE({C(BalanceMap,"b","OnHandQty")},0) AS "OnHandQty"
        FROM {Table(LineMap)} l JOIN {Table(HeadMap)} h ON {H("Uid")}={L("OutboundDocumentId")}
        LEFT JOIN {Table(ProductMap)} p ON {P("Uid")}={L("ProductId")}
        LEFT JOIN {Table(BalanceMap)} b ON {C(BalanceMap,"b","WarehouseSiteId")}={H("WarehouseSiteId")}
            AND {C(BalanceMap,"b","ProductId")}={L("ProductId")} AND {C(BalanceMap,"b","IsDeleted")}=FALSE
        WHERE {L("OutboundDocumentId")}=@Uid AND {L("IsDeleted")}=FALSE
        ORDER BY {L("LineNumber")} ASC NULLS FIRST,{UuidOrder(L("Uid"))} ASC NULLS FIRST;
        """;

    /// <summary>源.NET空白/Trim与字面LIKE转义原样保留，不扩展公共匹配能力。</summary>
    private static string? Like(string? value)=>string.IsNullOrWhiteSpace(value)?null:"%"+value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")+"%";
    /// <summary>固定实体映射提供被引用的schema/table。</summary>
    private static string Table(EntityMetadata map)=>PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);
    /// <summary>固定元数据与内部别名提供物理列，不接受外部标识符。</summary>
    private static string C(EntityMetadata map,string alias,string property)=>alias+"."+PostgreSqlNames.Quote(map.Columns.Single(x=>x.PropertyName==property).ColumnName);
    private static string H(string property)=>C(HeadMap,"h",property);
    private static string L(string property)=>C(LineMap,"l",property);
    private static string P(string property)=>C(ProductMap,"p",property);
    /// <summary>保留SQL Server uniqueidentifier的SqlGuid字节比较顺序。</summary>
    private static string UuidOrder(string column)=>"ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(x=>$"get_byte(uuid_send({column}),{x})"))+"]";

    /// <summary>本查询局部明确NULL标量和UUID数组类型；每命令独立克隆，不修改公共参数组件。</summary>
    private sealed class QueryParameters(PostgreSqlParameters parameters):SqlMapper.IDynamicParameters
    {
        /// <summary>装饰连接也按uuid[]单参数绑定，NULL enum/text不依赖驱动猜测。</summary>
        public void AddParameters(IDbCommand command,SqlMapper.Identity identity)
        {
            parameters.AddParameters(command,identity);
            foreach(NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType=parameter.ParameterName switch
                {
                    "Uid" or "ProductId"=>NpgsqlDbType.Uuid,
                    "Ids"=>NpgsqlDbType.Array|NpgsqlDbType.Uuid,
                    "Keyword"=>NpgsqlDbType.Text,
                    "Skip"=>NpgsqlDbType.Bigint,
                    _=>NpgsqlDbType.Integer
                };
        }
    }
}
