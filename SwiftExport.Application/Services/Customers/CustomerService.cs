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
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using System.Text;
namespace SwiftExport.Application.Services.Customers;

public partial class CustomerService(
    ICustomerRepository repository,
    ICustomerProductRepository customerProductRepository,
    ServiceBaseContext context) : BaseService<Customer, CustomerDto>(repository, context), ICustomerService
{

    private readonly ILogger<CustomerService> _logger = context.LoggerFactory.CreateLogger<CustomerService>();

    public async Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync(CustomerSearchRequest request)
    {
        try
        {
            var result = await repository.GetPagedAsync(request);
            if (result == null)
                return ServiceResult<PagedResponseBase<CustomerDto>>.Fail("查询失败：未能获取分页数据");
            return ServiceResult<PagedResponseBase<CustomerDto>>.Ok(result);
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<CustomerDto>>.Fail($"查询失败：{ex.Message}");
        }
    }

    #region 批量保存和验证
    public async Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync(CustomerBatchSaveRequest batchReq)
    {
        if (batchReq == null)
        {
            return ServiceResult<CustomerBatchSaveResponse>.Fail("请求不能为空。");
        }

        if (!batchReq.HasChanges)
        {
            return ServiceResult<CustomerBatchSaveResponse>.Fail("没有需要保存的数据。");
        }

        var validationError = await ValidateBatchSaveAsync(batchReq);

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return ServiceResult<CustomerBatchSaveResponse>.Fail(validationError);
        }

        var response = new CustomerBatchSaveResponse
        {
            TotalCount = batchReq.TotalCount
        };


        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var item in batchReq.CreateItems)
            {
                var entity = item.Data.Adapt<Customer>();

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
                var entity = item.Data.Adapt<Customer>();

                entity.Uid = item.Uid;
                entity.Version = item.Version;

                var writeResult = await _repository.UpdateAsync(entity);

                response.Items.Add(
                    SaveItemResultFactory.FromUpdate(item, writeResult));

                if (!writeResult.Success)
                {
                    throw new InvalidOperationException(
                        writeResult.Message ?? "更新客户失败。");
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
                        writeResult.Message ?? "删除客户失败。");
                }
            }


            _session.Transaction.Commit();
            return ServiceResult<CustomerBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<CustomerBatchSaveResponse>.Fail(
                        $"批量保存失败，所有操作已回滚。原因：{ex.Message}");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    private async Task<string?> ValidateBatchSaveAsync(CustomerBatchSaveRequest batchReq)
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
    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<CustomerDto>> createItems)
    {
        var codes = new List<string>();

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

            if (string.IsNullOrWhiteSpace(item.Data.CustomerName))
            {
                return $"第 {item.RowIndex} 行：客户名称不能为空。";
            }

            codes.Add(item.Data.CustomerCode.Trim());
        }

        var duplicateInRequest = codes
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateInRequest.Count > 0)
        {
            return "新增失败：本次提交中存在重复客户代码：" +
                   string.Join(", ", duplicateInRequest);
        }

        var existedCodes = await repository.GetExistingCustomerCodesAsync(codes);

        if (existedCodes.Count > 0)
        {
            return "新增失败：客户代码在数据库中已存在：" +
                   string.Join(", ", existedCodes);
        }

        return null;
    }
    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<CustomerDto>> updateItems)
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
                return $"第 {item.RowIndex} 行：客户代码不能为空。";
            }

            if (string.IsNullOrWhiteSpace(item.Data.CustomerName))
            {
                return $"第 {item.RowIndex} 行：客户名称不能为空。";
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
            .Select(x => new
            {
                x.Uid,
                CustomerCode = x.Data.CustomerCode.Trim()
            })
            .ToList();

        var duplicateCodesInRequest = codePairs
            .GroupBy(x => x.CustomerCode, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Uid).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateCodesInRequest.Count > 0)
        {
            return "修改失败：本次提交中存在重复客户代码：" +
                   string.Join(", ", duplicateCodesInRequest);
        }

        var occupiedCodes = await repository.GetCustomerCodesUsedByOtherCustomersAsync(
            codePairs.Select(x => (x.Uid, x.CustomerCode)));

        if (occupiedCodes.Count > 0)
        {
            return "修改失败：以下客户代码已被其他客户使用：" +
                   string.Join(", ", occupiedCodes);
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
            return "删除失败：包含不存在的客户 Id：" +
                   string.Join(", ", notFoundUids);
        }

        var referencedCustomerIds = await customerProductRepository.GetCustomerIdsWithProductsAsync(uids);
        if (referencedCustomerIds.Count > 0)
        {
            return "删除失败：以下客户仍有关联客户产品，请先删除客户产品：" +
                   string.Join(", ", referencedCustomerIds);
        }

        return null;
    }
    #endregion

}
