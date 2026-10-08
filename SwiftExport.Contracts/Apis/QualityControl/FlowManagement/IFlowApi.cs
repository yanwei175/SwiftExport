using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.QualityControl.FlowManagement
{
    /// <summary>流量测试组专用接口；写入均提交完整组，独立行没有公开写入口。</summary>
    public interface IFlowApi
    {
        /// <summary>按物料、历史 SKU 和测试日期分页查询。</summary>
        [Post("/api/quality-control/flows/search")]
        Task<ServiceResult<PagedResponseBase<FlowListItemDto>>> GetPagedAsync([Body] FlowSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取一个测试组及所有未删除功能行。</summary>
        [Get("/api/quality-control/flows/{id}")]
        Task<ServiceResult<FlowDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>新增测试组并由服务端生成物料和字典名称快照。</summary>
        [Post("/api/quality-control/flows")]
        Task<ServiceResult<FlowSaveResponse>> CreateAsync([Body] CreateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按头版本提交完整最终行集合，遗漏旧行按软删除处理。</summary>
        [Put("/api/quality-control/flows/{id}")]
        Task<ServiceResult<FlowSaveResponse>> UpdateAsync(Guid id, [Body] UpdateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按头版本整体删除；请求的软硬删除标记不改变本模块固定软删除规则。</summary>
        [Delete("/api/quality-control/flows/{id}")]
        Task<ServiceResult<SaveItemResult>> DeleteAsync(Guid id, [Body] DeleteItemRequest request, CancellationToken cancellationToken = default(CancellationToken));
    }
}

