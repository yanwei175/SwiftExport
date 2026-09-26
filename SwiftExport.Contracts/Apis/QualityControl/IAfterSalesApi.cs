using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.QualityControl
{
    /// <summary>售后事实及来源交接的客户端契约。</summary>
    public interface IAfterSalesApi
    {
        /// <summary>查询售后分页。</summary>
        [Get("/api/quality-control/after-sales")]
        Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync([Query] AfterSalesSearchRequest request,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>读取售后详情。</summary>
        [Get("/api/quality-control/after-sales/{uid}")]
        Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>读取安全候选。</summary>
        [Get("/api/quality-control/after-sales/lookups/{kind}")]
        Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind,[Query] AfterSalesLookupRequest request,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>创建售后单。</summary>
        [Post("/api/quality-control/after-sales")]
        Task<ServiceResult<AfterSalesCommandResult>> CreateAsync([Body] SaveAfterSalesCommand command,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>保存售后事实。</summary>
        [Put("/api/quality-control/after-sales/{uid}")]
        Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid,[Body] SaveAfterSalesCommand command,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>执行售后ConfirmDemand命令。</summary>
        [Post("/api/quality-control/after-sales/{uid}/confirm-demand")]
        Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid,[Body] ConfirmAfterSalesDemandCommand command,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>执行售后CancelDemandConfirmation命令。</summary>
        [Post("/api/quality-control/after-sales/{uid}/cancel-demand-confirmation")]
        Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid,[Body] CancelAfterSalesDemandConfirmationCommand command,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>执行售后Close命令。</summary>
        [Post("/api/quality-control/after-sales/{uid}/close")]
        Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid,[Body] CloseAfterSalesCommand command,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>执行售后Reopen命令。</summary>
        [Post("/api/quality-control/after-sales/{uid}/reopen")]
        Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid,[Body] ReopenAfterSalesCommand command,CancellationToken cancellationToken=default(CancellationToken));
        /// <summary>执行售后Void命令。</summary>
        [Post("/api/quality-control/after-sales/{uid}/void")]
        Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid,[Body] VoidAfterSalesCommand command,CancellationToken cancellationToken=default(CancellationToken));
    }
}

