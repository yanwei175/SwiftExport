using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Customers;

public class CustomerPoConfigService(ICustomerPoConfigRepository repository, DbSession session, ILogger<CustomerPoConfigService> logger)
    : BaseService<CustomerPoConfig, CustomerPoConfigDto>(repository, logger), ICustomerPoConfigService
{
    private readonly DbSession _session = session;

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestGeneric<CustomerPoConfigDto> batchReq)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "开始执行客户批量保存事务，计划新增: {AddCount}，修改: {UpdateCount}，删除: {DeleteCount}",
                batchReq.AddList?.Count ?? 0,
                batchReq.UpdateList?.Count ?? 0,
                batchReq.DeleteIdList?.Count ?? 0);
        }

        _session.Transaction = _session.Connection.BeginTransaction();
        int totalRowsAffected = 0;
        BatchResultDto batchResultDto = new();
        var createList = batchReq.AddList.Adapt<List<CustomerPoConfig>>();
        var updateList = batchReq.UpdateList.Adapt<List<CustomerPoConfig>>();

        try
        {
            List<string> WrongAdd = [];
            List<PoConfigCheck> NeedAdd = [];
            List<int> WrongUpdate = [];
            List<int> NeedUpdate = [];
            List<int> WrongDelete = [];
            List<int> NeedDelete = [];

            // 1. 检查新增的数据
            if (createList != null && createList.Count != 0)
            {
                var selfDuplicates = createList
                    .GroupBy(x => new { x.CustomerCode, x.SupplierCode })
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key.CustomerCode}|{g.Key.SupplierCode}")
                    .ToList();

                if (selfDuplicates.Count > 0)
                {
                    WrongAdd.AddRange(selfDuplicates);
                    // 如果自身都有重复，这批新增数据直接打回，不用去查数据库了
                    return ServiceResult<BatchResultDto>.Fail("批量保存失败：新增列表中存在重复的 客户+供应商 组合！");
                }

                foreach (var item in createList)
                {
                    if (string.IsNullOrWhiteSpace(item.CustomerCode))
                    {
                        if (_logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning("批量保存校验失败：存在客户代码为空的新增项");
                        }
                        return ServiceResult<BatchResultDto>.Fail("客户代码不能为空");
                    }
                    if (string.IsNullOrWhiteSpace(item.SupplierCode))
                    {
                        if (_logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning("批量保存校验失败：存在供应商代码为空的新增项");
                        }
                        return ServiceResult<BatchResultDto>.Fail("供应商代码不能为空");
                    }
                    NeedAdd.Add(new PoConfigCheck { CustomerCode = item.CustomerCode, SupplierCode = item.SupplierCode });
                }

                IReadOnlyList<PoConfigCheck> _nonUniquePoConfig = await repository.GetNonUniqueByPoConfigCheckList(NeedAdd);
                if (_nonUniquePoConfig.Count > 0)
                {
                    WrongAdd.AddRange(_nonUniquePoConfig.Select(x => $"{x.CustomerCode}|{x.SupplierCode}"));
                }

            }

            // 2. 检查更新的数据
            if (updateList != null && updateList.Count != 0)
            {
                foreach (var upItem in updateList)
                {
                    if (upItem.Id <= 0)
                        WrongUpdate.Add(upItem.Id);
                    NeedUpdate.Add(upItem.Id);
                }

                var existingIds = await repository.GetExistingIdListAsync(NeedUpdate);
                if (existingIds == null || !existingIds.Any())
                {
                    WrongUpdate.AddRange(NeedUpdate);
                }
                else
                {
                    var allidSet = new HashSet<int>(NeedUpdate);
                    var existingidSet = new HashSet<int>(existingIds);
                    var nonExistentIds = allidSet.Except(existingidSet).ToList();
                    if (nonExistentIds.Count != 0)
                        WrongUpdate.AddRange(nonExistentIds);
                }
            }

            // 3. 检查删除数据
            if (batchReq.DeleteIdList != null && batchReq.DeleteIdList.Count != 0)
            {
                foreach (var id in batchReq.DeleteIdList)
                {
                    if (id <= 0)
                    {
                        WrongDelete.Add(id);
                    }
                    NeedDelete.Add(id);
                }
            }

            // 如果有错误，则返回错误信息
            if (WrongAdd.Count != 0 || WrongUpdate.Count != 0 || WrongDelete.Count != 0)
            {
                var errorMessage = new StringBuilder();
                if (WrongAdd.Count != 0)
                    errorMessage.AppendLine($"新创建的在数据库中已存在，拒绝新增。重复代码: {string.Join(", ", WrongAdd)}");
                if (WrongUpdate.Count != 0)
                    errorMessage.AppendLine($"批量更新列表包含无效ID，事务已终止。ID: {string.Join(", ", WrongUpdate)}");
                if (WrongDelete.Count != 0)
                    errorMessage.AppendLine($"删除操作列表包含无效ID的实体。实体ID: {string.Join(", ", WrongDelete)}");

                var finalError = errorMessage.ToString();

                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("批量保存事务中断（数据校验不通过）：{ErrorMessage}", finalError);
                }

                return ServiceResult<BatchResultDto>.Fail(finalError);
            }

            // uow 开始写数据库
            // 新增
            if (createList != null)
            {
                batchResultDto.AddIdList = [.. (await repository.AddRangeAsync(createList))];
                totalRowsAffected += batchResultDto.AddIdList.Count;
            }

            // 更新
            if (updateList != null)
            {
                totalRowsAffected += await repository.UpdateRangeAsync(updateList);
                batchResultDto.UpdateIdList = NeedUpdate;
            }

            // 删除
            if (NeedDelete.Count != 0)
            {
                totalRowsAffected += await repository.DeleteRangeAsync(NeedDelete);
                batchResultDto.DeleteIdList = NeedDelete;
            }

            // uow 提交事务
            _session.Transaction.Commit();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("批量保存事务提交！共影响数据库 {TotalRowsAffected} 行。", totalRowsAffected);
            }

            return ServiceResult<BatchResultDto>.Ok(batchResultDto);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();

            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "执行批量保存时发生致命异常，数据库事务已安全回滚！");
            }

            throw;
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

}