using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>PG销售详情分阶段读取，当前头行商业字段与冻结交付事实分别保留源观察时刻。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository
{
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string DetailLineColumns = """
        l."uid" AS "Uid",
        l."version" AS "Version",
        l."created_date" AS "CreatedDate",
        l."created_by" AS "CreatedBy",
        l."updated_date" AS "UpdatedDate",
        l."updated_by" AS "UpdatedBy",
        l."line_number" AS "LineNumber",
        l."is_non_standard" AS "IsNonStandard",
        l."product_id" AS "ProductId",
        l."customer_product_id" AS "CustomerProductId",
        l."sku" AS "Sku",
        l."link_sku" AS "LinkSku",
        l."customer_sku" AS "CustomerSku",
        l."description_en" AS "DescriptionEn",
        l."color_name_en" AS "ColorNameEn",
        l."features_en" AS "FeaturesEn",
        l."cny_price" AS "CnyPrice",
        l."usd_price" AS "UsdPrice",
        l."qty" AS "Qty",
        l."unit_snapshot" AS "UnitSnapshot",
        l."pcs_of_carton" AS "PcsOfCarton",
        l."nw_of_carton" AS "NwOfCarton",
        l."gw_of_carton" AS "GwOfCarton",
        l."cbm_of_carton" AS "CbmOfCarton",
        l."sub_note" AS "SubNote",
        l."expected_delivery_date" AS "ExpectedDeliveryDate",
        l."main_image" AS "MainImage"
    """;

    /// <summary>借用调用方可空事务依次物化候选/交付事实再读取头行；空头在行集合完整缓冲后返回null。</summary>
    public async Task<SalesOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var parameters = CreateParameters(uid);
        await FreezeCandidatesAsync(parameters, cancellationToken);
        await FreezeLineFactsAsync(parameters, cancellationToken);
        var header = (await ReadAsync<SalesOrderHeader>($"SELECT {HeaderProjection} FROM \"public\".\"sales_order_header\" WHERE uid=@Uid AND is_deleted=FALSE",
            parameters, cancellationToken)).SingleOrDefault();
        // 原头后第二结果要完整释放；不内部取消读取器，不用早返触发调用方PG事务被取消。
        var lines = await ReadAsync<SalesOrderDetailLineDto>($"""
            WITH facts AS ({LineFactsRelation})
            SELECT {DetailLineColumns},facts."ShippedQty",facts."UnshippedQty",facts."ReviewReason",
                   COALESCE(l.cny_amount,l.cny_price*l.qty) AS "CnyAmount",
                   COALESCE(l.usd_amount,l.usd_price*l.qty) AS "UsdAmount"
            FROM "public"."sales_order_line" l INNER JOIN facts ON facts."SalesOrderLineId"=l.uid
            WHERE l.sales_order_id=@Uid AND l.is_deleted=FALSE
            ORDER BY l.line_number,{SourceUuidOrder("l.uid")}
            """, parameters, cancellationToken);
        if (header is null) return null;
        return new SalesOrderDetailDto
        {
            Header = MapHeader(header),
            Lines = lines,
            CloseReason = header.CloseReason ?? string.Empty,
            ClosedDate = header.ClosedDate,
            ClosedBy = header.ClosedBy ?? string.Empty
        };
    }

    /// <summary>按原编辑头契约回落可空商业字段；关闭信息仍独立返回，不回查主数据补快照。</summary>
    private static SalesOrderEditHeaderDto MapHeader(SalesOrderHeader header) => new()
    {
        Uid = header.Uid,
        Version = header.Version,
        DocumentStatus = header.DocumentStatus,
        ConfirmationStatus = header.ConfirmationStatus,
        CustomerId = header.CustomerId ?? Guid.Empty,
        CustomerCode = header.CustomerCode ?? string.Empty,
        OrderNo = header.OrderNo,
        InvoiceNo = header.InvoiceNo ?? string.Empty,
        CustomerPoNo = header.CustomerPoNo ?? string.Empty,
        ShippedBy = header.ShippedBy ?? string.Empty,
        Currency = header.Currency ?? string.Empty,
        ExchangeRate = header.ExchangeRate ?? 0,
        ShipToName = header.ShipToName ?? string.Empty,
        ShipToAddress = header.ShipToAddress ?? string.Empty,
        ShipToPhone = header.ShipToPhone ?? string.Empty,
        Incoterm = header.Incoterm ?? string.Empty,
        IncotermSortRemark = header.IncotermSortRemark ?? string.Empty,
        IncotermLocation = header.IncotermLocation ?? string.Empty,
        DepositRate = header.DepositRate ?? 0,
        LeadTimeDays = header.LeadTimeDays,
        PaymentMethod = header.PaymentMethod ?? string.Empty,
        SoFolder = header.SoFolder ?? string.Empty,
        Remark = header.Remark ?? string.Empty,
        OrderDate = header.OrderDate,
        DeliveryDate = header.DeliveryDate,
        ReplyDate = header.ReplyDate,
        Eta = header.ETA,
        Etd = header.ETD
    };
}
