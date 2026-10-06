using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>PG销售详情分阶段读取，当前头行商业字段与冻结交付事实分别保留源观察时刻。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository
{
    /// <summary>借用调用方可空事务依次物化候选/交付事实再读取头行；空头在行集合完整缓冲后返回null。</summary>
    public async Task<SalesOrderDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var parameters = CreateParameters(uid);
        await FreezeCandidatesAsync(parameters, cancellationToken);
        await FreezeLineFactsAsync(parameters, cancellationToken);
        var header = (await ReadAsync<SalesOrderHeader>($"SELECT {HeaderProjection} FROM {Headers} WHERE uid=@Uid AND is_deleted=FALSE",
            parameters, cancellationToken)).SingleOrDefault();
        // 原头后第二结果要完整释放；不内部取消读取器，不用早返触发调用方PG事务被取消。
        var lines = await ReadAsync<SalesOrderDetailLineDto>($"""
            WITH facts AS ({LineFactsRelation})
            SELECT {DetailLineProjection()},facts."ShippedQty",facts."UnshippedQty",facts."ReviewReason",
                   COALESCE(l.cny_amount,l.cny_price*l.qty) AS "CnyAmount",
                   COALESCE(l.usd_amount,l.usd_price*l.qty) AS "UsdAmount"
            FROM {Lines} l INNER JOIN facts ON facts."SalesOrderLineId"=l.uid
            WHERE l.sales_order_id=@Uid AND l.is_deleted=FALSE
            ORDER BY l.line_number,{SourceUuidOrder("l.uid")}
            """, parameters, cancellationToken);
        if (header is null) return null;
        return new SalesOrderDetailDto
        {
            Header = MapHeader(header), Lines = lines, CloseReason = header.CloseReason ?? string.Empty,
            ClosedDate = header.ClosedDate, ClosedBy = header.ClosedBy ?? string.Empty
        };
    }

    /// <summary>只投影源Detail显式商业列，金额在专属表达式回落，不通过SELECT*混入保存字段。</summary>
    private static string DetailLineProjection()
    {
        var properties = new HashSet<string>([
            "Uid","Version","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy","LineNumber","IsNonStandard",
            "ProductId","CustomerProductId","Sku","LinkSku","CustomerSku","DescriptionEn","ColorNameEn","FeaturesEn",
            "Qty","UnitSnapshot","CnyPrice","UsdPrice","PcsOfCarton","NwOfCarton","GwOfCarton","CbmOfCarton",
            "ExpectedDeliveryDate","SubNote","MainImage"], StringComparer.Ordinal);
        return string.Join(',', LineMetadata.Columns.Where(column => properties.Contains(column.PropertyName))
            .Select(column => $"l.\"{column.ColumnName}\" AS \"{column.PropertyName}\""));
    }

    /// <summary>按原编辑头契约回落可空商业字段；关闭信息仍独立返回，不回查主数据补快照。</summary>
    private static SalesOrderEditHeaderDto MapHeader(SalesOrderHeader header) => new()
    {
        Uid = header.Uid, Version = header.Version, DocumentStatus = header.DocumentStatus,
        ConfirmationStatus = header.ConfirmationStatus, CustomerId = header.CustomerId ?? Guid.Empty,
        CustomerCode = header.CustomerCode ?? string.Empty, OrderNo = header.OrderNo,
        InvoiceNo = header.InvoiceNo ?? string.Empty, CustomerPoNo = header.CustomerPoNo ?? string.Empty,
        ShippedBy = header.ShippedBy ?? string.Empty, Currency = header.Currency ?? string.Empty,
        ExchangeRate = header.ExchangeRate ?? 0, ShipToName = header.ShipToName ?? string.Empty,
        ShipToAddress = header.ShipToAddress ?? string.Empty, ShipToPhone = header.ShipToPhone ?? string.Empty,
        Incoterm = header.Incoterm ?? string.Empty, IncotermSortRemark = header.IncotermSortRemark ?? string.Empty,
        IncotermLocation = header.IncotermLocation ?? string.Empty, DepositRate = header.DepositRate ?? 0,
        LeadTimeDays = header.LeadTimeDays, PaymentMethod = header.PaymentMethod ?? string.Empty,
        SoFolder = header.SoFolder ?? string.Empty, Remark = header.Remark ?? string.Empty,
        OrderDate = header.OrderDate, DeliveryDate = header.DeliveryDate, ReplyDate = header.ReplyDate,
        Eta = header.ETA, Etd = header.ETD
    };
}
