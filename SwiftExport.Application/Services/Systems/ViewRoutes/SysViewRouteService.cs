using SwiftExport.Application.Interfaces.Systems.ViewRoutes;
using SwiftExport.Application.Services.Systems.Internal;
using SwiftExport.Core.Entities.Systems.ViewRoutes;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Systems.ViewRoutes;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems.ViewRoutes
{
    /// <summary>
    /// 视图路由配置服务实现
    /// 额外实现 GetEnabledRoutesAsync，供前端拉取缓存。
    /// </summary>
    public partial class SysViewRouteService(
        ISysViewRouteRepository repository,
        ISysDictTypeRepository dictTypeRepository,
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
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                Logger.LogError(ex, "应用服务执行失败。");
                return ServiceResult<IReadOnlyList<SysViewRouteDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        public async Task<ServiceResult<PagedResponseBase<SysViewRouteDto>>> GetPagedAsync(SysViewRouteSearchRequest request)
        {
            try
            {
                return ServiceResult<PagedResponseBase<SysViewRouteDto>>.Ok(
                    await _routeRepo.GetPagedAsync(request ?? new SysViewRouteSearchRequest()));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "分页查询视图路由失败。");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<PagedResponseBase<SysViewRouteDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }



        public async Task<ServiceResult<SysViewRouteBatchSaveResponse>> BatchSaveAsync(SysViewRouteBatchSaveRequest batchReq)
        {
            var requestError = SystemBatchSaveSupport.ValidateRequest(batchReq);
            if (requestError is not null) return ServiceResult<SysViewRouteBatchSaveResponse>.Fail(requestError);
            if (_session.Transaction is not null)
                return ServiceResult<SysViewRouteBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始视图路由批量保存。");

            var response = new SysViewRouteBatchSaveResponse { TotalCount = batchReq.TotalCount };
            try
            {
                _session.Transaction = _session.Connection.BeginTransaction();
                var preparation = await PrepareBatchSaveAsync(batchReq);
                if (preparation.Error is not null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<SysViewRouteBatchSaveResponse>.Fail(preparation.Error);
                }

                foreach (var item in batchReq.CreateItems)
                {
                    var writeResult = await _routeRepo.AddAsync(item.Data.Adapt<SysViewRoute>());
                    response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "新增视图路由失败。");
                }
                foreach (var item in batchReq.UpdateItems)
                {
                    var entity = preparation.Existing[item.Uid];
                    SystemBatchSaveSupport.AdaptPreservingServerFields(item.Data, entity);
                    entity.Version = item.Version;
                    var writeResult = await _routeRepo.UpdateAsync(entity);
                    response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "更新视图路由失败。");
                }
                foreach (var item in batchReq.DeleteItems)
                {
                    var writeResult = await _routeRepo.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                    response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "删除视图路由失败。");
                }

                _session.Transaction.Commit();
                return ServiceResult<SysViewRouteBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
            }
            catch (SystemBatchWriteException exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "视图路由批量保存");
                return ServiceResult<SysViewRouteBatchSaveResponse>.Fail($"视图路由批量保存失败，所有操作已回滚。原因：{exception.Message}");
            }
            catch (Exception exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "视图路由批量保存");
                _logger.LogError(exception, "视图路由批量保存发生未预期异常。");
                return ServiceResult<SysViewRouteBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "视图路由批量保存失败，所有操作已回滚，请刷新后重试。");
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }

        private async Task<RouteBatchPreparation> PrepareBatchSaveAsync(SysViewRouteBatchSaveRequest request)
        {
            foreach (var item in request.CreateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "新增");
                if (error is not null) return RouteBatchPreparation.Fail(error);
            }
            foreach (var item in request.UpdateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "修改");
                if (error is not null) return RouteBatchPreparation.Fail(error);
            }

            var all = await _routeRepo.GetAllForUpdateAsync();
            var existing = all.ToDictionary(entity => entity.Uid);
            var targetIds = request.UpdateItems.Select(x => x.Uid).Concat(request.DeleteItems.Select(x => x.Uid)).Distinct().ToArray();
            var missing = targetIds.Where(uid => !existing.ContainsKey(uid)).ToArray();
            if (missing.Length > 0) return RouteBatchPreparation.Fail("修改或删除失败，以下视图路由不存在：" + string.Join(", ", missing));

            var deleteIds = request.DeleteItems.Select(x => x.Uid).ToHashSet();
            var updateById = request.UpdateItems.ToDictionary(x => x.Uid);
            var finalKeys = all.Where(x => !deleteIds.Contains(x.Uid))
                .Select(x => updateById.TryGetValue(x.Uid, out var update) ? update.Data.TargetViewKey : x.TargetViewKey)
                .Concat(request.CreateItems.Select(x => x.Data.TargetViewKey))
                .ToArray();
            var finalKeyError = ValidateFinalKeys(finalKeys);
            if (finalKeyError is not null) return RouteBatchPreparation.Fail(finalKeyError);

            var changedOrDeletedKeys = request.DeleteItems.Select(x => existing[x.Uid].TargetViewKey)
                .Concat(request.UpdateItems.Where(x => !string.Equals(existing[x.Uid].TargetViewKey, x.Data.TargetViewKey, StringComparison.OrdinalIgnoreCase))
                    .Select(x => existing[x.Uid].TargetViewKey))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var referenced = await dictTypeRepository.GetReferencedTargetViewKeysAsync(changedOrDeletedKeys);
            if (referenced.Count > 0)
                return RouteBatchPreparation.Fail("以下路由键仍被字典类型引用，不能改名或删除：" + string.Join(", ", referenced));

            return RouteBatchPreparation.Ok(existing);
        }

        internal static string? ValidateFinalKeys(IEnumerable<string> keys)
        {
            var duplicate = keys.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            return duplicate is null ? null : $"路由键不能重复：{duplicate.Key}。";
        }

        private static string? NormalizeAndValidate(SysViewRouteDto? dto, int? rowIndex, string operation)
        {
            var row = SystemBatchSaveSupport.FormatRow(rowIndex);
            if (dto is null) return $"第 {row} 行：{operation}数据不能为空。";
            if (string.IsNullOrWhiteSpace(dto.TargetViewKey)) return $"第 {row} 行：路由键不能为空。";
            if (string.IsNullOrWhiteSpace(dto.DisplayName)) return $"第 {row} 行：路由显示名称不能为空。";
            if (string.IsNullOrWhiteSpace(dto.ViewModelTypeName)) return $"第 {row} 行：ViewModel 类型名不能为空。";
            dto.TargetViewKey = dto.TargetViewKey.Trim();
            dto.DisplayName = dto.DisplayName.Trim();
            dto.ViewModelTypeName = dto.ViewModelTypeName.Trim();
            dto.ViewTypeName = dto.ViewTypeName?.Trim();
            dto.RouteCategory = dto.RouteCategory?.Trim();
            dto.AssemblyName = dto.AssemblyName?.Trim();
            dto.Remark = dto.Remark?.Trim();
            return null;
        }

        private sealed record RouteBatchPreparation(string? Error, Dictionary<Guid, SysViewRoute> Existing)
        {
            public static RouteBatchPreparation Fail(string error) => new(error, []);
            public static RouteBatchPreparation Ok(Dictionary<Guid, SysViewRoute> existing) => new(null, existing);
        }
        #endregion

    }
}
