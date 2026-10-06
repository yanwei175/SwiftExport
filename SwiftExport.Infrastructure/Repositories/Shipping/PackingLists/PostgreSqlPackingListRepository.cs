using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>在同一请求会话提供装箱查询、草稿及来源退出三个接口；仅翻译原持久化和固定资格，不处理库存分配。</summary>
public sealed class PostgreSqlPackingListRepository(RepositoryBaseContext context) : IPackingListQueryRepository,IPackingListDocumentRepository,IPackingListSourceExitRepository
{
    private static readonly EntityMetadata HeadMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PackingListHead));
    private static readonly EntityMetadata LineMap=PostgreSqlEntityMetadata.GetMetadata(typeof(PackingListLine));
    private static readonly string[] HeadListFields=["Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","PackingListNo","CustomerId","CustomerNameSnapshot","InvoiceDate","ContainerNo","SealNo","ShippingLocationSnapshot","Etd","Eta","LifecycleStatus","PostingStatus"];

    /// <summary>保留原10秒组身份应用锁，查询所有其他头的占用（含历史）；PG事务快照及空组联合竞争另验。</summary>
    public async Task<bool> HasForeignPackingGroupsAsync(Guid headId,IReadOnlyCollection<Guid> groupIds,CancellationToken cancellationToken=default)
    {
        foreach(var id in groupIds.Distinct().Order())
        {
            if(context.Session.Transaction is null)throw new InvalidOperationException("拼箱组锁必须属于业务事务。");
            var key=BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("SwiftExport:PackingList.Group:"+id.ToString("N"))));
            var watch=Stopwatch.StartNew();var budget=TimeSpan.FromSeconds(10);var acquired=false;
            while(watch.Elapsed<budget)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(await context.Session.Connection.ExecuteScalarAsync<bool>(Command("SELECT pg_try_advisory_xact_lock(@Key)",new {Key=key},cancellationToken))){acquired=true;break;}
                var remaining=budget-watch.Elapsed;if(remaining<=TimeSpan.Zero)break;
                await Task.Delay(remaining<TimeSpan.FromMilliseconds(50)?remaining:TimeSpan.FromMilliseconds(50),cancellationToken);
            }
            if(!acquired)throw new DBConcurrencyException("拼箱标识正在被其他操作使用，请重试。");
        }
        if(groupIds.Count==0)return false;
        var parameters=Ids(groupIds);parameters.Add("HeadId",headId);
        return await context.Session.Connection.ExecuteScalarAsync<bool>(Command("SELECT EXISTS(SELECT 1 FROM public.packing_list_line WHERE packing_group_id=ANY(@Ids) AND packing_list_head_id<>@HeadId)",parameters,cancellationToken));
    }

    /// <summary>只读检查原三个表、必要列、Gross可空性和指定索引存在，不部署或额外收紧原索引资格。</summary>
    public Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken=default)=>context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
        SELECT to_regclass('public.packing_list_head') IS NOT NULL AND to_regclass('public.packing_list_line') IS NOT NULL
            AND to_regclass('public.outbound_document') IS NOT NULL
            AND (SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='packing_list_head'
                AND column_name IN ('create_command_id','shipping_location_snapshot'))=2
            AND (SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='packing_list_line'
                AND column_name IN ('gross_demand_id','packing_group_id','is_non_standard_snapshot','source_type_snapshot','source_document_no_snapshot'))=5
            AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='packing_list_line' AND column_name='gross_demand_id' AND is_nullable='YES')
            AND EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='outbound_document' AND column_name='trigger_packing_list_id')
            AND EXISTS(SELECT 1 FROM pg_class idx JOIN pg_index i ON i.indexrelid=idx.oid JOIN pg_class t ON t.oid=i.indrelid
                JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname='public' AND t.relname='packing_list_head' AND idx.relname=@Index);
        """,new {Index=PostgreSqlNames.FromClr("UX_PackingListHead_CreateCommand")},cancellationToken));

    /// <summary>锁实际头行，includeDeleted保持原范围；不存在身份返回空，不伪造间隙保护。</summary>
    public Task<PackingListHead?> LockHeaderAsync(Guid uid,bool includeDeleted,CancellationToken cancellationToken=default)=>
        context.Session.Connection.QuerySingleOrDefaultAsync<PackingListHead>(Command($"SELECT {Projection(HeadMap)} FROM public.packing_list_head WHERE uid=@Uid AND (@IncludeDeleted OR is_deleted=FALSE) FOR UPDATE",new {Uid=uid,IncludeDeleted=includeDeleted},cancellationToken));

    /// <summary>实际父头作为既有空行集合锚点，保留源有效行过滤和SqlGuid顺序，事务仍由调用方拥有。</summary>
    public async Task<IReadOnlyList<PackingListLine>> LockLinesAsync(Guid uid,CancellationToken cancellationToken=default)
    {
        await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command("SELECT uid FROM public.packing_list_head WHERE uid=@Uid FOR UPDATE",new {Uid=uid},cancellationToken));
        return(await context.Session.Connection.QueryAsync<PackingListLine>(Command($"SELECT {Projection(LineMap)} FROM public.packing_list_line WHERE packing_list_head_id=@Uid AND is_deleted=FALSE ORDER BY {UuidOrder("uid")} FOR UPDATE",new {Uid=uid},cancellationToken))).AsList();
    }

    /// <summary>按原请求行身份锁归属，不过滤软删、不新增父头资格；空输入原样短路。</summary>
    public async Task<IReadOnlyDictionary<Guid,Guid>> LockLineOwnersAsync(IReadOnlyCollection<Guid> lineIds,CancellationToken cancellationToken=default)
    {
        if(lineIds.Count==0)return new Dictionary<Guid,Guid>();
        var rows=await context.Session.Connection.QueryAsync<LineOwnerRow>(Command("SELECT uid AS \"Uid\",packing_list_head_id AS \"PackingListHeadId\" FROM public.packing_list_line WHERE uid=ANY(@Ids) FOR UPDATE",Ids(lineIds),cancellationToken));
        return rows.ToDictionary(row=>row.Uid,row=>row.PackingListHeadId);
    }

    /// <summary>保持头行固定写列、原版本回写与临时正行号策略；失败交给父事务回滚，不自动重建行身份。</summary>
    public async Task SaveAsync(PackingListHead header,IReadOnlyList<PackingListLine> activeLines,IReadOnlyList<PackingListLine> deletedLines,int expectedVersion,CancellationToken cancellationToken=default)
    {
        if(expectedVersion==0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command(InsertSql(HeadMap),PostgreSqlParameters.Entity(header,HeadMap),cancellationToken));header.Version=1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var count=await context.Session.Connection.ExecuteAsync(Command(UpdateSql(HeadMap,["CustomerId","CustomerNameSnapshot","CustomerAddressSnapshot","AttentionSnapshot","InvoiceDate","ContainerNo","SealNo","ShippingLocationSnapshot","Note","Etd","Eta"],"uid=@Uid AND version=@Version AND is_deleted=FALSE AND lifecycle_status=0 AND posting_status=0"),PostgreSqlParameters.Entity(header,HeadMap),cancellationToken));
            if(count!=1)throw new DBConcurrencyException("装箱单头版本或状态已变化。");header.Version++;
            // 与源一致，先将旧有效行移到未占用的正行号，不递增临时版本；最终写入或删除恢复行号。
            var current=(await context.Session.Connection.QueryAsync<PackingListLine>(Command($"SELECT uid AS \"Uid\",version AS \"Version\",line_number AS \"LineNumber\" FROM public.packing_list_line WHERE packing_list_head_id=@Uid AND is_deleted=FALSE ORDER BY {UuidOrder("uid")} FOR UPDATE",new {header.Uid},cancellationToken))).ToArray();
            var occupied=current.Select(x=>x.LineNumber).Concat(activeLines.Select(x=>x.LineNumber)).ToHashSet();var temporary=1;
            foreach(var row in current)
            {
                while(occupied.Contains(temporary))temporary=checked(temporary+1);occupied.Add(temporary);
                if(await context.Session.Connection.ExecuteAsync(Command("UPDATE public.packing_list_line SET line_number=@Temporary WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE",new {row.Uid,row.Version,Temporary=temporary},cancellationToken))!=1)throw new DBConcurrencyException("装箱单行排序版本已变化。");
            }
        }
        foreach(var line in activeLines)
        {
            var insert=line.Version==0;if(insert)context.AuditStamp.StampForInsert(line);else context.AuditStamp.StampForUpdate(line);
            var sql=insert?InsertSql(LineMap):UpdateSql(LineMap,["PackingGroupId","LineNumber","Qty","PcsPerPackage","ProductId","IsNonStandardSnapshot","SkuSnapshot","LinkSkuSnapshot","CustomerSkuSnapshot","DescriptionEnSnapshot","ColorNameEnSnapshot","UnitSnapshot","CustomerPoNo","PackageCount","NwPerPackage","GwPerPackage","CbmPerPackage","SubTotalNw","SubTotalGw","SubTotalCbm","InvoiceNo","Marks","Note"],"uid=@Uid AND packing_list_head_id=@PackingListHeadId AND version=@Version AND is_deleted=FALSE");
            if(await context.Session.Connection.ExecuteAsync(Command(sql,PostgreSqlParameters.Entity(line,LineMap),cancellationToken))!=1)throw new DBConcurrencyException($"装箱单行 {line.Uid} 版本已变化。");
            if(insert)line.Version=1;else line.Version++;
        }
        foreach(var line in deletedLines)
        {
            context.AuditStamp.StampForUpdate(line);
            if(await context.Session.Connection.ExecuteAsync(Command("UPDATE public.packing_list_line SET is_deleted=TRUE,line_number=@LineNumber,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy WHERE uid=@Uid AND packing_list_head_id=@PackingListHeadId AND version=@Version AND is_deleted=FALSE",PostgreSqlParameters.Entity(line,LineMap),cancellationToken))!=1)throw new DBConcurrencyException($"待删除装箱单行 {line.Uid} 版本已变化。");
        }
    }

    /// <summary>按原未过账活动草稿资格软删头行，成功仅递增头对象版本。</summary>
    public async Task DeleteAsync(PackingListHead header,CancellationToken cancellationToken=default)
    {
        context.AuditStamp.StampForUpdate(header);var parameters=PostgreSqlParameters.Entity(header,HeadMap);
        if(await context.Session.Connection.ExecuteAsync(Command("UPDATE public.packing_list_head SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE AND lifecycle_status=0 AND posting_status=0",parameters,cancellationToken))!=1)throw new DBConcurrencyException("装箱单版本或状态已变化。");
        await context.Session.Connection.ExecuteAsync(Command("UPDATE public.packing_list_line SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy WHERE packing_list_head_id=@Uid AND is_deleted=FALSE",parameters,cancellationToken));header.Version++;
    }

    /// <summary>保留原计数后列表的两个查询阶段、日期上界和发票摘要，不将旧分阶段行为改为全局快照。</summary>
    public async Task<PagedResponseBase<PackingListManagerListItemDto>> SearchAsync(PackingListManagerSearchRequest request,CancellationToken cancellationToken=default)
    {
        const string filter="""
            FROM public.packing_list_head h WHERE h.is_deleted=FALSE
                AND (@CustomerId IS NULL OR h.customer_id=@CustomerId)
                AND (@PostingStatus IS NULL OR h.posting_status=@PostingStatus)
                AND (@DateFrom IS NULL OR h.invoice_date>=@DateFrom)
                AND (@DateTo IS NULL OR h.invoice_date<@DateTo+INTERVAL '1 day')
                AND (@Keyword IS NULL OR h.packing_list_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.customer_name_snapshot COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.container_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.seal_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR EXISTS(SELECT 1 FROM public.packing_list_line kl WHERE kl.packing_list_head_id=h.uid AND kl.is_deleted=FALSE
                        AND kl.invoice_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'))
            """;
        var parameters=new QueryParameters();parameters.Add("CustomerId",request.CustomerId,NpgsqlDbType.Uuid);
        parameters.Add("PostingStatus",request.PostingStatus is null?null:(int)request.PostingStatus,NpgsqlDbType.Integer);
        parameters.Add("DateFrom",request.InvoiceDateFrom?.Date,NpgsqlDbType.Timestamp);parameters.Add("DateTo",request.InvoiceDateTo?.Date,NpgsqlDbType.Timestamp);
        parameters.Add("Keyword",Like(request.Keyword),NpgsqlDbType.Text);parameters.Add("Skip",(long)(request.PageIndex-1)*request.PageSize,NpgsqlDbType.Bigint);parameters.Add("Take",request.PageSize,NpgsqlDbType.Integer);
        var sql="SELECT count(*)::integer "+filter+";SELECT "+Projection(HeadMap,"h",HeadListFields)+","+InvoiceSummary()+" AS \"InvoiceNosSummary\" "+filter+$" ORDER BY h.invoice_date DESC NULLS LAST,{UuidOrder("h.uid")} DESC LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip;";
        using var reader=await context.Session.Connection.QueryMultipleAsync(Command(sql,parameters,cancellationToken));
        return new(){TotalCount=await reader.ReadSingleAsync<int>(),Items=(await reader.ReadAsync<PackingListManagerListItemDto>()).ToList(),PageIndex=request.PageIndex,PageSize=request.PageSize};
    }

    /// <summary>按源两阶段读取头和有效行，历史来源只补图片及单号，不因断链过滤箱单明细。</summary>
    public async Task<PackingListDetailDto?> GetDetailAsync(Guid uid,CancellationToken cancellationToken=default)
    {
        var sql=$"SELECT {Projection(HeadMap,"h")},{InvoiceSummary()} AS \"InvoiceNosSummary\" FROM public.packing_list_head h WHERE h.uid=@Uid AND h.is_deleted=FALSE;"+$"""
            SELECT {Projection(LineMap,"l")},g.image_path_snapshot AS "ImagePath",
                CASE WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO' THEN soh.invoice_no
                     WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR' THEN ash.after_sales_no END AS "SourceInvoiceNo"
            FROM public.packing_list_line l
            LEFT JOIN public.gross_demand g ON g.uid=l.gross_demand_id AND g.is_deleted=FALSE
            LEFT JOIN public.sales_order_header soh ON rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO'
                AND soh.uid=g.source_uid AND soh.customer_id=g.customer_id AND soh.is_deleted=FALSE
            LEFT JOIN public.after_sales_header ash ON rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR'
                AND ash.uid=g.source_uid AND ash.customer_id=g.customer_id AND ash.is_deleted=FALSE
            WHERE l.packing_list_head_id=@Uid AND l.is_deleted=FALSE ORDER BY l.line_number,{UuidOrder("l.uid")};
            """;
        using var reader=await context.Session.Connection.QueryMultipleAsync(Command(sql,new {Uid=uid},cancellationToken));
        var header=await reader.ReadSingleOrDefaultAsync<PackingListHeaderDto>();var lines=(await reader.ReadAsync<PackingListLineDto>()).ToList();
        return header is null?null:new PackingListDetailDto{Header=header,Lines=lines};
    }

    /// <summary>原包装唯一默认项和完整来源头行匹配才补充快照，保持标准/非标、售后投诉号和销售客户PO规则。</summary>
    public async Task<IReadOnlyList<PackingListSourceEnrichment>> GetSourceEnrichmentsAsync(IReadOnlyCollection<Guid> grossDemandIds,CancellationToken cancellationToken=default)
    {
        if(grossDemandIds.Count==0)return Array.Empty<PackingListSourceEnrichment>();
        return(await context.Session.Connection.QueryAsync<PackingListSourceEnrichment>(Command("""
            SELECT g.uid AS "GrossDemandId",CASE WHEN pkg.package_count=1 THEN pkg.pcs_per_package::numeric(18,4) END AS "PcsPerPackage",
                CASE WHEN pkg.package_count=1 THEN pkg.nw_per_package END AS "NwPerPackage",
                CASE WHEN pkg.package_count=1 THEN pkg.gw_per_package END AS "GwPerPackage",
                CASE WHEN pkg.package_count=1 THEN pkg.cbm_per_package END AS "CbmPerPackage",
                CASE WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO' AND soh.uid IS NOT NULL AND sol.uid IS NOT NULL THEN soh.customer_po_no
                     WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR' AND ash.uid IS NOT NULL AND asl.uid IS NOT NULL THEN ash.customer_complaint_no END AS "CustomerPoNo",
                CASE WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO' AND soh.uid IS NOT NULL AND sol.uid IS NOT NULL THEN soh.invoice_no
                     WHEN rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR' AND ash.uid IS NOT NULL AND asl.uid IS NOT NULL THEN ash.after_sales_no END AS "InvoiceNo"
            FROM public.gross_demand g
            LEFT JOIN public.sales_order_header soh ON rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO' AND soh.uid=g.source_uid AND soh.customer_id=g.customer_id AND soh.is_deleted=FALSE
            LEFT JOIN public.sales_order_line sol ON rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='SO' AND sol.uid=g.source_line_id AND sol.sales_order_id=soh.uid AND sol.product_id=g.product_id AND sol.is_non_standard=g.is_non_standard AND sol.is_deleted=FALSE
            LEFT JOIN public.after_sales_header ash ON rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR' AND ash.uid=g.source_uid AND ash.customer_id=g.customer_id AND ash.is_deleted=FALSE
            LEFT JOIN public.after_sales_line asl ON rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='CR' AND asl.uid=g.source_line_id AND asl.header_id=ash.uid AND asl.is_deleted=FALSE
            LEFT JOIN LATERAL(SELECT count(*) AS package_count,MAX(pp.pcs_per_package) AS pcs_per_package,
                MAX(pp.nw_kg) AS nw_per_package,MAX(pp.gw_kg) AS gw_per_package,MAX(pp.cbm) AS cbm_per_package
                FROM public.product_package pp WHERE g.is_non_standard=FALSE AND pp.product_id=g.product_id AND pp.is_deleted=FALSE AND pp.is_default=TRUE) pkg ON TRUE
            WHERE g.uid=ANY(@Ids) AND g.is_deleted=FALSE;
            """,Ids(grossDemandIds),cancellationToken))).AsList();
    }

    /// <summary>复用统一交付投影，原多关键词OR/发票精确集合在物化计数前筛选，不新增采购贡献资格。</summary>
    public async Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchSourcesAsync(PackingListSourceSearchRequest request,CancellationToken cancellationToken=default)
    {
        var words=(request.Keywords??[]).Append(request.Keyword).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(words.Length>201||words.Any(x=>x.Length>200))throw new ArgumentException("搜索关键词超出数量或长度限制。",nameof(request));
        var parameters=new QueryParameters();parameters.Add("CustomerId",request.CustomerId,NpgsqlDbType.Uuid);parameters.Add("Posted",(int)PackingListPostingStatusEnum.Posted,NpgsqlDbType.Integer);parameters.Add("Active",(int)GrossDemandLifecycleStatusEnum.Active,NpgsqlDbType.Integer);
        parameters.Add("ProductId",request.ProductId,NpgsqlDbType.Uuid);parameters.Add("IsNonStandard",request.IsNonStandard,NpgsqlDbType.Boolean);
        parameters.Add("Skip",(request.PageIndex-1)*request.PageSize,NpgsqlDbType.Integer);parameters.Add("Take",request.PageSize,NpgsqlDbType.Integer);
        parameters.Add("InvoiceNumbers",(request.InvoiceNumbers??[]).Select(x=>x.Trim().ToUpperInvariant()).ToArray(),NpgsqlDbType.Array|NpgsqlDbType.Text);
        var matches=new List<string>();
        for(var i=0;i<words.Length;i++)
        {
            var name="Keyword"+i;parameters.Add(name,Like(words[i]),NpgsqlDbType.Text);
            matches.Add("("+string.Join(" OR ",new[]{"SourceNo","Sku","LinkSku","CustomerSku","Description","Color"}.Select(field=>$"s.\"{field}\" COLLATE public.erp_dictionary_ci LIKE @{name} ESCAPE '~'"))+")");
        }
        var keywordFilter=matches.Count==0?"":" AND ("+string.Join(" OR ",matches)+")";
        var invoiceFilter=request.InvoiceNumbers is null||request.InvoiceNumbers.Count==0?"":" AND upper(btrim(CASE WHEN rtrim(s.\"SourceType\",' ') COLLATE public.erp_dictionary_ci='SO' THEN soh.invoice_no WHEN rtrim(s.\"SourceType\",' ') COLLATE public.erp_dictionary_ci='CR' THEN ash.after_sales_no END,' ')) COLLATE public.erp_dictionary_ci=ANY(SELECT rtrim(invoice,' ') FROM unnest(@InvoiceNumbers) input(invoice))";
        var order=$"rtrim(s.\"SourceNo\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,rtrim(s.\"SourceType\",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,COALESCE(s.\"SourceLineNumber\",2147483647),{UuidOrder("s.\"GrossDemandId\"")}";
        var sql=PostgreSqlSourceDeliveryQueryRepository.Projection()+$"""
            , candidates AS MATERIALIZED (
                SELECT s.* FROM source_delivery s
                LEFT JOIN public.sales_order_header soh ON rtrim(s."SourceType",' ') COLLATE public.erp_dictionary_ci='SO' AND soh.uid=s."SourceUid" AND soh.customer_id=s."CustomerId" AND soh.is_deleted=FALSE
                LEFT JOIN public.after_sales_header ash ON rtrim(s."SourceType",' ') COLLATE public.erp_dictionary_ci='CR' AND ash.uid=s."SourceUid" AND ash.customer_id=s."CustomerId" AND ash.is_deleted=FALSE
                WHERE s."IsActive"=TRUE AND s."CustomerId"=@CustomerId AND s."RemainingQty">0
                    AND (@ProductId IS NULL OR s."ProductId"=@ProductId) AND (@IsNonStandard IS NULL OR s."IsNonStandard"=@IsNonStandard){invoiceFilter}{keywordFilter}
            ), totals AS(SELECT count(*)::integer AS "TotalCount" FROM candidates), page AS(
                SELECT s.*,TRUE AS "HasItem" FROM candidates s ORDER BY {order} LIMIT CASE WHEN @Take<=0 THEN -1 ELSE @Take END OFFSET @Skip
            ) SELECT t."TotalCount",COALESCE(s."HasItem",FALSE) AS "HasItem",s.* FROM totals t LEFT JOIN page s ON TRUE ORDER BY {order};
            """;
        var rows=(await context.Session.Connection.QueryAsync<PageSummary,SourceDemandDeliveryItemDto,SourcePage>(Command(sql,parameters,cancellationToken),(summary,item)=>new(summary,item),splitOn:"GrossDemandId")).ToArray();
        return new(){TotalCount=rows.First().Summary.TotalCount,Items=rows.Where(x=>x.Summary.HasItem).Select(x=>x.Item).ToList(),PageIndex=request.PageIndex,PageSize=request.PageSize};
    }

    /// <summary>原来源退出单次聚合保留孤立行、坏历史及出货事实，锁实际可空头放入LATERAL，避免非法外连接锁。</summary>
    public Task<SourceDeliveryExitAssessment> AssessSourceExitForUpdateAsync(IReadOnlyCollection<Guid> grossDemandIds,CancellationToken cancellationToken=default)
    {
        var ids=grossDemandIds.Where(id=>id!=Guid.Empty).Distinct().OrderBy(id=>id).ToArray();
        if(ids.Length==0)return Task.FromResult(new SourceDeliveryExitAssessment());
        return context.Session.Connection.QuerySingleAsync<SourceDeliveryExitAssessment>(Command("""
            WITH related AS MATERIALIZED (
                SELECT l.uid AS line_uid,l.is_deleted AS line_is_deleted,l.packing_list_head_id,h.uid AS head_uid,h.is_deleted AS head_is_deleted,h.lifecycle_status,h.posting_status,h.reversed_date
                FROM public.packing_list_line l LEFT JOIN LATERAL(
                    SELECT source.uid,source.is_deleted,source.lifecycle_status,source.posting_status,source.reversed_date
                    FROM public.packing_list_head source WHERE source.uid=l.packing_list_head_id FOR UPDATE
                ) h ON TRUE WHERE l.gross_demand_id=ANY(@Ids) FOR UPDATE OF l
            ), related_heads AS(SELECT DISTINCT head_uid FROM related WHERE head_uid IS NOT NULL), outbound AS MATERIALIZED(
                SELECT o.posting_status FROM public.outbound_document o JOIN related_heads rh ON rh.head_uid=o.trigger_packing_list_id
                WHERE o.is_deleted=FALSE AND o.document_type=20 FOR UPDATE OF o
            ) SELECT
                (SELECT count(DISTINCT packing_list_head_id)::integer FROM related WHERE line_is_deleted=FALSE AND head_is_deleted=FALSE AND lifecycle_status=0 AND posting_status=0) AS "ActiveDraftCount",
                (SELECT count(DISTINCT packing_list_head_id)::integer FROM related WHERE line_is_deleted=FALSE AND head_is_deleted=FALSE AND posting_status=1) AS "PostedPackingListCount",
                (SELECT count(*)::integer FROM outbound WHERE posting_status=1) AS "PostedCustomerOutboundCount",
                ((SELECT count(*)::integer FROM related WHERE line_is_deleted=FALSE AND (head_uid IS NULL OR head_is_deleted=TRUE OR posting_status NOT IN(0,1,8) OR lifecycle_status NOT IN(0,8,9) OR (posting_status=0 AND lifecycle_status=8) OR (posting_status=8 AND reversed_date IS NULL)))
                    +(SELECT count(*)::integer FROM outbound WHERE posting_status NOT IN(1,8))) AS "InvalidHistoryCount";
            """,Ids(ids),cancellationToken));
    }

    /// <summary>发票按原二进制分组去重，保持首行及最小SqlGuid排序；只修剪ASCII空格，空集合返回空字符串。</summary>
    private static string InvoiceSummary()=>$"""
        COALESCE((SELECT string_agg(x.invoice_no,', ' ORDER BY x.first_line,{UuidOrder("x.first_uid")}) FROM (
            SELECT btrim(il.invoice_no,' ') COLLATE "C" AS invoice_no,MIN(il.line_number) AS first_line,(array_agg(il.uid ORDER BY {UuidOrder("il.uid")}))[1] AS first_uid
            FROM public.packing_list_line il WHERE il.packing_list_head_id=h.uid AND il.is_deleted=FALSE AND NULLIF(btrim(il.invoice_no,' '),'') IS NOT NULL
            GROUP BY btrim(il.invoice_no,' ') COLLATE "C"
        ) x),'')
        """;

    /// <summary>物化空页的统计与占位判据，不构造零身份的伪来源。</summary>
    private sealed class PageSummary{public int TotalCount{get;set;}public bool HasItem{get;set;}}
    /// <summary>仅组合数据库统计和真实来源投影，不重算业务字段。</summary>
    private sealed record SourcePage(PageSummary Summary,SourceDemandDeliveryItemDto Item);
    /// <summary>本仓储可空筛选、数组和墙上日期显式绑定PG类型，每命令克隆参数避免跨命令所有权冲突。</summary>
    private sealed class QueryParameters:SqlMapper.IDynamicParameters
    {
        private readonly List<NpgsqlParameter> parameters=[];
        /// <summary>保留NULL及原墙上时间，不按DateTime.Kind意外切换列类型。</summary>
        internal void Add(string name,object? value,NpgsqlDbType type)
        {if(type==NpgsqlDbType.Timestamp&&value is DateTime date)value=DateTime.SpecifyKind(date,DateTimeKind.Unspecified);parameters.Add(new NpgsqlParameter(name,type){Value=value??DBNull.Value});}
        /// <summary>向当前真实命令加入独立参数实例，不修改连接或事务设置。</summary>
        public void AddParameters(IDbCommand command,SqlMapper.Identity identity){foreach(var parameter in parameters)command.Parameters.Add(parameter.Clone());}
    }

    /// <summary>所有命令透传原会话和令牌，不接管事务。</summary>
    private CommandDefinition Command(string sql,object? parameters,CancellationToken token)=>new(sql,parameters,context.Session.Transaction,cancellationToken:token);
    /// <summary>投影显式保持CLR别名；限定列表时保持源列表字段。</summary>
    private static string Projection(EntityMetadata map,string alias="",IEnumerable<string>? fields=null)=>string.Join(',',(fields??map.Columns.Select(c=>c.PropertyName)).Select(name=>(alias.Length==0?"":alias+".")+PostgreSqlNames.Quote(map.GetProperty(name).ColumnName)+" AS "+PostgreSqlNames.Quote(name)));
    /// <summary>新增常量Version/IsDeleted与源一致，不从输入对象取值。</summary>
    private static string InsertSql(EntityMetadata map)=>$"INSERT INTO public.{PostgreSqlNames.Quote(map.TableName)} ({string.Join(',',map.Columns.Select(c=>PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',',map.Columns.Select(c=>c.PropertyName=="Version"?"1":c.PropertyName=="IsDeleted"?"FALSE":"@"+c.ParameterName))});";
    /// <summary>仅固定业务列与审计字段参与更新，版本谓词由原方法固定提供。</summary>
    private static string UpdateSql(EntityMetadata map,string[] fields,string where)=>$"UPDATE public.{PostgreSqlNames.Quote(map.TableName)} SET {string.Join(',',fields.Concat(["UpdatedDate","UpdatedBy"]).Select(name=>PostgreSqlNames.Quote(map.GetProperty(name).ColumnName)+"=@"+name))},version=version+1 WHERE {where};";
    /// <summary>数组固定uuid[]，保留空集合类型。</summary>
    private static PostgreSqlParameters Ids(IEnumerable<Guid> values){var parameters=new PostgreSqlParameters();parameters.AddIds("Ids",values.ToArray());return parameters;}
    /// <summary>保持源uniqueidentifier排序优先级。</summary>
    private static string UuidOrder(string column)=>"ARRAY["+string.Join(',',new[]{10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0}.Select(i=>$"get_byte(uuid_send({column}),{i})"))+"]";
    /// <summary>来源和搜索文本使用原转义次序及输入Trim，不改变投影文字。</summary>
    private static string? Like(string? value)=>string.IsNullOrWhiteSpace(value)?null:"%"+value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[")+"%";
    /// <summary>锁定行归属的最小投影，避免ValueTuple构造映射。</summary>
    private sealed class LineOwnerRow{public Guid Uid{get;set;}public Guid PackingListHeadId{get;set;}}
}
