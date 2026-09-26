using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.QualityControl.AfterSales.Lifecycle;
/// <summary>AfterSalesLifecycleService 的窄应用契约。</summary>
public interface IAfterSalesLifecycleService
{
    /// <summary>结案售后单。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command, CancellationToken cancellationToken = default);
    /// <summary>重开已结案售后单。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, ReopenAfterSalesCommand command, CancellationToken cancellationToken = default);
    /// <summary>作废未保留活动来源的售后单。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, VoidAfterSalesCommand command, CancellationToken cancellationToken = default);
}
