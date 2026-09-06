namespace SwiftExportClient.Features.Sales.Fulfillment.Models;

/// <summary>销售履约 Detail 的窄打开参数，只携带稳定销售订单身份。</summary>
public sealed record SalesFulfillmentDetailOpenRequest(Guid SalesOrderId);
