namespace SwiftExportClient.Features.Sales.Orders.Services;

public interface ISalesOrderCancellationDialogService
{
    Task<string?> RequestReasonAsync(
        string orderNo,
        CancellationToken cancellationToken = default);
}
