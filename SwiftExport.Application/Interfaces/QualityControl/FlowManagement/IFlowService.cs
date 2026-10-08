using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.QualityControl.FlowManagement;

/// <summary>流量管理的五个测试组用例；权限、版本和头行事务统一由服务负责。</summary>
public interface IFlowService
{
    /// <summary>分页读取测试组摘要。</summary>
    Task<ServiceResult<PagedResponseBase<FlowListItemDto>>> GetPagedAsync(FlowSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取包含历史快照和全部行的测试组详情。</summary>
    Task<ServiceResult<FlowDetailDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>建立测试组，失败时不保留部分头行。</summary>
    Task<ServiceResult<FlowSaveResponse>> CreateAsync(CreateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default);
    /// <summary>校验版本和行归属后原子更新完整测试组。</summary>
    Task<ServiceResult<FlowSaveResponse>> UpdateAsync(UpdateItemRequest<FlowEditDto> request, CancellationToken cancellationToken = default);
    /// <summary>按头版本原子软删除测试组及其未删除行。</summary>
    Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest request, CancellationToken cancellationToken = default);
}

