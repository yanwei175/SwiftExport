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
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 货币主数据服务
    /// 核心职责：
    /// 1. 提供货币主数据 CRUD 和业务查询
    /// 2. 扩展本位币唯一性校验（批量保存时的前置拦截）
    /// 3. 提供"获取本位币"、"获取启用列表"等业务查询
    /// </summary>
    public partial class SysDicCurrencyService(ISysDicCurrencyRepository repository, ServiceBaseContext context,
        ISysDicCurrencyExchangeRepository exchangeRepository)
        : BaseService<SysDicCurrency, SysDicCurrencyDto>(repository, context), ISysDicCurrencyService
    {

        private readonly ILogger<SysDicCurrencyService> _logger = context.LoggerFactory.CreateLogger<SysDicCurrencyService>();


        // ============================================================
        // 业务查询扩展
        // ============================================================
        public async Task<ServiceResult<int>> SetNewBaseCurrencyAsync(SysDicCurrencyDto newSysDicCurrencyDto)
        {
            if (newSysDicCurrencyDto is null || newSysDicCurrencyDto.Uid == Guid.Empty || newSysDicCurrencyDto.Version <= 0)
                return ServiceResult<int>.Fail("本位币标识或版本无效。");
            if (_session.Transaction is not null)
                return ServiceResult<int>.Fail("当前请求已存在未完成事务，不能重复开始本位币切换。");

            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                var entity = await repository.GetByIdAsync(newSysDicCurrencyDto.Uid, withUpdLock: true);
                if (entity is null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<int>.Fail("重置本位币失败，未找到目标货币。");
                }
                var isSystem = entity.IsSystem;
                SystemBatchSaveSupport.AdaptPreservingServerFields(newSysDicCurrencyDto, entity);
                entity.IsSystem = isSystem;
                entity.Version = newSysDicCurrencyDto.Version;
                await repository.ResetAllBaseCurrencyAsync();
                entity.IsBase = true;
                entity.IsEnabled = true;
                entity.RateToBase = 1m;
                var rst = await repository.UpdateAsync(entity);
                if (!rst.Success)
                {
                    _session.Transaction.Rollback();
                    _logger.LogWarning("重置本位币失败，未找到匹配的货币代码：{CurrencyCode}", newSysDicCurrencyDto.CurrencyCode);
                    return ServiceResult<int>.Fail(rst.Message ?? "重置本位币失败。");
                }
                _session.Transaction.Commit();
                return ServiceResult<int>.Ok(1, "成功重置本位币");
            }
            catch (Exception ex)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "本位币切换");
                if (_logger.IsEnabled(LogLevel.Error)) _logger.LogError(ex, "[{EntityName}] 保存时发生错误，事务已回滚！", nameof(SysDicCurrency));
                return ServiceResult<int>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "重置本位币失败，所有操作已回滚。");
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }

        }

        /// <summary>
        /// 获取当前本位币
        /// </summary>
        public async Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync()
        {
            try
            {
                var currencies = await repository.GetBaseCurrenciesAsync();
                if (currencies.Count == 0)
                    return ServiceResult<SysDicCurrencyDto>.Fail("系统中未设置本位币。");
                if (currencies.Count > 1)
                {
                    _logger.LogError("检测到多个本位币：{CurrencyCodes}", string.Join(", ", currencies.Select(x => x.CurrencyCode)));
                    return ServiceResult<SysDicCurrencyDto>.Fail("系统中存在多个本位币，请先修复货币配置。");
                }
                if (!currencies[0].IsEnabled || currencies[0].RateToBase != 1m)
                    return ServiceResult<SysDicCurrencyDto>.Fail("本位币必须启用且对本位币汇率必须为 1。");
                return ServiceResult<SysDicCurrencyDto>.Ok(currencies[0].Adapt<SysDicCurrencyDto>());
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "查询本位币失败。");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<SysDicCurrencyDto>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        /// <summary>
        /// 获取所有启用的货币（用于下拉选择器）
        /// </summary>
        public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync()
        {
            try
            {
                var currencies = await repository.GetEnabledCurrenciesAsync();
                return ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Ok(currencies.Adapt<IReadOnlyList<SysDicCurrencyDto>>());
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "查询启用货币列表失败。");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }





        // ============================================================
        // 新增前校验：
        // 1. CurrencyCode 必填 + 不能重复
        // 2. 传入列表中最多只能有 1 个 IsBase = true
        // 3. 若新增行中含 IsBase = true，数据库已有本位币则报错
        // ============================================================
        private async Task<string?> ValidateBeforeAddAsync(List<SysDicCurrency> entities)
        {
            // —— 校验 1：CurrencyCode 必填 ——
            var emptyCodeItems = entities.Where(x => string.IsNullOrWhiteSpace(x.CurrencyCode)).ToList();
            if (emptyCodeItems.Count != 0)
            {
                return "货币代码(CurrencyCode)不能为空";
            }

            // —— 校验 2：传入批次内 IsBase 唯一性 ——
            var baseInBatch = entities.Where(x => x.IsBase).ToList();
            if (baseInBatch.Count > 1)
            {
                var codes = string.Join(", ", baseInBatch.Select(x => x.CurrencyCode));
                return $"一次提交中只能有一个本位币，当前批次中有 {baseInBatch.Count} 个 IsBase=true：[{codes}]";
            }

            // —— 校验 3：若本次新增含本位币，检查数据库是否已存在本位币 ——
            if (baseInBatch.Count == 1)
            {
                var existingBaseIds = await repository.GetBaseIsBaseCurrencyIdsAsync();
                if (existingBaseIds.Count > 0)
                {
                    return $"系统中已存在本位币（ID: {string.Join(", ", existingBaseIds)}），无法再设置新的本位币。请先取消原有本位币设置后再操作。";
                }
            }

            // —— 校验 4：CurrencyCode 在数据库中不能重复 ——
            var codes2 = entities.Select(x => x.CurrencyCode!).ToList();
            var existedCodes = await repository.GetExistingCurrencyCodesAsync(codes2);
            if (existedCodes.Count > 0)
            {
                return $"以下货币代码已存在，拒绝新增：{string.Join(", ", existedCodes)}";
            }

            return null; // null = 校验全部通过
        }

        // ============================================================
        // 修改前校验：
        // 1. 传入列表中最多只能有 1 个 IsBase = true
        // 2. 若某行要更新为 IsBase = true，数据库中不能有除了这行自身以外的本位币
        // ============================================================
        private async Task<string?> ValidateBeforeUpdateAsync(List<SysDicCurrency> entities)
        {
            // —— 校验 1：传入批次内 IsBase 唯一性 ——
            var baseInBatch = entities.Where(x => x.IsBase).ToList();
            if (baseInBatch.Count > 1)
            {
                var codes = string.Join(", ", baseInBatch.Select(x => x.CurrencyCode));
                return $"一次提交中只能有一个本位币，当前批次中有 {baseInBatch.Count} 个 IsBase=true：[{codes}]";
            }

            // —— 校验 2：如果某行要被设为本位币，检查数据库中是否有其他本位币 ——
            if (baseInBatch.Count == 1)
            {
                // 排除本次要更新的这些行自身（避免误判：原来它就是本位币，现在只是更新其他字段）
                var updatingIds = entities.Select(x => x.Uid).ToList();
                var existingBaseIds = await repository.GetBaseIsBaseCurrencyIdsAsync(updatingIds);

                if (existingBaseIds.Count > 0)
                {
                    return $"系统中已存在本位币（ID: {string.Join(", ", existingBaseIds)}），无法将多个货币同时设为本位币。请先取消原有本位币设置后再操作。";
                }
            }

            return null; // null = 校验全部通过
        }

        public async Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrencyAsync()
        {
            try
            {
                var currency = await GetBaseCurrencyAsync();
                if (!currency.Success || currency.Data is null)
                    return ServiceResult<SysDicCurrencyDetailDto>.Fail(currency.Message);

                var exchange = await exchangeRepository.GetAllAsync();
                var detailDto = new SysDicCurrencyDetailDto
                {
                    Currency = currency.Data,
                    CurrencyRates = exchange.Adapt<List<SysDicCurrencyExchangeDto>>()
                };
                return ServiceResult<SysDicCurrencyDetailDto>.Ok(detailDto);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "查询默认本位币详情失败。");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<SysDicCurrencyDetailDto>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }




        public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetAllAsync()
        {
            try
            {
                var entities = await repository.GetAllAsync();
                return entities.Any()
                    ? ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Ok(entities.Adapt<IReadOnlyList<SysDicCurrencyDto>>())
                    : ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Fail("未找到数据");
            }
            catch (Exception ex)
            {
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                Logger.LogError(ex, "应用服务执行失败。");
                return ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        public async Task<ServiceResult<PagedResponseBase<SysDicCurrencyDto>>> GetPagedAsync(SysDicCurrencySearchRequest request)
        {
            try
            {
                return ServiceResult<PagedResponseBase<SysDicCurrencyDto>>.Ok(
                    await repository.GetPagedAsync(request ?? new SysDicCurrencySearchRequest()));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "分页查询货币失败。");
                // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
                return ServiceResult<PagedResponseBase<SysDicCurrencyDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
            }
        }

        public async Task<ServiceResult<SysDicCurrencyBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyBatchSaveRequest batchReq)
        {
            var requestError = SystemBatchSaveSupport.ValidateRequest(batchReq);
            if (requestError is not null) return ServiceResult<SysDicCurrencyBatchSaveResponse>.Fail(requestError);
            if (_session.Transaction is not null)
                return ServiceResult<SysDicCurrencyBatchSaveResponse>.Fail("当前请求已存在未完成事务，不能重复开始货币批量保存。");

            var response = new SysDicCurrencyBatchSaveResponse { TotalCount = batchReq.TotalCount };
            try
            {
                _session.Transaction = _session.Connection.BeginTransaction();
                var preparation = await PrepareBatchSaveAsync(batchReq);
                if (preparation.Error is not null)
                {
                    _session.Transaction.Rollback();
                    return ServiceResult<SysDicCurrencyBatchSaveResponse>.Fail(preparation.Error);
                }

                foreach (var item in batchReq.CreateItems)
                {
                    var entity = item.Data.Adapt<SysDicCurrency>();
                    entity.IsSystem = false;
                    var writeResult = await repository.AddAsync(entity);
                    response.Items.Add(SaveItemResultFactory.FromCreate(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "新增货币失败。");
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
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "更新货币失败。");
                }
                foreach (var item in batchReq.DeleteItems)
                {
                    var writeResult = await repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                    response.Items.Add(SaveItemResultFactory.FromDelete(item, writeResult));
                    SystemBatchSaveSupport.EnsureWriteSucceeded(writeResult, "删除货币失败。");
                }

                _session.Transaction.Commit();
                return ServiceResult<SysDicCurrencyBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
            }
            catch (SystemBatchWriteException exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "货币批量保存");
                return ServiceResult<SysDicCurrencyBatchSaveResponse>.Fail($"货币批量保存失败，所有操作已回滚。原因：{exception.Message}");
            }
            catch (Exception exception)
            {
                SystemBatchSaveSupport.TryRollback(_session, _logger, "货币批量保存");
                _logger.LogError(exception, "货币批量保存发生未预期异常。");
                return ServiceResult<SysDicCurrencyBatchSaveResponse>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "货币批量保存失败，所有操作已回滚，请刷新后重试。");
            }
            finally
            {
                _session.Transaction?.Dispose();
                _session.Transaction = null;
            }
        }

        private async Task<CurrencyBatchPreparation> PrepareBatchSaveAsync(SysDicCurrencyBatchSaveRequest request)
        {
            foreach (var item in request.CreateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "新增");
                if (error is not null) return CurrencyBatchPreparation.Fail(error);
            }
            foreach (var item in request.UpdateItems)
            {
                var error = NormalizeAndValidate(item.Data, item.RowIndex, "修改");
                if (error is not null) return CurrencyBatchPreparation.Fail(error);
            }

            var all = await repository.GetAllForUpdateAsync();
            var existing = all.ToDictionary(x => x.Uid);
            var targetIds = request.UpdateItems.Select(x => x.Uid).Concat(request.DeleteItems.Select(x => x.Uid)).Distinct().ToArray();
            var missing = targetIds.Where(uid => !existing.ContainsKey(uid)).ToArray();
            if (missing.Length > 0) return CurrencyBatchPreparation.Fail("修改或删除失败，以下货币不存在：" + string.Join(", ", missing));
            var systemDeleteIds = SystemBatchSaveSupport.FindProtectedDeleteIds(request.DeleteItems, existing, entity => entity.IsSystem);
            if (systemDeleteIds.Length > 0)
                return CurrencyBatchPreparation.Fail("系统内置货币不能删除：" + string.Join(", ", systemDeleteIds.Select(uid => existing[uid].CurrencyCode)));

            var deleteIds = request.DeleteItems.Select(x => x.Uid).ToHashSet();
            var updateById = request.UpdateItems.ToDictionary(x => x.Uid);
            var finalState = all.Where(x => !deleteIds.Contains(x.Uid))
                .Select(x => updateById.TryGetValue(x.Uid, out var update)
                    ? new CurrencyState(update.Data.CurrencyCode, update.Data.IsBase, update.Data.IsEnabled, update.Data.RateToBase)
                    : new CurrencyState(x.CurrencyCode ?? string.Empty, x.IsBase, x.IsEnabled, x.RateToBase))
                .Concat(request.CreateItems.Select(x => new CurrencyState(x.Data.CurrencyCode, x.Data.IsBase, x.Data.IsEnabled, x.Data.RateToBase)))
                .ToArray();
            var finalError = ValidateFinalState(finalState);
            if (finalError is not null) return CurrencyBatchPreparation.Fail(finalError);

            var changedOrDeletedCodes = request.DeleteItems.Select(x => existing[x.Uid].CurrencyCode ?? string.Empty)
                .Concat(request.UpdateItems.Where(x => !string.Equals(existing[x.Uid].CurrencyCode, x.Data.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                    .Select(x => existing[x.Uid].CurrencyCode ?? string.Empty))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var exchangeReferences = await exchangeRepository.GetReferencedCurrencyCodesAsync(changedOrDeletedCodes);
            var businessReferences = await repository.GetBusinessReferencedCurrencyCodesAsync(changedOrDeletedCodes);
            var referenced = exchangeReferences.Concat(businessReferences).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (referenced.Length > 0)
                return CurrencyBatchPreparation.Fail("以下货币仍被汇率或业务数据引用，不能改编码或删除：" + string.Join(", ", referenced));

            return CurrencyBatchPreparation.Ok(existing);
        }

        internal static string? ValidateFinalState(IReadOnlyList<CurrencyState> currencies)
        {
            var duplicate = currencies.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null) return $"货币代码不能重复：{duplicate.Key}。";
            if (currencies.Any(x => string.IsNullOrWhiteSpace(x.Code))) return "货币代码不能为空。";
            var invalidRate = currencies.FirstOrDefault(x => !x.IsBase && x.RateToBase <= 0);
            if (invalidRate is not null) return $"非本位币汇率必须大于 0：{invalidRate.Code}。";
            if (currencies.Count == 0) return null;
            var bases = currencies.Where(x => x.IsBase).ToArray();
            if (bases.Length != 1) return "有效货币中必须恰好存在一个本位币。";
            if (!bases[0].IsEnabled) return "本位币必须处于启用状态。";
            if (bases[0].RateToBase != 1m) return "本位币对本位币汇率必须为 1。";
            return null;
        }

        internal static string? NormalizeAndValidate(SysDicCurrencyDto? dto, int? rowIndex, string operation)
        {
            var row = SystemBatchSaveSupport.FormatRow(rowIndex);
            if (dto is null) return $"第 {row} 行：{operation}数据不能为空。";
            if (string.IsNullOrWhiteSpace(dto.CurrencyCode)) return $"第 {row} 行：货币代码不能为空。";
            if (string.IsNullOrWhiteSpace(dto.CurrencyNameZh)) return $"第 {row} 行：货币中文名称不能为空。";
            if (dto.Precision is < 0 or > 6) return $"第 {row} 行：精度必须在 0 到 6 之间。";
            if (!dto.IsBase && dto.RateToBase <= 0) return $"第 {row} 行：非本位币汇率必须大于 0。";
            if (dto.IsBase && dto.RateToBase != 1m) return $"第 {row} 行：本位币汇率必须为 1。";
            if (dto.IsBase && !dto.IsEnabled) return $"第 {row} 行：本位币必须启用。";
            dto.CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant();
            dto.CurrencyNameZh = dto.CurrencyNameZh.Trim();
            dto.CurrencyNameEn = dto.CurrencyNameEn?.Trim();
            dto.CurrencySymbol = dto.CurrencySymbol?.Trim();
            dto.Remark = dto.Remark?.Trim();
            return null;
        }

        internal sealed record CurrencyState(string Code, bool IsBase, bool IsEnabled, decimal RateToBase);

        private sealed record CurrencyBatchPreparation(string? Error, Dictionary<Guid, SysDicCurrency> Existing)
        {
            public static CurrencyBatchPreparation Fail(string error) => new(error, []);
            public static CurrencyBatchPreparation Ok(Dictionary<Guid, SysDicCurrency> existing) => new(null, existing);
        }
    }
}
