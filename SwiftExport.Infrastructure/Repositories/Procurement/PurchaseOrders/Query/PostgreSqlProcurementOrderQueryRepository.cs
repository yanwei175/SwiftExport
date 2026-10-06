using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>采购订单基础PG查询；分别观察count/真实页及头汇总/当前行，不接DI或其他采购事实。</summary>
public sealed class PostgreSqlProcurementOrderQueryRepository(RepositoryBaseContext context) : IProcurementOrderQueryRepository
{
    private static readonly EntityMetadata HeadMap = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata LineMap = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderLine));
    private static readonly EntityMetadata ProductMap = PostgreSqlEntityMetadata.GetMetadata(typeof(Product));

    /// <summary>按源参数先取得独立总数再读取当前商业页；分页由调用服务校验，仓储不新增默认值或事务。</summary>
    public async Task<PagedResponseBase<ProcurementOrderListItemDto>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("CustomerId", request.CustomerId);
        parameters.Add("SupplierId", request.SupplierId);
        parameters.Add("ConfirmationStatus", request.ConfirmationStatus.HasValue ? (int)request.ConfirmationStatus.Value : null);
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null
            : "%" + request.Keyword.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        parameters.Add("Keyword", keyword);
        parameters.Add("Skip", (request.PageIndex - 1) * request.PageSize);
        parameters.Add("Take", request.PageSize);
        var filter = $"""
            FROM {Table(HeadMap)} h WHERE {H(nameof(PurchaseOrderHead.IsDeleted))}=FALSE
            AND (@CustomerId IS NULL OR {H(nameof(PurchaseOrderHead.CustomerId))}=@CustomerId)
            AND (@SupplierId IS NULL OR {H(nameof(PurchaseOrderHead.SupplierId))}=@SupplierId)
            AND (@ConfirmationStatus IS NULL OR {H(nameof(PurchaseOrderHead.ConfirmationStatus))}=@ConfirmationStatus)
            AND (@Keyword IS NULL OR {H(nameof(PurchaseOrderHead.OrderNo))} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
            """;
        var count = await context.Session.Connection.ExecuteScalarAsync<int>(Command("SELECT COUNT(*)::integer " + filter, parameters, ct));
        var items = (await context.Session.Connection.QueryAsync<ProcurementOrderListItemDto>(Command($"""
            SELECT {H(nameof(PurchaseOrderHead.Uid))} AS "PurchaseOrderId",
                {H(nameof(PurchaseOrderHead.OrderNo))} AS "OrderNo",
                {H(nameof(PurchaseOrderHead.CustomerId))} AS "CustomerId",
                {H(nameof(PurchaseOrderHead.CustomerCode))} AS "CustomerCode",
                {H(nameof(PurchaseOrderHead.CustomerNameSnapshot))} AS "CustomerName",
                {H(nameof(PurchaseOrderHead.SupplierId))} AS "SupplierId",
                {H(nameof(PurchaseOrderHead.SupplierCode))} AS "SupplierCode",
                {H(nameof(PurchaseOrderHead.SupplierName))} AS "SupplierName",
                {H(nameof(PurchaseOrderHead.OrderDate))} AS "OrderDate",
                {H(nameof(PurchaseOrderHead.ConfirmationStatus))} AS "ConfirmationStatus",
                {H(nameof(PurchaseOrderHead.LifecycleStatus))} AS "LifecycleStatus",
                {H(nameof(PurchaseOrderHead.Version))} AS "Version"
            {filter}
            ORDER BY {H(nameof(PurchaseOrderHead.OrderDate))} DESC NULLS LAST,
                     {H(nameof(PurchaseOrderHead.OrderNo))} COLLATE public.erp_dictionary_ci DESC NULLS LAST,
                     {SourceUuidOrder(H(nameof(PurchaseOrderHead.Uid)))} DESC
            OFFSET @Skip LIMIT @Take
            """, parameters, ct))).ToList();
        return new() { TotalCount = count, Items = items, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>先读取头及当阶段总量，再完整缓冲当前有效行；空头也完成行阶段，商业NULL与源按钮优先级保持。</summary>
    public async Task<PurchaseOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        parameters.Add("Uid", uid);
        parameters.Add("Pending", (int)ConfirmationStatusEnum.待确认);
        parameters.Add("Confirmed", (int)ConfirmationStatusEnum.已确认);
        parameters.Add("Active", (int)PurchaseOrderLifecycleStatusEnum.Active);
        var head = await context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderDetailDto>(Command($"""
            SELECT {H(nameof(PurchaseOrderHead.Uid))} AS "PurchaseOrderId",
                {H(nameof(PurchaseOrderHead.OrderNo))} AS "OrderNo",
                {H(nameof(PurchaseOrderHead.CustomerId))} AS "CustomerId",
                {H(nameof(PurchaseOrderHead.CustomerCode))} AS "CustomerCode",
                {H(nameof(PurchaseOrderHead.CustomerNameSnapshot))} AS "CustomerName",
                {H(nameof(PurchaseOrderHead.SupplierId))} AS "SupplierId",
                {H(nameof(PurchaseOrderHead.SupplierCode))} AS "SupplierCode",
                {H(nameof(PurchaseOrderHead.SupplierName))} AS "SupplierName",
                {H(nameof(PurchaseOrderHead.Currency))} AS "Currency",
                {H(nameof(PurchaseOrderHead.DiscountRate))} AS "DiscountRate",
                {H(nameof(PurchaseOrderHead.DiscountAmount))} AS "DiscountAmount",
                {H(nameof(PurchaseOrderHead.BoNo))} AS "BoNo",
                {H(nameof(PurchaseOrderHead.CustomerPoNo))} AS "CustomerPoNo",
                {H(nameof(PurchaseOrderHead.DemandSourceType))} AS "DemandSourceType",
                {H(nameof(PurchaseOrderHead.DemandSourceUid))} AS "DemandSourceUid",
                {H(nameof(PurchaseOrderHead.DemandSourceNo))} AS "DemandSourceNo",
                {H(nameof(PurchaseOrderHead.SoPerson))} AS "SalesPerson",
                {H(nameof(PurchaseOrderHead.PoPerson))} AS "PurchasePerson",
                {H(nameof(PurchaseOrderHead.InletrHoseType))} AS "InletHoseType",
                {H(nameof(PurchaseOrderHead.AeratorType))} AS "AeratorType",
                {H(nameof(PurchaseOrderHead.PalletType))} AS "PalletType",
                {H(nameof(PurchaseOrderHead.BoxType))} AS "BoxType",
                {H(nameof(PurchaseOrderHead.LabelType))} AS "LabelType",
                {H(nameof(PurchaseOrderHead.IMType))} AS "InstructionManualType",
                {H(nameof(PurchaseOrderHead.CartonType))} AS "CartonType",
                {H(nameof(PurchaseOrderHead.LogoType))} AS "LogoType",
                {H(nameof(PurchaseOrderHead.ReviewedBy))} AS "ReviewedBy",
                {H(nameof(PurchaseOrderHead.PoFolder))} AS "PurchaseOrderFolder",
                {H(nameof(PurchaseOrderHead.ZheKouBeiZhu))} AS "DiscountRemark",
                {H(nameof(PurchaseOrderHead.OrderDate))} AS "OrderDate",
                {H(nameof(PurchaseOrderHead.ExpectedDate))} AS "ExpectedDate",
                {H(nameof(PurchaseOrderHead.ReplyDate))} AS "ReplyDate",
                {H(nameof(PurchaseOrderHead.ConfirmedDate))} AS "ConfirmedDate",
                {H(nameof(PurchaseOrderHead.ConfirmedBy))} AS "ConfirmedBy",
                {H(nameof(PurchaseOrderHead.Remark))} AS "Remark",
                {H(nameof(PurchaseOrderHead.PackagingRequirements))} AS "PackagingRequirements",
                {H(nameof(PurchaseOrderHead.Version))} AS "Version",
                {H(nameof(PurchaseOrderHead.ConfirmationStatus))} AS "ConfirmationStatus",
                {H(nameof(PurchaseOrderHead.LifecycleStatus))} AS "LifecycleStatus",
                {H(nameof(PurchaseOrderHead.ActiveConfirmationId))} AS "ActiveConfirmationId",
                {H(nameof(PurchaseOrderHead.CreatedDate))} AS "CreatedDate",
                {H(nameof(PurchaseOrderHead.CreatedBy))} AS "CreatedBy",
                {H(nameof(PurchaseOrderHead.UpdatedDate))} AS "UpdatedDate",
                {H(nameof(PurchaseOrderHead.UpdatedBy))} AS "UpdatedBy",
                CASE WHEN {H(nameof(PurchaseOrderHead.ConfirmationStatus))}=@Pending AND {H(nameof(PurchaseOrderHead.LifecycleStatus))}=@Active THEN TRUE ELSE FALSE END AS "CanEditDraft",
                CASE WHEN {H(nameof(PurchaseOrderHead.ConfirmationStatus))}=@Pending AND {H(nameof(PurchaseOrderHead.LifecycleStatus))}=@Active THEN TRUE ELSE FALSE END AS "CanConfirm",
                CASE WHEN {H(nameof(PurchaseOrderHead.ConfirmationStatus))}=@Confirmed AND {H(nameof(PurchaseOrderHead.LifecycleStatus))}=@Active THEN TRUE ELSE FALSE END AS "CanCancelConfirmation",
                CASE WHEN {H(nameof(PurchaseOrderHead.LifecycleStatus))}<>@Active THEN '采购单不是活动状态。'
                     WHEN {H(nameof(PurchaseOrderHead.ConfirmationStatus))} NOT IN (@Pending,@Confirmed) THEN '采购确认状态不支持当前命令。'
                     ELSE '' END AS "CommandBlockedReason",
                totals.line_count AS "LineCount",totals.total_quantity AS "TotalQuantity",totals.cny_total_amount AS "CnyTotalAmount"
            FROM {Table(HeadMap)} h
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::integer AS line_count,COALESCE(SUM({L(nameof(PurchaseOrderLine.Quantity))}),0) AS total_quantity,
                       COALESCE(SUM({L(nameof(PurchaseOrderLine.UnitAmount))}),0) AS cny_total_amount
                FROM {Table(LineMap)} l WHERE {L(nameof(PurchaseOrderLine.PurchaseOrderId))}={H(nameof(PurchaseOrderHead.Uid))}
                    AND {L(nameof(PurchaseOrderLine.IsDeleted))}=FALSE
            ) totals ON TRUE
            WHERE {H(nameof(PurchaseOrderHead.Uid))}=@Uid AND {H(nameof(PurchaseOrderHead.IsDeleted))}=FALSE
            """, parameters, ct));
        // 源在空头时仍消费完整行结果；不可提前返回或刷新已经读出的头汇总。
        var lines = (await context.Session.Connection.QueryAsync<PurchaseOrderLineDetailDto>(Command($"""
            SELECT {L(nameof(PurchaseOrderLine.Uid))} AS "Uid",
                {L(nameof(PurchaseOrderLine.LineNumber))} AS "LineNumber",
                {L(nameof(PurchaseOrderLine.Version))} AS "Version",
                {L(nameof(PurchaseOrderLine.ProductId))} AS "ProductId",
                {L(nameof(PurchaseOrderLine.Sku))} AS "Sku",
                {L(nameof(PurchaseOrderLine.UnitSnapshot))} AS "UnitSnapshot",
                {L(nameof(PurchaseOrderLine.CustomerSku))} AS "CustomerSku",
                {L(nameof(PurchaseOrderLine.LinkSku))} AS "LinkSku",
                {L(nameof(PurchaseOrderLine.FinishZh))} AS "FinishZh",
                {L(nameof(PurchaseOrderLine.FeaturesZh))} AS "FeaturesZh",
                {L(nameof(PurchaseOrderLine.MaterialType))} AS "MaterialType",
                {L(nameof(PurchaseOrderLine.SupplyType))} AS "SupplyType",
                {L(nameof(PurchaseOrderLine.Quantity))} AS "Quantity",
                {L(nameof(PurchaseOrderLine.UnlinkedQty))} AS "UnlinkedQty",
                {L(nameof(PurchaseOrderLine.SourceType))} AS "SourceType",
                {L(nameof(PurchaseOrderLine.SourceNo))} AS "SourceNo",
                {L(nameof(PurchaseOrderLine.SourceUid))} AS "SourceUid",
                {L(nameof(PurchaseOrderLine.SourceLineId))} AS "SourceLineId",
                {L(nameof(PurchaseOrderLine.SourceLineNumber))} AS "SourceLineNumber",
                {L(nameof(PurchaseOrderLine.UnitPrice))} AS "UnitPrice",
                {L(nameof(PurchaseOrderLine.TaxPrice))} AS "TaxPrice",
                {L(nameof(PurchaseOrderLine.UnitAmount))} AS "UnitAmount",
                {L(nameof(PurchaseOrderLine.TaxAmount))} AS "TaxAmount",
                {L(nameof(PurchaseOrderLine.PlannedDeliveryDate))} AS "PlannedDeliveryDate",
                {L(nameof(PurchaseOrderLine.SupplierSku))} AS "SupplierSku",
                {L(nameof(PurchaseOrderLine.DescriptionZh))} AS "DescriptionZh",
                {L(nameof(PurchaseOrderLine.SubNote))} AS "SubNote",
                {L(nameof(PurchaseOrderLine.CreatedDate))} AS "CreatedDate",
                {L(nameof(PurchaseOrderLine.CreatedBy))} AS "CreatedBy",
                {L(nameof(PurchaseOrderLine.UpdatedDate))} AS "UpdatedDate",
                {L(nameof(PurchaseOrderLine.UpdatedBy))} AS "UpdatedBy",
                {P(nameof(Product.MainImage))} AS "MainImage",
                ({L(nameof(PurchaseOrderLine.ProductId))} IS NULL) AS "IsNonStandard",
                CASE WHEN rtrim({L(nameof(PurchaseOrderLine.MaterialType))},' ') COLLATE public.erp_dictionary_ci='客供件'
                     THEN {L(nameof(PurchaseOrderLine.DeliveryTargetSupplierId))} END AS "DeliverySupplierId",
                CASE WHEN rtrim({L(nameof(PurchaseOrderLine.MaterialType))},' ') COLLATE public.erp_dictionary_ci='客供件'
                     THEN {L(nameof(PurchaseOrderLine.DeliverySupplierCodeSnapshot))} ELSE '' END AS "DeliverySupplierCode",
                CASE WHEN rtrim({L(nameof(PurchaseOrderLine.MaterialType))},' ') COLLATE public.erp_dictionary_ci='客供件'
                     THEN {L(nameof(PurchaseOrderLine.DeliverySupplierNameSnapshot))} ELSE '' END AS "DeliverySupplierName"
            FROM {Table(LineMap)} l JOIN {Table(HeadMap)} h ON {H(nameof(PurchaseOrderHead.Uid))}={L(nameof(PurchaseOrderLine.PurchaseOrderId))}
            LEFT JOIN {Table(ProductMap)} p ON {P(nameof(Product.Uid))}={L(nameof(PurchaseOrderLine.ProductId))} AND {P(nameof(Product.IsDeleted))}=FALSE
            WHERE {H(nameof(PurchaseOrderHead.Uid))}=@Uid AND {H(nameof(PurchaseOrderHead.IsDeleted))}=FALSE AND {L(nameof(PurchaseOrderLine.IsDeleted))}=FALSE
            ORDER BY {L(nameof(PurchaseOrderLine.LineNumber))},{SourceUuidOrder(L(nameof(PurchaseOrderLine.Uid)))}
            """, parameters, ct))).ToList();
        if (head is not null) head.Lines = lines;
        return head;
    }

    /// <summary>借用请求连接与可空事务，显式类型包只处理本文件命令，不更改公共参数或隔离。</summary>
    private CommandDefinition Command(string sql, PostgreSqlParameters parameters, CancellationToken token)
        => new(sql, new QueryParameters(parameters), context.Session.Transaction, cancellationToken: token);

    /// <summary>按既有可信元数据引用表名，不读取外部标识符。</summary>
    private static string Table(EntityMetadata map) => PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);

    /// <summary>通过CLR属性取PG物理列；数量采用quantity，不沿用旧SQL Server的QTY名称。</summary>
    private static string Column(EntityMetadata map, string alias, string property)
        => alias + "." + PostgreSqlNames.Quote(map.Columns.Single(column => column.PropertyName == property).ColumnName);

    /// <summary>定位固定头属性，别名由本文件SQL指定。</summary>
    private static string H(string property) => Column(HeadMap, "h", property);
    /// <summary>定位固定行属性，包括数量与配送快照。</summary>
    private static string L(string property) => Column(LineMap, "l", property);
    /// <summary>定位当前产品属性，不把主数据值覆盖采购商业快照。</summary>
    private static string P(string property) => Column(ProductMap, "p", property);

    /// <summary>保留源uniqueidentifier顺序，仅用于内部固定SQL的UUID排序键。</summary>
    private static string SourceUuidOrder(string column)
        => "ARRAY[" + string.Join(',', new[] { 10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0 }.Select(offset => $"get_byte(uuid_send({column}),{offset})")) + "]";

    /// <summary>在既有参数克隆上明确nullable标量类型，不让NULL参数依赖服务端或包装连接推断。</summary>
    private sealed class QueryParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>命令拥有独立参数实例；只明确源筛选/标识/状态的类型，不改变值或所有权。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = parameter.ParameterName switch
                {
                    "CustomerId" or "SupplierId" or "Uid" => NpgsqlDbType.Uuid,
                    "Keyword" => NpgsqlDbType.Text,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
