using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;

/// <summary>独立拥有售后实物确认事务与完整命令回执。</summary>
public interface IAfterSalesConfirmationService
{
    /// <summary>确认售后单的正数量实物需求，纯投诉按原规则不接收来源。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid,
        ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default);
}
