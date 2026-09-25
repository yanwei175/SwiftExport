using SwiftExport.Application.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Application.Services.Systems.Internal;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems.Dictionaries.Data
{
    /// <summary>
    /// 字典明细服务。
    /// </summary>
    public partial class SysDictDataService(
        ISysDictDataRepository repository,
        ISysDictTypeRepository dictTypeRepository,
        ServiceBaseContext context)
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
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                Logger.LogError(ex, "应用服务执行失败。");
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
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
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                Logger.LogError(ex, "应用服务执行失败。");
                return ServiceResult<PagedResponseBase<SysDictDataDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }


        public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode)
        {
            if (string.IsNullOrWhiteSpace(dictCode))
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail("字典编码不能为空。");
            try
            {
                var entities = await repository.GetByCodeAsync(dictCode.Trim());
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Ok(entities.Adapt<IReadOnlyList<SysDictDataDto>>());
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "按字典编码查询明细失败：{DictCode}", dictCode);
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<IReadOnlyList<SysDictDataDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        public async Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSaveAsync(SysDictDataBatchSaveRequest batchReq)
        {
            var requestError = SystemBatchSaveSupport.ValidateRequest(batchReq);
            if (requestError is not null) return ServiceResult<SysDictDataBatchSaveResponse>.Fail(requestError);
            if (_session.Transaction is not null)
                return ServiceResult<SysDictDataBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始字典明细批量保存。");

            var response = new SysDictDataBatchSaveResponse { TotalCount = batchReq.TotalCount };
            try
            {
                _session.Transaction = _session.Connection.BeginTransaction();
                var preparation = await PrepareBatchSaveAsync(batchReq);
                if (preparation.Error is not null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<SysDictDataBatchSaveResponse>.Fail(preparation.Error);
                }

                foreach (var item in batchReq.CreateItems)
                {
                    var entity = item.Data.Adapt<SysDictData>();
                    entity.IsSystem = false;
                    var writeResult = await repository.AddAsync(entity);
                    response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "新增字典明细失败。");
                }
                foreach (var item in batchReq.UpdateItems)
                {
                    var entity = preparation.Existing[item.Uid];
                    var isSystem = entity.IsSystem;
                    SystemBatchSaveSupport.AdaptPreservingServerFields(item.Data, entity);
                    entity.IsSystem = isSystem;
                    entity.Version = item.Version;
                    var writeResult = await repository.UpdateAsync(entity);
                    response.Items.Add(SaveItemResultFactory.FromUpdate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "更新字典明细失败。");
                }
                foreach (var item in batchReq.DeleteItems)
                {
                    var writeResult = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                    response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "删除字典明细失败。");
                }

                _session.Transaction.Commit();
                return ServiceResult<SysDictDataBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
            }
            catch (SystemBatchWriteException exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "字典明细批量保存");
                return ServiceResult<SysDictDataBatchSaveResponse>.Fail($"字典明细批量保存失败，所有操作已回滚。原因：{exception.Message}");
            }
            catch (Exception exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "字典明细批量保存");
                _logger.LogError(exception, "字典明细批量保存发生未预期异常。");
                return ServiceResult<SysDictDataBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "字典明细批量保存失败，所有操作已回滚，请刷新后重试。");
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }

        private async Task<DictDataBatchPreparation> PrepareBatchSaveAsync(SysDictDataBatchSaveRequest request)
        {
            foreach (var item in request.CreateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "新增");
                if (error is not null) return DictDataBatchPreparation.Fail(error);
            }
            foreach (var item in request.UpdateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "修改");
                if (error is not null) return DictDataBatchPreparation.Fail(error);
            }

            var dictCodes = request.CreateItems.Select(x => x.Data.DictCode)
                .Concat(request.UpdateItems.Select(x => x.Data.DictCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var dictTypes = await dictTypeRepository.GetByDictCodesAsync(dictCodes, withUpdLock: true);
            var existingCodes = dictTypes.Select(x => x.DictCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingCodes = dictCodes.Where(code => !existingCodes.Contains(code)).ToArray();
            if (missingCodes.Length > 0) return DictDataBatchPreparation.Fail("以下字典类型不存在：" + string.Join(", ", missingCodes));

            var targetIds = request.UpdateItems.Select(x => x.Uid).Concat(request.DeleteItems.Select(x => x.Uid)).Distinct().ToArray();
            var existing = (await repository.GetByIdListAsync(targetIds, withUpdLock: true)).ToDictionary(x => x.Uid);
            var missing = targetIds.Where(uid => !existing.ContainsKey(uid)).ToArray();
            if (missing.Length > 0) return DictDataBatchPreparation.Fail("修改或删除失败，以下字典明细不存在：" + string.Join(", ", missing));
            var protectedItems = SystemBatchSaveSupport.FindProtectedDeleteIds(request.DeleteItems, existing, entity => entity.IsSystem);
            if (protectedItems.Length > 0) return DictDataBatchPreparation.Fail("系统字典明细不能删除：" + string.Join(", ", protectedItems));
            return DictDataBatchPreparation.Ok(existing);
        }

        internal static string? NormalizeAndValidate(SysDictDataDto? dto, int? rowIndex, string operation)
        {
            var row = SystemBatchSaveSupport.FormatRow(rowIndex);
            if (dto is null) return $"第 {row} 行：{operation}数据不能为空。";
            if (string.IsNullOrWhiteSpace(dto.DictCode)) return $"第 {row} 行：字典编码不能为空。";
            if (string.IsNullOrWhiteSpace(dto.DataNameEn)) return $"第 {row} 行：英文名称不能为空。";
            if (string.IsNullOrWhiteSpace(dto.DataNameZh)) return $"第 {row} 行：中文名称不能为空。";
            dto.DictCode = dto.DictCode.Trim();
            dto.DataNameEn = dto.DataNameEn.Trim();
            dto.DataNameZh = dto.DataNameZh.Trim();
            dto.Remark = dto.Remark?.Trim();
            dto.CssClass = dto.CssClass?.Trim();
            return null;
        }

        private sealed record DictDataBatchPreparation(string? Error, Dictionary<Guid, SysDictData> Existing)
        {
            public static DictDataBatchPreparation Fail(string error) => new(error, []);
            public static DictDataBatchPreparation Ok(Dictionary<Guid, SysDictData> existing) => new(null, existing);
        }
        #endregion

    }
}
