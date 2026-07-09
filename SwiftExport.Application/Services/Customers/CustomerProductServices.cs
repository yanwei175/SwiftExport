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
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using System.Text;
namespace SwiftExport.Application.Services.Customers;

/// <summary>
/// 客户产品服务。
/// 因 CustomerProduct 实体与 CustomerProductDto 不对称（DTO 含联表字段），
/// 不继承 BaseService，写操作手动映射实体字段，读操作走仓储联表查询直接返回 DTO。
/// </summary>
public class CustomerProductServices(
    ICustomerRepository customerRepository,
    ICustomerProductRepository repository,
    ServiceBaseContext context)
    : BaseService<CustomerProduct, CustomerProductDto>(repository, context), ICustomerProductService
{
    private readonly ILogger? _loggerInstance;

    /// <summary>按 ID 联表查询单条（含 CustomerCode / DescriptionEn / CategoryName 等）</summary>
    public override async Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid id)
    {
        var dto = await repository.GetDtoByIdAsync(id);
        if (dto == null)
        {
            Logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的客户产品", id);
            return ServiceResult<CustomerProductDto>.Fail("未找到数据");
        }
        return ServiceResult<CustomerProductDto>.Ok(dto);
    }

    /// <summary>按客户料号 + 客户 ID 精确查询（调用仓储联表版本确保字段完整）</summary>
    public async Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, Guid customerId)
    {
        // 先拿实体拿到 Uid，再走联表查询保证 DTO 字段完整
        var entity = await repository.GetByCustomerSkuAsync(customerSku, customerId);
        if (entity == null)
        {
            Logger.LogWarning("查询失败：客户 {CustomerId} 下未找到料号 {CustomerSku}", customerId, customerSku);
            return ServiceResult<CustomerProductDto>.Fail($"未找到客户产品型号为 {customerSku} 的产品");
        }

        var dto = await repository.GetDtoByIdAsync(entity.Uid);
        return ServiceResult<CustomerProductDto>.Ok(dto!);
    }

    /// <summary>联表分页查询</summary>
    public async Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest request)
    {
        try
        {
            var result = await repository.GetPagedAsync(request);
            if (result == null)
                return ServiceResult<PagedResponseBase<CustomerProductDto>>.Fail("未找到数据");
            return ServiceResult<PagedResponseBase<CustomerProductDto>>.Ok(result);

        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResponseBase<CustomerProductDto>>.Fail(ex.Message);
        }
    }



    /// <summary>获取所有有产品记录的客户列表</summary>
    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync()
    {
        var ids = await repository.GetAllCustomerIdListAsync();
        if (ids == null || ids.Count == 0)
            return ServiceResult<IReadOnlyList<CustomerDto>>.Fail("暂无客户产品记录");

        var list = await customerRepository.GetByIdListAsync(ids.Select(x => Guid.Empty));
        if (list == null || list.Count == 0)
            return ServiceResult<IReadOnlyList<CustomerDto>>.Fail("未找到对应客户");

        return ServiceResult<IReadOnlyList<CustomerDto>>.Ok(list.Adapt<IReadOnlyList<CustomerDto>>());
    }


    public async Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(CustomerProductBatchSaveRequest batchReq)
    {
        if (batchReq == null)
        {
            return ServiceResult<CustomerProductBatchSaveResponse>.Fail("请求不能为空。");
        }

        if (!batchReq.HasChanges)
        {
            return ServiceResult<CustomerProductBatchSaveResponse>.Fail("没有需要保存的数据。");
        }

        var validationError = await ValidateBatchSaveAsync(batchReq);

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return ServiceResult<CustomerProductBatchSaveResponse>.Fail(validationError);
        }

        var response = new CustomerProductBatchSaveResponse
        {
            TotalCount = batchReq.TotalCount
        };


        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            foreach (var item in batchReq.CreateItems)
            {
                var entity = item.Data.Adapt<CustomerProduct>();

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
                var entity = item.Data.Adapt<CustomerProduct>();

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
            return ServiceResult<CustomerProductBatchSaveResponse>.Ok(response, $"批量保存完成，成功 {response.SuccessCount} 条。");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<CustomerProductBatchSaveResponse>.Fail(
                        $"批量保存失败，所有操作已回滚。原因：{ex.Message}");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    private async Task<string?> ValidateBatchSaveAsync(CustomerProductBatchSaveRequest batchReq)
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
    protected override async Task<string?> ValidateBeforeCreateAsync(List<CreateItemRequest<CustomerProductDto>> createItems)
    {
        var cSkuList = new List<string>();
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

            if (string.IsNullOrWhiteSpace(item.Data.CustomerSku))
            {
                return $"第 {item.RowIndex} 行：客户产品编号不能为空。";
            }

            cSkuList.Add(item.Data.CustomerSku.Trim());
        }

        var duplicateInRequest = cSkuList
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateInRequest.Count > 0)
        {
            return "新增失败：本次提交中存在重复客户代码：" +
                   string.Join(", ", duplicateInRequest);
        }
        var customerUid = createItems.FirstOrDefault()?.Data?.CustomerId ?? Guid.Empty;

        var existedCodes = await repository.GetExistingCustomerSkuListAsync(cSkuList, customerUid);

        if (existedCodes.Count > 0)
        {
            return "新增失败：客户代码在数据库中已存在：" +
                   string.Join(", ", existedCodes);
        }

        return null;
    }
    protected override async Task<string?> ValidateBeforeUpdateAsync(List<UpdateItemRequest<CustomerProductDto>> updateItems)
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

            if (string.IsNullOrWhiteSpace(item.Data.CustomerSku))
            {
                return $"第 {item.RowIndex} 行：客户产品编号不能为空。";
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
            return "删除失败：包含不存在的 Id：" +
                   string.Join(", ", notFoundUids);
        }

        return null;
    }
}
