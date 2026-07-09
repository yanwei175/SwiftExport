using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 字典明细服务。
    /// </summary>
    public partial class SysDictDataService(ISysDictDataRepository repository, ServiceBaseContext context)
        : BaseService<SysDictData, SysDictDataDto>(repository, context), ISysDictDataService
    {

        private readonly ILogger<SysDictDataService> _logger = context.LoggerFactory.CreateLogger<SysDictDataService>();


        #region 显式基础读写


        public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync()
        {
            try
            {
                var entities = await _repository.GetAllAsync();
                return entities.Any()
                    ? ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(entities.Adapt<IReadOnlyList<SysDictDataDto>>())
                    : ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("未找到数据");
            }
            catch (Exception ex)
            {
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail(ex.Message);
            }
        }

        public async Task<ServiceResult<PagedResponseBase<SysDictDataDto>>> GetPagedAsync(SysDictDataSearchRequest request)
        {
            try
            {
                var result = await repository.GetPagedAsync(request);
                if (result == null)
                    return ServiceResult<PagedResponseBase<SysDictDataDto>>.Fail("未找到数据");
                return ServiceResult<PagedResponseBase<SysDictDataDto>>.Ok(result);

            }
            catch (Exception ex)
            {
                return ServiceResult<PagedResponseBase<SysDictDataDto>>.Fail(ex.Message);
            }
        }


        public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode)
        {
            throw new NotImplementedException();

        }

        public Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest batchReq)
        {
            throw new NotImplementedException();
        }
        #endregion

    }
}
