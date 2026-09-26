using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.QualityControl.AfterSales.Maintenance;
/// <summary>AfterSalesMaintenanceService 的窄应用契约。</summary>
public interface IAfterSalesMaintenanceService
{
    /// <summary>创建、保存并按原规则补充售后事实。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, bool create, CancellationToken cancellationToken = default);
}
