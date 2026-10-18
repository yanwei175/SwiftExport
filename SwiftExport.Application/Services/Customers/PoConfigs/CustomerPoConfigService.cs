using SwiftExport.Core.Interfaces.Systems.Currencies;
using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Customers.PoConfigs;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using System.Data;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Data;
namespace SwiftExport.Application.Services.Customers.PoConfigs;

/// <summary>编排客户采购默认配置查询、组合校验与原子批量保存。</summary>
public partial class CustomerPoConfigService(
    ICustomerPoConfigRepository repository,
    ISysDicCurrencyRepository currencyRepository,
    ServiceBaseContext context)
    : BaseService<CustomerPoConfig, CustomerPoConfigDto>(repository, context), ICustomerPoConfigService
{
    private readonly ILogger<CustomerPoConfigService> _logger = context.LoggerFactory.CreateLogger<CustomerPoConfigService>();

    #region 显式基础读写

    /// <summary>校验主数据标识后，返回指定客户与供应商的完整只读配置。</summary>
    public async Task<ServiceResult<CustomerPoConfigDto>> GetByPartiesAsync(
        Guid customerId,
        Guid supplierId,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
            return ServiceResult<CustomerPoConfigDto>.Fail("客户 Id 不能为空。");
        if (supplierId == Guid.Empty)
            return ServiceResult<CustomerPoConfigDto>.Fail("供应商 Id 不能为空。");

        try
        {
            var dto = await repository.GetByPartiesAsync(customerId, supplierId, cancellationToken);
            return dto == null
                ? ServiceResult<CustomerPoConfigDto>.Fail("未找到该客户与供应商的采购默认配置。")
                : ServiceResult<CustomerPoConfigDto>.Ok(dto);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按客户和供应商查询采购默认配置失败。CustomerId={CustomerId}, SupplierId={SupplierId}", customerId, supplierId);
            return ServiceResult<CustomerPoConfigDto>.Fail(ServiceErrorCodes.Unexpected, "查询客户采购默认配置失败。");
        }
    }

    /// <summary>兼容既有编码消费者读取全部未删除配置，精确身份读取使用专用端点。</summary>
    public async Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Ok(entities.Adapt<IReadOnlyList<CustomerPoConfigDto>>())
                : ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Fail(ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    /// <summary>按双方身份与关键词同时分页，展示编码来自当前主数据。</summary>
    public async Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>> GetPagedAsync(CustomerPoConfigSearchRequest request)
    {
        try
        {
            var result = await repository.GetPagedAsync(request);
            if (result == null)
                return ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Fail("未找到数据");
            return ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Ok(result);

        }
        catch (Exception ex)
        {
            // 技术异常只写服务端日志，响应不携带 SQL、连接或堆栈细节。
            Logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Fail(ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    #endregion

    /// <summary>单条新增也进入身份解析、ID 唯一性及同一事务内核，不依赖基类的编码校验。</summary>
    public override async Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<CustomerPoConfigDto> item)
        => SingleResult(await BatchSaveAsync(new() { CreateItems = [item] }));

    /// <summary>单条更新采用外层身份和版本，与批量更新共享主数据解析及并发保护。</summary>
    public override async Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<CustomerPoConfigDto> item)
        => SingleResult(await BatchSaveAsync(new() { UpdateItems = [item] }));

    /// <summary>删除采用相同事务边界及乐观版本，不为历史无效主数据强制重新解析身份。</summary>
    public override async Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item)
        => SingleResult(await BatchSaveAsync(new() { DeleteItems = [item] }));

    /// <summary>成功只返回已提交的单行回执；失败保留统一内核错误，不暴露已回滚结果。</summary>
    private static ServiceResult<SaveItemResult> SingleResult(ServiceResult<CustomerPoConfigBatchSaveResponse> result)
        => result.Success ? ServiceResult<SaveItemResult>.Ok(result.Data.Items.Single(), result.Message)
            : new() { Success = false, Error = result.Error, Message = result.Message };

    /// <summary>在自有低隔离事务内串行配置写入；先解析当前双方身份，再校验唯一性及原子写入，不自动重试。</summary>
    public async Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSaveAsync(CustomerPoConfigBatchSaveRequest batchReq)
    {
        if (batchReq is null) return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail("请求不能为空。");
        batchReq.CreateItems ??= [];
        batchReq.UpdateItems ??= [];
        batchReq.DeleteItems ??= [];
        if (batchReq.CreateItems.Any(item => item?.Data is null)
            || batchReq.UpdateItems.Any(item => item?.Data is null) || batchReq.DeleteItems.Any(item => item is null))
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail("保存明细及数据不能为空。");
        if (!batchReq.HasChanges) return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail("没有需要保存的数据。");
        if (_session.Transaction is not null)
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail("当前请求已有事务，不能重复开始配置保存。");

        IDbTransaction? transaction = null;
        var committed = false;
        try
        {
            transaction = _session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            _session.Transaction = transaction;
            // 与采购默认配置锁定读取采用同一顺序；先锁范围再读取行，兼顾缺失组合和更新并发。
            await repository.LockForWriteAsync();
            var (creates, updates) = await PrepareSaveAsync(batchReq);
            var response = new CustomerPoConfigBatchSaveResponse { TotalCount = batchReq.TotalCount };
            for (var i = 0; i < creates.Length; i++)
            {
                var write = await _repository.AddAsync(creates[i]);
                EnsureWritten(write);
                response.Items.Add(SaveItemResultFactory.FromCreate(batchReq.CreateItems[i], write));
            }
            for (var i = 0; i < updates.Length; i++)
            {
                var write = await _repository.UpdateAsync(updates[i]);
                EnsureWritten(write);
                response.Items.Add(SaveItemResultFactory.FromUpdate(batchReq.UpdateItems[i], write));
            }
            foreach (var item in batchReq.DeleteItems)
            {
                var write = await _repository.DeleteAsync(item.Uid, item.Version, item.IsSoftDelete);
                EnsureWritten(write);
                response.Items.Add(SaveItemResultFactory.FromDelete(item, write));
            }
            try { transaction.Commit(); committed = true; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "客户采购配置提交失败，保留明确终止与结果未知的区别。");
                var kind = _session.Errors.Classify(exception);
                return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail(
                    kind == DatabaseFailureKind.UniqueConflict ? ServiceErrorCodes.BusinessBlocked
                    : kind == DatabaseFailureKind.SerializationFailure ? ServiceErrorCodes.ServiceUnavailable
                    : ServiceErrorCodes.OutcomeUnknown,
                    kind == DatabaseFailureKind.UniqueConflict ? "客户与供应商配置已存在，请刷新后重试。"
                    : kind == DatabaseFailureKind.SerializationFailure ? "并发配置变化导致本次未提交，请刷新后重试。"
                    : "配置提交结果无法确认，请刷新核实，不能直接重试。");
            }
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Ok(response, $"保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (ConfigSaveException exception)
        {
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail(exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "客户采购配置保存失败。");
            var kind = _session.Errors.Classify(exception);
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail(
                kind == DatabaseFailureKind.UniqueConflict ? ServiceErrorCodes.BusinessBlocked
                : ServiceErrorCodes.Unexpected,
                kind == DatabaseFailureKind.UniqueConflict
                    ? "客户与供应商配置已存在，请刷新后重试。" : "保存客户采购默认配置失败。");
        }
        finally
        {
            if (transaction is not null)
            {
                if (!committed)
                    try { transaction.Rollback(); } catch (Exception exception) { _logger.LogWarning(exception, "回滚客户采购配置失败。"); }
                try { transaction.Dispose(); } catch (Exception exception) { _logger.LogWarning(exception, "释放客户采购配置事务失败。"); }
                _session.Transaction = null;
            }
        }
    }

    /// <summary>解析合法主数据并回填当前编码，所有写入口只按最终双方 ID 组合判断重复，旧编码不能绕过。</summary>
    private async Task<(CustomerPoConfig[] Creates, CustomerPoConfig[] Updates)> PrepareSaveAsync(CustomerPoConfigBatchSaveRequest request)
    {
        var operationIds = request.UpdateItems.Select(item => item.Uid).Concat(request.DeleteItems.Select(item => item.Uid)).ToArray();
        if (operationIds.Any(id => id == Guid.Empty) || operationIds.Distinct().Count() != operationIds.Length
            || request.UpdateItems.Any(item => item.Version <= 0) || request.DeleteItems.Any(item => item.Version <= 0))
            throw new ConfigSaveException("配置身份或版本无效，同一配置不能在批次中重复操作。");
        var existing = (await _repository.GetByIdListAsync(operationIds, withUpdLock: true)).Where(row => !row.IsDeleted).ToDictionary(row => row.Uid);
        foreach (var item in request.UpdateItems.Select(item => (item.Uid, item.Version))
            .Concat(request.DeleteItems.Select(item => (item.Uid, item.Version))))
        {
            if (!existing.TryGetValue(item.Uid, out var old))
                throw new ConfigSaveException("配置不存在或已删除。", ServiceErrorCodes.NotFound);
            if (old.Version != item.Version)
                throw new ConfigSaveException("配置已被修改，请刷新后重试。", ServiceErrorCodes.VersionConflict);
        }
        var creates = new List<CustomerPoConfig>();
        foreach (var item in request.CreateItems)
        {
            var entity = await ResolveAsync(item.Data);
            entity.Uid = Guid.Empty;
            entity.IsDeleted = false;
            // 新建身份、版本和审计由服务器建立，不能沿用客户端附带的系统字段。
            entity.Version = 1;
            entity.CreatedBy = null; entity.CreatedDate = null;
            entity.UpdatedBy = null; entity.UpdatedDate = null;
            creates.Add(entity);
        }
        var updates = new List<CustomerPoConfig>();
        foreach (var item in request.UpdateItems)
        {
            var entity = await ResolveAsync(item.Data);
            entity.Uid = item.Uid;
            entity.Version = item.Version;
            // Edit 不能伪装 Delete；创建审计来自锁定旧记录，更新审计仍由仓储重新打戳。
            entity.IsDeleted = existing[item.Uid].IsDeleted;
            entity.CreatedBy = existing[item.Uid].CreatedBy;
            entity.CreatedDate = existing[item.Uid].CreatedDate;
            entity.UpdatedBy = existing[item.Uid].UpdatedBy;
            entity.UpdatedDate = existing[item.Uid].UpdatedDate;
            updates.Add(entity);
        }
        var active = creates.Concat(updates).Where(row => !row.IsDeleted).ToArray();
        if (active.GroupBy(row => (row.CustomerId, row.SupplierId)).Any(group => group.Count() > 1))
            throw new ConfigSaveException("本次提交中存在重复客户与供应商身份组合。");
        foreach (var entity in active)
        {
            var occupied = await repository.GetByPartiesAsync(entity.CustomerId!.Value, entity.SupplierId!.Value);
            if (occupied is not null && occupied.Uid != entity.Uid)
                throw new ConfigSaveException("该客户与供应商身份组合已有采购默认配置。");
        }
        var currencyError = await ValidateCurrenciesAsync(request.CreateItems.Select(item => item.Data).Concat(request.UpdateItems.Select(item => item.Data)));
        if (currencyError is not null) throw new ConfigSaveException(currencyError);
        return (creates.ToArray(), updates.ToArray());
    }

    /// <summary>身份优先、编码兼容；双方必须存在且未删除，停用主数据可保留配置，失败不写入。</summary>
    private async Task<CustomerPoConfig> ResolveAsync(CustomerPoConfigDto data)
    {
        NormalizeCodes(data);
        var entity = data.Adapt<CustomerPoConfig>();
        if (!await repository.PopulateRelationIdsAsync(entity))
            throw new ConfigSaveException("客户或供应商无效；请提供有效身份，缺少身份时编码必须唯一。");
        data.CustomerId = entity.CustomerId;
        data.CustomerCode = entity.CustomerCode!;
        data.SupplierId = entity.SupplierId;
        data.SupplierCode = entity.SupplierCode!;
        var error = ValidateFieldLengths(data);
        if (error is not null) throw new ConfigSaveException(error);
        return entity;
    }

    /// <summary>任何一行失败都终止整个批次；版本冲突保持明确分类。</summary>
    private static void EnsureWritten(RepositoryWriteResult write)
    {
        if (!write.Success)
            throw new ConfigSaveException(write.Message ?? "配置写入失败。",
                write.ErrorCode == "CONCURRENCY_CONFLICT" ? ServiceErrorCodes.VersionConflict
                    : ServiceErrorCodes.WriteFailed);
    }

    /// <summary>仅传递本用例已确认的业务阻断，技术异常仍由外层统一日志和包装。</summary>
    private sealed class ConfigSaveException(string message, string code = ServiceErrorCodes.BusinessBlocked) : Exception(message)
    {
        public string Code { get; } = code;
    }
    /// <summary>统一清理业务编码和新增可选字段，确保校验与最终写入采用同一值。</summary>
    private static void NormalizeCodes(CustomerPoConfigDto data)
    {
        data.CustomerCode = data.CustomerCode?.Trim();
        data.SupplierCode = data.SupplierCode?.Trim();
        data.Currency = string.IsNullOrWhiteSpace(data.Currency) ? null : data.Currency.Trim();
        data.DiscountRemark = string.IsNullOrWhiteSpace(data.DiscountRemark) ? null : data.DiscountRemark.Trim();
    }

    /// <summary>按数据库列宽验证文本，避免把截断异常推迟到事务写入阶段。</summary>
    internal static string? ValidateFieldLengths(CustomerPoConfigDto data)
    {
        if (data.CustomerCode?.Length > 255) return "客户编码不能超过 255 个字符。";
        if (data.SupplierCode?.Length > 50) return "供应商编码不能超过 50 个字符。";
        if (data.Currency?.Length > 10) return "币种代码不能超过 10 个字符。";
        if (data.DefaultDiscountRate is < 0m) return "默认折扣率不能小于 0。";
        if (data.DefaultDiscountRate > 999.999999m) return "默认折扣率不能大于 999.999999。";
        if (data.DefaultDiscountRate.HasValue && decimal.Round(data.DefaultDiscountRate.Value, 6) != data.DefaultDiscountRate.Value)
            return "默认折扣率最多保留 6 位小数。";
        if (data.DiscountRemark?.Length > 500) return "折扣备注不能超过 500 个字符。";
        var fiftyCharacterFields = new[]
        {
            data.InletHoseType, data.AeratorType, data.PalletType, data.BoxType,
            data.LabelType, data.IMType, data.CartonType, data.LogoType
        };
        if (fiftyCharacterFields.Any(value => value?.Length > 50))
            return "进水管、起泡器、托盘、包装盒、标签、说明书、外箱和标志不能超过 50 个字符。";
        if (data.SalesPerson?.Length > 10) return "销售人员不能超过 10 个字符。";
        if (data.Purchaser?.Length > 10) return "采购人员不能超过 10 个字符。";
        return null;
    }

    /// <summary>校验所有非空币种代码均存在于未删除币种主数据；停用币种仍允许保留历史引用。</summary>
    private async Task<string?> ValidateCurrenciesAsync(IEnumerable<CustomerPoConfigDto> items)
    {
        var codes = items
            .Select(item => item.Currency)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (codes.Length == 0) return null;

        var currencies = await currencyRepository.GetByCurrencyCodesAsync(codes);
        var existingCodes = currencies
            .Where(currency => !string.IsNullOrWhiteSpace(currency.CurrencyCode))
            .Select(currency => currency.CurrencyCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingCodes = codes.Where(code => !existingCodes.Contains(code)).ToArray();
        return missingCodes.Length == 0
            ? null
            : "以下币种代码不存在：" + string.Join(", ", missingCodes);
    }


}
