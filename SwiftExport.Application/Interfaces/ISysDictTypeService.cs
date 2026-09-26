using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 系统字典类型服务接口
/// </summary>
public partial interface ISysDictTypeService : IBaseService<SysDictType, SysDictTypeDto>
{

    Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>> GetPagedAsync(SysDictTypeSearchRequest request);
    Task<ServiceResult<SysDictTypeBatchSaveResponse>> BatchSaveAsync(SysDictTypeBatchSaveRequest batchReq);

    /// <summary>
    /// 获取字典类型分组树
    /// 按 GroupCode 分组，返回树形结构（一层分组树）
    /// </summary>
    /// <returns>分组树节点列表</returns>
    Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync();
}
