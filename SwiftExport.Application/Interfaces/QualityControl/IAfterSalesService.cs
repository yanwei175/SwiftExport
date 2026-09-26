using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.QualityControl;
/// <summary>售后查询、事实保存与来源需求交接的唯一业务入口。</summary>
public interface IAfterSalesService
{
    /// <summary>查询单头分页。</summary>
    Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取两表详情。</summary>
    Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>读取安全候选。</summary>
    Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken = default);
    /// <summary>创建或保存事实，事务失败不保留部分行。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, bool create, CancellationToken cancellationToken = default);
    /// <summary>执行售后ConfirmDemand显式命令并记录日志。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default);
    /// <summary>执行售后CancelDemandConfirmation显式命令并记录日志。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command, CancellationToken cancellationToken = default);
    /// <summary>执行售后Close显式命令并记录日志。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command, CancellationToken cancellationToken = default);
    /// <summary>执行售后Reopen显式命令并记录日志。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, ReopenAfterSalesCommand command, CancellationToken cancellationToken = default);
    /// <summary>执行售后Void显式命令并记录日志。</summary>
    Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, VoidAfterSalesCommand command, CancellationToken cancellationToken = default);
}

