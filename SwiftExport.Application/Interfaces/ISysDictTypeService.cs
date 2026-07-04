using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 系统字典类型服务接口
    /// </summary>
    public partial interface ISysDictTypeService
    {
    Task<ServiceResult<SysDictTypeDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<SysDictTypeDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(SysDictTypeDto dto);
    Task<ServiceResult<int>> UpdateAsync(SysDictTypeDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysDictTypeDto> batchReq);

        /// <summary>
        /// 获取字典类型分组树
        /// 按 GroupCode 分组，返回树形结构（一层分组树）
        /// </summary>
        /// <returns>分组树节点列表</returns>
        Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync();
    }
}
