using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>借当前会话读取来源交付净额，锁定读取只锁参与的现有来源和Posted头行；空范围与完整装箱联合协议另验。</summary>
public sealed partial class PostgreSqlSourceDeliveryQueryRepository(RepositoryBaseContext context) : ISourceDeliveryQueryRepository
{
    private static readonly EntityMetadata GrossMap=PostgreSqlEntityMetadata.GetMetadata(typeof(GrossDemand));
    private static readonly EntityMetadata HeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PackingListHead));
    private static readonly EntityMetadata LineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PackingListLine));
    private static readonly EntityMetadata CustomerMap=PostgreSqlEntityMetadata.GetMetadata(typeof(Customer));
    private static readonly EntityMetadata ProductMap=PostgreSqlEntityMetadata.GetMetadata(typeof(Product));

    /// <summary>仅检查原三个用户实体表与箱单三快照列；含分区表、不接受视图，不部署或证明完整历史。</summary>
    public Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken=default)=>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT
              EXISTS(SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=@Schema AND t.relname=@GrossTable AND t.relkind IN ('r','p'))
              AND EXISTS(SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=@Schema AND t.relname=@HeadTable AND t.relkind IN ('r','p'))
              AND EXISTS(SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=@Schema AND t.relname=@LineTable AND t.relkind IN ('r','p'))
              AND (SELECT COUNT(*) FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class t ON t.oid=a.attrelid
                JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                WHERE n.nspname=@Schema AND t.relname=@LineTable AND t.relkind IN ('r','p') AND a.attnum>0 AND NOT a.attisdropped
                  AND a.attname IN (@ModeColumn,@TypeColumn,@NumberColumn))=3;
            """,new QueryParameters([
                ("Schema",NpgsqlDbType.Text,GrossMap.SchemaName),("GrossTable",NpgsqlDbType.Text,GrossMap.TableName),
                ("HeadTable",NpgsqlDbType.Text,HeadMap.TableName),("LineTable",NpgsqlDbType.Text,LineMap.TableName),
                ("ModeColumn",NpgsqlDbType.Text,Column(LineMap,"IsNonStandardSnapshot")),
                ("TypeColumn",NpgsqlDbType.Text,Column(LineMap,"SourceTypeSnapshot")),
                ("NumberColumn",NpgsqlDbType.Text,Column(LineMap,"SourceDocumentNoSnapshot"))]),cancellationToken));

    /// <summary>活动、有客户及正剩余来源只物化一次；计数/空页由SQL传输，不创建事务或预占交付量。</summary>
    public async Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchAsync(SourceDemandDeliverySearchRequest request,CancellationToken cancellationToken=default)
    {
        var parameters=new QueryParameters([
            ("SourceType",NpgsqlDbType.Text,Clean(request.SourceType)),("CustomerId",NpgsqlDbType.Uuid,request.CustomerId),
            ("SourceNo",NpgsqlDbType.Text,Like(request.SourceNo)),("Sku",NpgsqlDbType.Text,Like(request.Sku)),
            ("Keyword",NpgsqlDbType.Text,Like(request.Keyword)),("Skip",NpgsqlDbType.Bigint,(long)(request.PageIndex-1)*request.PageSize),
            ("Take",NpgsqlDbType.Integer,request.PageSize),("Posted",NpgsqlDbType.Integer,(int)PackingListPostingStatusEnum.Posted),
            ("Active",NpgsqlDbType.Integer,(int)GrossDemandLifecycleStatusEnum.Active)]);
        var rows=(await context.Session.Connection.QueryAsync<SearchRow>(Command(SearchSql(),parameters,cancellationToken))).ToList();
        var result=new PagedResponseBase<SourceDemandDeliveryItemDto>{TotalCount=rows.First().TotalCount,PageIndex=request.PageIndex,PageSize=request.PageSize};
        foreach(var row in rows)if(row.HasSource)result.Items.Add(ToItem(row));
        return result;
    }

    /// <summary>仅按Gross身份及共同产品模式资格读取；关闭/作废、客户缺失、零负剩余原样返回，缺失或软删为null。</summary>
    public Task<SourceDemandDeliveryItemDto?> GetAsync(Guid grossDemandId,CancellationToken cancellationToken=default)=>
        context.Session.Connection.QuerySingleOrDefaultAsync<SourceDemandDeliveryItemDto>(Command(
            Projection()+"SELECT * FROM source_delivery WHERE \"GrossDemandId\"=@GrossDemandId;",
            new QueryParameters([("GrossDemandId",NpgsqlDbType.Uuid,grossDemandId),
                ("Posted",NpgsqlDbType.Integer,(int)PackingListPostingStatusEnum.Posted),
                ("Active",NpgsqlDbType.Integer,(int)GrossDemandLifecycleStatusEnum.Active)]),cancellationToken));

    /// <summary>保留原事务门禁和输入过滤，单个命令锁实际来源/Posted头行并读取净额，不创建事务或伪造不存在的范围锁。</summary>
    public async Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetForUpdateAsync(IReadOnlyCollection<Guid> grossDemandIds,CancellationToken cancellationToken=default)
    {
        if(context.Session.Transaction is null)throw new InvalidOperationException("来源交付额度复核必须加入调用方业务事务。");
        var ids=grossDemandIds.Where(id=>id!=Guid.Empty).Distinct().OrderBy(id=>id).ToArray();
        if(ids.Length==0)return Array.Empty<SourceDemandDeliveryItemDto>();
        var parameters=new QueryParameters([("Ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid,ids),
            ("Posted",NpgsqlDbType.Integer,(int)PackingListPostingStatusEnum.Posted),
            ("Active",NpgsqlDbType.Integer,(int)GrossDemandLifecycleStatusEnum.Active)]);
        var order="ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(i=>$"get_byte(uuid_send(\"GrossDemandId\"),{i})"))+"]";
        return (await context.Session.Connection.QueryAsync<SourceDemandDeliveryItemDto>(Command(
            Projection(true)+$"SELECT * FROM source_delivery ORDER BY {order};",parameters,cancellationToken))).ToList();
    }

    /// <summary>保持原权威Posted合计、Gross快照及nullable客户；锁定聚合先物化实际行，避免对聚合结果或外连接可空侧加锁。</summary>
    internal static string Projection(bool forUpdate=false)=>DeliveryProjection(forUpdate)+$"""
        , source_delivery AS {(forUpdate?"MATERIALIZED":"")} (
            SELECT
                {G("Uid")} AS "GrossDemandId",
                {G("Version")} AS "DemandVersion",
                {G("SourceType")} AS "SourceType",
                {G("SourceUid")} AS "SourceUid",
                {G("SourceNo")} AS "SourceNo",
                {G("SourceLineId")} AS "SourceLineId",
                {G("SourceLineNumber")} AS "SourceLineNumber",
                {G("SourceVersion")} AS "SourceVersion",
                CASE WHEN {C(CustomerMap,"c","Uid")} IS NOT NULL THEN {G("CustomerId")} END AS "CustomerId",
                {C(CustomerMap,"c","CustomerCode")} AS "CustomerCode",
                {C(CustomerMap,"c","CustomerName")} AS "CustomerName",
                {G("ProductId")} AS "ProductId",
                {G("IsNonStandard")} AS "IsNonStandard",
                {G("SkuSnapshot")} AS "Sku",
                {G("LinkSkuSnapshot")} AS "LinkSku",
                {G("CustomerSkuSnapshot")} AS "CustomerSku",
                {G("DescriptionSnapshot")} AS "Description",
                {G("ColorSnapshot")} AS "Color",
                {G("SpecificationSnapshot")} AS "Specification",
                {G("ImagePathSnapshot")} AS "ImagePath",
                {G("UnitSnapshot")} AS "Unit",
                {G("DemandQty")} AS "DemandQty",
                COALESCE(d."DeliveredQty",0) AS "DeliveredQty",
                {G("DemandQty")}-COALESCE(d."DeliveredQty",0) AS "RemainingQty",
                {G("LifecycleStatus")} AS "LifecycleStatus",
                ({G("LifecycleStatus")}=@Active) AS "IsActive",
                {G("RequiredDate")} AS "RequiredDate"
            FROM {Table(GrossMap)} g LEFT JOIN {Table(CustomerMap)} c ON {C(CustomerMap,"c","Uid")}={G("CustomerId")} AND {C(CustomerMap,"c","IsDeleted")}=FALSE
            LEFT JOIN {Table(ProductMap)} p ON {C(ProductMap,"p","Uid")}={G("ProductId")} AND {C(ProductMap,"p","IsDeleted")}=FALSE
            LEFT JOIN delivered d ON d."GrossDemandId"={G("Uid")}
            WHERE {G("IsDeleted")}=FALSE AND (({G("IsNonStandard")}=TRUE AND {G("ProductId")} IS NULL)
                OR ({G("IsNonStandard")}=FALSE AND {G("ProductId")} IS NOT NULL AND {C(ProductMap,"p","Uid")} IS NOT NULL))
                {(forUpdate?$"AND {G("Uid")}=ANY(@Ids) FOR UPDATE OF g":"")}
        )
        """;

    /// <summary>普通读取保留原聚合；锁定读取只物化原范围内有效Posted头行，再对锁定事实求和。</summary>
    private static string DeliveryProjection(bool forUpdate)=>forUpdate?$"""
        WITH locked_delivery AS MATERIALIZED (
            SELECT {C(LineMap,"l","GrossDemandId")} AS "GrossDemandId",{C(LineMap,"l","Qty")} AS "Qty"
            FROM {Table(LineMap)} l JOIN {Table(HeadMap)} h ON {C(HeadMap,"h","Uid")}={C(LineMap,"l","PackingListHeadId")}
            WHERE {C(LineMap,"l","IsDeleted")}=FALSE AND {C(HeadMap,"h","IsDeleted")}=FALSE
                AND {C(HeadMap,"h","PostingStatus")}=@Posted AND {C(LineMap,"l","GrossDemandId")}=ANY(@Ids)
            FOR UPDATE OF l,h
        ), delivered AS (
            SELECT "GrossDemandId",SUM("Qty") AS "DeliveredQty" FROM locked_delivery GROUP BY "GrossDemandId"
        )
        """:$"""
        WITH delivered AS (
            SELECT {C(LineMap,"l","GrossDemandId")} AS "GrossDemandId",SUM({C(LineMap,"l","Qty")}) AS "DeliveredQty"
            FROM {Table(LineMap)} l JOIN {Table(HeadMap)} h ON {C(HeadMap,"h","Uid")}={C(LineMap,"l","PackingListHeadId")}
            WHERE {C(LineMap,"l","IsDeleted")}=FALSE AND {C(HeadMap,"h","IsDeleted")}=FALSE AND {C(HeadMap,"h","PostingStatus")}=@Posted
            GROUP BY {C(LineMap,"l","GrossDemandId")}
        )
        """;

    /// <summary>固定完整过滤候选供COUNT和分页共用；HasSource区分LEFT占位，非法FETCH保持技术拒绝。</summary>
    private static string SearchSql()=>Projection()+$"""
        , candidates AS MATERIALIZED (
            SELECT * FROM source_delivery s WHERE s."IsActive"=TRUE AND s."CustomerId" IS NOT NULL AND s."RemainingQty">0
              AND (@SourceType IS NULL OR rtrim(s."SourceType",' ') COLLATE public.erp_dictionary_ci=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci)
              AND (@CustomerId IS NULL OR s."CustomerId"=@CustomerId)
              AND (@SourceNo IS NULL OR s."SourceNo" COLLATE public.erp_dictionary_ci LIKE @SourceNo ESCAPE '~')
              AND (@Sku IS NULL OR s."Sku" COLLATE public.erp_dictionary_ci LIKE @Sku ESCAPE '~')
              AND (@Keyword IS NULL OR s."SourceNo" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR s."Sku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s."LinkSku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR s."CustomerSku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR s."Description" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                OR s."Color" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
        ), totals AS (SELECT COUNT(*)::integer AS "TotalCount" FROM candidates), page AS (
            SELECT s.*,TRUE AS "HasSource" FROM candidates s ORDER BY {Order("s")}
            LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip
        )
        SELECT t."TotalCount",COALESCE(pg."HasSource",FALSE) AS "HasSource",{string.Join(',',Fields.Select(n=>"pg.\""+n+"\""))}
        FROM totals t LEFT JOIN page pg ON TRUE ORDER BY {Order("pg")};
        """;

    /// <summary>页内与最终输出都沿来源字典尾空格/行号/SqlGuid顺序，不改原始文本。</summary>
    private static string Order(string alias)=>$"rtrim({alias}.\"SourceNo\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,rtrim({alias}.\"SourceType\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,COALESCE({alias}.\"SourceLineNumber\",2147483647) ASC,"+
        "ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(i=>$"get_byte(uuid_send({alias}.\"GrossDemandId\"),{i})"))+"] ASC NULLS FIRST";
    /// <summary>当前连接、可空父事务与原令牌贯穿一个命令，仓储不接管所有权。</summary>
    private CommandDefinition Command(string sql,object parameters,CancellationToken token)=>new(sql,parameters,context.Session.Transaction,cancellationToken:token);
    /// <summary>源输入空白规则仅用于查询参数，不处理投影文本。</summary>
    private static string? Clean(string? text)=>string.IsNullOrWhiteSpace(text)?null:text.Trim();
    /// <summary>原四次转义次序保持，防止用户通配符扩大匹配。</summary>
    private static string? Like(string? text)=>Clean(text) is {} value?"%"+value.Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")+"%":null;
    /// <summary>固定实体提供物理表，禁止外部标识符拼接。</summary>
    private static string Table(EntityMetadata map)=>PostgreSqlNames.Quote(map.SchemaName)+"."+PostgreSqlNames.Quote(map.TableName);
    private static string Column(EntityMetadata map,string property)=>map.Columns.Single(c=>c.PropertyName==property).ColumnName;
    private static string C(EntityMetadata map,string alias,string property)=>alias+"."+PostgreSqlNames.Quote(Column(map,property));
    private static string G(string property)=>C(GrossMap,"g",property);

    private static readonly string[] Fields=["GrossDemandId","DemandVersion","SourceType","SourceUid","SourceNo","SourceLineId","SourceLineNumber","SourceVersion","CustomerId","CustomerCode","CustomerName","ProductId","IsNonStandard","Sku","LinkSku","CustomerSku","Description","Color","Specification","ImagePath","Unit","DemandQty","DeliveredQty","RemainingQty","LifecycleStatus","IsActive","RequiredDate"];
    /// <summary>只组装真实SQL行，必需值为NULL暴露映射错误；不在客户端重算净额、资格或页序。</summary>
    private static SourceDemandDeliveryItemDto ToItem(SearchRow row)=>new()
    {
        GrossDemandId=row.GrossDemandId!.Value,
        DemandVersion=row.DemandVersion!.Value,
        SourceType=row.SourceType,
        SourceUid=row.SourceUid!.Value,
        SourceNo=row.SourceNo,
        SourceLineId=row.SourceLineId!.Value,
        SourceLineNumber=row.SourceLineNumber,
        SourceVersion=row.SourceVersion,
        CustomerId=row.CustomerId,
        CustomerCode=row.CustomerCode,
        CustomerName=row.CustomerName,
        ProductId=row.ProductId,
        IsNonStandard=row.IsNonStandard!.Value,
        Sku=row.Sku,
        LinkSku=row.LinkSku,
        CustomerSku=row.CustomerSku,
        Description=row.Description,
        Color=row.Color,
        Specification=row.Specification,
        ImagePath=row.ImagePath,
        Unit=row.Unit,
        DemandQty=row.DemandQty!.Value,
        DeliveredQty=row.DeliveredQty!.Value,
        RemainingQty=row.RemainingQty!.Value,
        LifecycleStatus=row.LifecycleStatus!.Value,
        IsActive=row.IsActive!.Value,
        RequiredDate=row.RequiredDate,
    };
    /// <summary>单结果包全部投影可空用于空页占位；HasSource是唯一占位判据，真实文本NULL保持。</summary>
    private sealed class SearchRow
    {
        public int TotalCount{get;set;}
        public bool HasSource{get;set;}
        public Guid? GrossDemandId{get;set;}
        public int? DemandVersion{get;set;}
        public string? SourceType{get;set;}
        public Guid? SourceUid{get;set;}
        public string? SourceNo{get;set;}
        public Guid? SourceLineId{get;set;}
        public int? SourceLineNumber{get;set;}
        public int? SourceVersion{get;set;}
        public Guid? CustomerId{get;set;}
        public string? CustomerCode{get;set;}
        public string? CustomerName{get;set;}
        public Guid? ProductId{get;set;}
        public bool? IsNonStandard{get;set;}
        public string? Sku{get;set;}
        public string? LinkSku{get;set;}
        public string? CustomerSku{get;set;}
        public string? Description{get;set;}
        public string? Color{get;set;}
        public string? Specification{get;set;}
        public string? ImagePath{get;set;}
        public string? Unit{get;set;}
        public decimal? DemandQty{get;set;}
        public decimal? DeliveredQty{get;set;}
        public decimal? RemainingQty{get;set;}
        public GrossDemandLifecycleStatusEnum? LifecycleStatus{get;set;}
        public bool? IsActive{get;set;}
        public DateTime? RequiredDate{get;set;}
    }
    /// <summary>本切片局部显式绑定标量NULL类型，每命令独立参数，不提前建设第四方法参数框架。</summary>
    private sealed class QueryParameters((string Name,NpgsqlDbType Type,object? Value)[] parameters):SqlMapper.IDynamicParameters
    {
        /// <summary>metadata目录及业务参数都参数化，NULL转DBNull但仍保持显式text/uuid类型。</summary>
        public void AddParameters(IDbCommand command,SqlMapper.Identity identity)
        {foreach(var p in parameters)command.Parameters.Add(new NpgsqlParameter(p.Name,p.Type){Value=p.Value??DBNull.Value});}
    }
}
