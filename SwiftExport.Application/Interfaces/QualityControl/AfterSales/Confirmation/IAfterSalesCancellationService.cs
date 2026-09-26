using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
/// <summary>AfterSalesCancellationService 的窄应用契约。</summary>
public interface IAfterSalesCancellationService
{
    /// <summary>按旧命令协议撤回需求确认。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command, CancellationToken cancellationToken = default);
}
