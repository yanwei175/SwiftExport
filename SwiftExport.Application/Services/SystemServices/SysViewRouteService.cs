using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SystemEntities;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.SystemServices
{
    /// <summary>
    /// 视图路由配置服务实现
    /// 额外实现 GetEnabledRoutesAsync，供前端拉取缓存。
    /// </summary>
    public partial class SysViewRouteService(
        ISysViewRouteRepository repository,
        ServiceBaseContext context) : BaseService<SysViewRoute, SysViewRouteDto>(repository, context), ISysViewRouteService
    {
        private readonly ILogger<SysViewRouteService> _logger = context.LoggerFactory.CreateLogger<SysViewRouteService>();

        // 保持对具体仓储的引用，以访问扩展方法
        private readonly ISysViewRouteRepository _routeRepo = repository;

        /// <summary>
        /// 获取所有启用的路由配置，映射为 DTO 列表返回。
        /// </summary>
        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutesAsync()
        {
            var entities = await _routeRepo.GetEnabledRoutesAsync();
            var dtos = entities.Adapt<IReadOnlyList<SysViewRouteDto>>();
            return ServiceResult<IReadOnlyList<SysViewRouteDto>>.Ok(dtos);
        }


        #region 显式基础读写

        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAllAsync()
        {
            try
            {
                var entities = await _routeRepo.GetAllAsync();
                return entities.Any()
                    ? ServiceResult<IReadOnlyList<SysViewRouteDto>>.Ok(entities.Adapt<IReadOnlyList<SysViewRouteDto>>())
                    : ServiceResult<IReadOnlyList<SysViewRouteDto>>.Fail("未找到数据");
            }
            catch (Exception ex)
            {
                return ServiceResult<IReadOnlyList<SysViewRouteDto>>.Fail(ex.Message);
            }
        }

        public async Task<ServiceResult<PagedResponseBase<SysViewRouteDto>>> GetPagedAsync(SysViewRouteSearchRequest request)
        {
            throw new NotImplementedException();
        }



        public async Task<ServiceResult<SysViewRouteBatchSaveResponse>> BatchSaveAsync(SysViewRouteBatchSaveRequest batchReq)
        {
            throw new NotImplementedException();
            //_session.Transaction = _session.Connection.BeginTransaction();
            //var result = new SysViewRouteBatchSaveResponse();

            //var createList = batchReq.CreateItems?.Adapt<List<SysViewRoute>>();
            //var updateList = batchReq.UpdateItems?.Adapt<List<SysViewRoute>>();
            //var deleteIds = batchReq.DeleteItems;

            //try
            //{
            //    var errorMsg = new StringBuilder();

            //    if (updateList is { Count: > 0 })
            //    {
            //        var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
            //        if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

            //        var needUpdateUids = updateList.Select(x => x.Uid).ToList();
            //        var existingUids = await _routeRepo.GetExistingIdListAsync(needUpdateUids);
            //        var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
            //        if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
            //    }

            //    if (deleteIds is { Count: > 0 })
            //    {
            //        var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
            //        if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            //    }

            //    if (errorMsg.Length > 0)
            //        return ServiceResult<SysViewRouteBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            //    if (_routeRepo is not IBatchWriteRepository<SysViewRoute> batchRepository)
            //        return ServiceResult<SysViewRouteBatchSaveResponse>.Fail($"{nameof(SysViewRoute)} 仓储未实现批量保存");

            //    if (createList is { Count: > 0 })
            //    {
            //        result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
            //    }

            //    if (updateList is { Count: > 0 })
            //    {
            //        await batchRepository.UpdateRangeAsync(updateList);
            //        result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
            //    }

            //    if (deleteIds is { Count: > 0 })
            //    {
            //        await batchRepository.DeleteRangeSoftAsync(deleteIds);
            //        result.DeleteIdList = deleteIds;
            //    }

            //    _session.Transaction.Commit();
            //    return ServiceResult<SysViewRouteBatchSaveResponse>.Ok(result);
            //}
            //catch (Exception ex)
            //{
            //    _session.Transaction.Rollback();
            //    return ServiceResult<SysViewRouteBatchSaveResponse>.Fail(ex.Message);
            //}
            //finally
            //{
            //    _session.Transaction.Dispose();
            //    _session.Transaction = null;
            //}
        }
        #endregion

    }
}
