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
using System.Text;
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
            return ServiceResult<CustomerPoConfigDto>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "查询客户采购默认配置失败。");
        }
    }

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
            return ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

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
            return ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    #endregion

    #region 批量保存和验证
    public async Task<ServiceResult<CustomerPoConfigBatchSaveResponse>> BatchSaveAsync(CustomerPoConfigBatchSaveRequest batchReq)
    {
        if (batchReq == null)
        {
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail("请求不能为空。");
        }

        if (!batchReq.HasChanges)
        {
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail("没有需要保存的数据。");
        }

        var validationError = await ValidateBatchSaveAsync(batchReq);

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail(validationError);
        }

        var response = new CustomerPoConfigBatchSaveResponse
        {
            TotalCount = batchReq.TotalCount
        };


        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var item in batchReq.CreateItems)
            {
                var entity = item.Data.Adapt<CustomerPoConfig>();

                if (!await repository.PopulateRelationIdsAsync(entity))
                    throw CreateRelationResolutionException(entity);

                var writeResult = await _repository.AddAsync(entity);

                response.Items.Add(
                    SaveItemResultFactory.FromCreate(item, writeResult));

                if (!writeResult.Success)
                {
                    throw new InvalidOperationException(
                        writeResult.Message ?? "新增客户失败。");
                }
            }

            foreach (var item in batchReq.UpdateItems)
            {
                var entity = item.Data.Adapt<CustomerPoConfig>();

                if (!await repository.PopulateRelationIdsAsync(entity))
                    throw CreateRelationResolutionException(entity);

                entity.Uid = item.Uid;
                entity.Version = item.Version;

                var writeResult = await _repository.UpdateAsync(entity);

                response.Items.Add(
                    SaveItemResultFactory.FromUpdate(item, writeResult));

                if (!writeResult.Success)
                {
                    throw new InvalidOperationException(
                        writeResult.Message ?? "更新失败。");
                }
            }

            foreach (var item in batchReq.DeleteItems)
            {
                var writeResult = await _repository.DeleteAsync(
                    item.Uid,
                    item.Version,
                    item.IsSoftDelete);

                response.Items.Add(
                    SaveItemResultFactory.FromDelete(item, writeResult));

                if (!writeResult.Success)
                {
                    throw new InvalidOperationException(
                        writeResult.Message ?? "删除失败。");
                }
            }


            _session.Transaction.Commit();
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<CustomerPoConfigBatchSaveResponse>.Fail(
                        $"批量保存失败，所有操作已回滚。原因：{ex.Message}");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    private async Task<string?> ValidateBatchSaveAsync(CustomerPoConfigBatchSaveRequest batchReq)
    {
        foreach (var item in batchReq.CreateItems)
        {
            if (item.Data != null) NormalizeCodes(item.Data);
        }
        foreach (var item in batchReq.UpdateItems)
        {
            if (item.Data != null) NormalizeCodes(item.Data);
        }

        var errorMsg = new StringBuilder();

        if (batchReq.CreateItems.Count > 0)
        {
            var error = await ValidateBeforeCreateAsync(batchReq.CreateItems);
            if (!string.IsNullOrWhiteSpace(error))
            {
                errorMsg.AppendLine(error);
            }
        }

        if (batchReq.UpdateItems.Count > 0)
        {
            var error = await ValidateBeforeUpdateAsync(batchReq.UpdateItems);
            if (!string.IsNullOrWhiteSpace(error))
            {
                errorMsg.AppendLine(error);
            }
        }

        if (batchReq.DeleteItems.Count > 0)
        {
            var error = await ValidateBeforeDeleteAsync(batchReq.DeleteItems);
            if (!string.IsNullOrWhiteSpace(error))
            {
                errorMsg.AppendLine(error);
            }
        }

        if (errorMsg.Length == 0)
        {
            var submittedPairs = batchReq.CreateItems
                .Where(item => item.Data != null)
                .Select(item => new PoConfigCheck
                {
                    CustomerCode = item.Data.CustomerCode,
                    SupplierCode = item.Data.SupplierCode
                })
                .Concat(batchReq.UpdateItems.Where(item => item.Data != null).Select(item => new PoConfigCheck
                {
                    Uid = item.Uid,
                    CustomerCode = item.Data.CustomerCode,
                    SupplierCode = item.Data.SupplierCode
                }))
                .ToList();

            var duplicatePairs = submittedPairs
                .GroupBy(item => $"{item.CustomerCode}|{item.SupplierCode}", StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key.Replace('|', '+'))
                .ToList();
            if (duplicatePairs.Count > 0)
            {
                errorMsg.AppendLine("保存失败：本次提交中存在重复客户+供应商：" + string.Join(", ", duplicatePairs));
            }
            else
            {
                var occupiedPairs = await repository.GetUsedByCustomerCodeAndSupplierCodeAsync(submittedPairs);
                if (occupiedPairs.Count > 0)
                    errorMsg.AppendLine("保存失败：以下客户+供应商组合已被其他记录使用：" + string.Join(", ", occupiedPairs));
            }
        }

        return errorMsg.Length > 0
            ? errorMsg.ToString().TrimEnd()
            : null;
    }
    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<CustomerPoConfigDto>> createItems)
    {
        var codes = new List<PoConfigCheck>();

        foreach (var item in createItems)
        {
            if (item.Data == null)
            {
                return $"第 {item.RowIndex} 行：新增数据不能为空。";
            }

            NormalizeCodes(item.Data);

            if (string.IsNullOrWhiteSpace(item.Data.CustomerCode))
            {
                return $"第 {item.RowIndex} 行：客户代码不能为空。";
            }

            if (string.IsNullOrWhiteSpace(item.Data.SupplierCode))
            {
                return $"第 {item.RowIndex} 行：供应商代码名称不能为空。";
            }

            var lengthError = ValidateFieldLengths(item.Data);
            if (!string.IsNullOrWhiteSpace(lengthError)) return $"第 {item.RowIndex} 行：{lengthError}";

            codes.Add(new PoConfigCheck
            {
                CustomerCode = item.Data.CustomerCode.Trim(),
                SupplierCode = item.Data.SupplierCode.Trim()
            });
        }

        var currencyError = await ValidateCurrenciesAsync(createItems.Select(item => item.Data));
        if (!string.IsNullOrWhiteSpace(currencyError)) return currencyError;

        var duplicatePairsInRequest = codes
            .GroupBy(
                x => new
                {
                    CustomerCode = x.CustomerCode!.ToUpperInvariant(),
                    SupplierCode = x.SupplierCode!.ToUpperInvariant()
                })
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.CustomerCode} + {g.Key.SupplierCode}")
            .ToList();

        if (duplicatePairsInRequest.Count > 0)
        {
            return "新增失败：本次提交中存在重复客户代码+供应商代码：" +
                   string.Join(", ", duplicatePairsInRequest);
        }

        var existedCodes = await repository.GetExistingCustomerCodesAsync(codes);

        if (existedCodes.Count > 0)
        {
            return "新增失败：客户代码+供应商代码在数据库中已存在：" +
                   string.Join(", ", existedCodes);
        }

        return null;
    }
    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<CustomerPoConfigDto>> updateItems)
    {
        foreach (var item in updateItems)
        {
            if (item.Uid == Guid.Empty)
            {
                return $"第 {item.RowIndex} 行：修改失败，Uid 不能为空。";
            }

            if (item.Version <= 0)
            {
                return $"第 {item.RowIndex} 行：修改失败，Version 无效。";
            }

            if (item.Data == null)
            {
                return $"第 {item.RowIndex} 行：修改数据不能为空。";
            }

            NormalizeCodes(item.Data);

            if (string.IsNullOrWhiteSpace(item.Data.CustomerCode))
            {
                return $"第 {item.RowIndex} 行：客户不能为空。";
            }

            if (string.IsNullOrWhiteSpace(item.Data.SupplierCode))
            {
                return $"第 {item.RowIndex} 行：供应商不能为空。";
            }

            var lengthError = ValidateFieldLengths(item.Data);
            if (!string.IsNullOrWhiteSpace(lengthError)) return $"第 {item.RowIndex} 行：{lengthError}";
        }

        var currencyError = await ValidateCurrenciesAsync(updateItems.Select(item => item.Data));
        if (!string.IsNullOrWhiteSpace(currencyError)) return currencyError;

        var uids = updateItems
            .Select(x => x.Uid)
            .Distinct()
            .ToList();

        var existingUids = await _repository.GetExistingIdListAsync(uids);

        var notFoundUids = uids
            .Except(existingUids)
            .ToList();

        if (notFoundUids.Count > 0)
        {
            return "修改失败：包含不存在的客户 Id：" +
                   string.Join(", ", notFoundUids);
        }

        var codePairs = updateItems
            .Select(x => new PoConfigCheck
            {
                Uid = x.Uid,
                CustomerCode = x.Data.CustomerCode.Trim(),
                SupplierCode = x.Data.SupplierCode.Trim()
            })
            .ToList();

        var duplicatePairsInRequest = codePairs
            .GroupBy(
                x => new
                {
                    CustomerCode = x.CustomerCode!.ToUpperInvariant(),
                    SupplierCode = x.SupplierCode!.ToUpperInvariant()
                })
            .Where(g => g.Select(x => x.Uid).Distinct().Count() > 1)
            .Select(g => $"{g.Key.CustomerCode} + {g.Key.SupplierCode}")
            .ToList();

        if (duplicatePairsInRequest.Count > 0)
        {
            return "修改失败：本次提交中存在重复客户+供应商：" +
                   string.Join(", ", duplicatePairsInRequest);
        }

        var occupiedPairs = await repository.GetUsedByCustomerCodeAndSupplierCodeAsync(codePairs);

        if (occupiedPairs.Count > 0)
        {
            return "修改失败：以下客户+供应商组合已被其他记录使用：" +
                   string.Join(", ", occupiedPairs);
        }

        return null;
    }
    protected override async Task<string?> ValidateBeforeDeleteAsync(List<DeleteItemRequest> deleteItems)
    {
        foreach (var item in deleteItems)
        {
            if (item.Uid == Guid.Empty)
            {
                return $"第 {item.RowIndex} 行：删除失败，Uid 不能为空。";
            }

            if (item.Version <= 0)
            {
                return $"第 {item.RowIndex} 行：删除失败，Version 无效。";
            }
        }

        var uids = deleteItems
            .Select(x => x.Uid)
            .Distinct()
            .ToList();

        var existingUids = await _repository.GetExistingIdListAsync(uids);

        var notFoundUids = uids
            .Except(existingUids)
            .ToList();

        if (notFoundUids.Count > 0)
        {
            return "删除失败：包含不存在的删除项目 Id：" +
                   string.Join(", ", notFoundUids);
        }

        return null;
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

    /// <summary>构造包含主数据标识和编码的关系解析错误，便于定位缺失、删除或重复主数据。</summary>
    private static InvalidOperationException CreateRelationResolutionException(CustomerPoConfig entity) => new(
        $"客户或供应商无法解析为有效 Uid。客户：{entity.CustomerCode ?? "（空）"} / {entity.CustomerId?.ToString() ?? "无 Id"}；" +
        $"供应商：{entity.SupplierCode ?? "（空）"} / {entity.SupplierId?.ToString() ?? "无 Id"}。请检查主数据是否存在、未删除；缺少 Id 时编码必须唯一。");
    #endregion

}
