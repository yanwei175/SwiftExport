using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
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
namespace SwiftExport.Application.Services.Customers;

public partial class CustomerPoConfigService(ICustomerPoConfigRepository repository, ServiceBaseContext context)
    : BaseService<CustomerPoConfig, CustomerPoConfigDto>(repository, context), ICustomerPoConfigService
{
    private readonly ILogger<CustomerPoConfigService> _logger = context.LoggerFactory.CreateLogger<CustomerPoConfigService>();

    #region 显式基础读写

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
            return ServiceResult<IReadOnlyList<CustomerPoConfigDto>>.Fail(ex.Message);
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
            return ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Fail(ex.Message);
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

            if (string.IsNullOrWhiteSpace(item.Data.CustomerCode))
            {
                return $"第 {item.RowIndex} 行：客户代码不能为空。";
            }

            if (string.IsNullOrWhiteSpace(item.Data.SupplierCode))
            {
                return $"第 {item.RowIndex} 行：供应商代码名称不能为空。";
            }

            codes.Add(new PoConfigCheck
            {
                CustomerCode = item.Data.CustomerCode.Trim(),
                SupplierCode = item.Data.SupplierCode.Trim()
            });
        }

        var duplicatePairsInRequest = codes
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

            if (string.IsNullOrWhiteSpace(item.Data.CustomerCode))
            {
                return $"第 {item.RowIndex} 行：客户不能为空。";
            }

            if (string.IsNullOrWhiteSpace(item.Data.SupplierCode))
            {
                return $"第 {item.RowIndex} 行：供应商不能为空。";
            }
        }

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
    #endregion

}
