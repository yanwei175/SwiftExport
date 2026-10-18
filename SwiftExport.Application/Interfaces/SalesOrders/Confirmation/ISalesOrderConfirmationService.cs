using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.SalesOrders.Confirmation;

/// <summary>在销售确认命令中原子更新订单、接收成品需求并返回可重放的完整结果。</summary>
public interface ISalesOrderConfirmationService
{
    /// <summary>确认指定销售订单；同一命令身份和输入重试返回首次提交结果。</summary>
    Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, ConfirmSalesOrderCommand request, CancellationToken cancellationToken = default);
}
