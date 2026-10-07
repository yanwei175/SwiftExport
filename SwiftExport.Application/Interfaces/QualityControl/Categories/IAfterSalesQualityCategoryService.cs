using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.QualityControl.Categories;

/// <summary>售后品质分类查询与整树批量维护，不提供绕过层级校验的单条 CRUD。</summary>
public interface IAfterSalesQualityCategoryService
{
    /// <summary>按查看权限读取全部未删节点，包括停用节点。</summary>
    Task<ServiceResult<List<AfterSalesQualityCategoryDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    /// <summary>按维护权限校验最终树及版本，在一个事务内保存全部变更。</summary>
    Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>> BatchSaveAsync(AfterSalesQualityCategoryBatchSaveRequest request, CancellationToken cancellationToken = default);
}
