using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
namespace SwiftExport.Application.Services.Products;

public partial class ProductColorService(IProductColorRepository repository, ServiceBaseContext context) : IProductColorService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<ProductColorService> _logger = context.LoggerFactory.CreateLogger<ProductColorService>();
    private readonly IProductColorRepository _repository = repository;

    #region 批量保存
    private async Task<string?> ValidateBeforeAddAsync(List<ProductColor> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.ColorCode))
                return "颜色代码不能为空";
            codes.Add(item.ColorCode);
        }

        var existedCodes = await _repository.GetExistingColorCodesAsync(codes);
        if (existedCodes.Any())
        {
            return $"以下颜色代码已存在，拒绝新增: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切正常，放行！
        return null;
    }
    public async Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new ProductColorBatchSaveResponse();

        var createList = batchReq.AddList?.Adapt<List<ProductColor>>();
        var updateList = batchReq.UpdateList?.Adapt<List<ProductColor>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (createList is { Count: > 0 })
            {
                var error = await ValidateBeforeAddAsync(createList);
                if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
            }

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _repository.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<ProductColorBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            if (_repository is not IBatchWriteRepository<ProductColor> batchRepository)
                return ServiceResult<ProductColorBatchSaveResponse>.Fail($"{nameof(ProductColor)} 仓储未实现批量保存");

            if (createList is { Count: > 0 })
            {
                result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
            }

            if (updateList is { Count: > 0 })
            {
                await batchRepository.UpdateRangeAsync(updateList);
                result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
            }

            if (deleteIds is { Count: > 0 })
            {
                await batchRepository.DeleteRangeAsync(deleteIds);
                result.DeleteIdList = deleteIds;
            }

            _session.Transaction.Commit();
            return ServiceResult<ProductColorBatchSaveResponse>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<ProductColorBatchSaveResponse>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion


    #region 显式基础读写

    public async Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _repository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(ProductColor));
                return ServiceResult<ProductColorDto>.Fail("未找到数据");
            }

            return ServiceResult<ProductColorDto>.Ok(entity.Adapt<ProductColorDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<ProductColorDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<ProductColorDto>>.Ok(entities.Adapt<IReadOnlyList<ProductColorDto>>())
                : ServiceResult<IReadOnlyList<ProductColorDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ProductColorDto>>.Fail(ex.Message);
        }
    }


    public async Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request)
    {
        try
        {
            var paged = await _repository.GetProductColorPagedAsync(request);
            return ServiceResult<PagedResultDto<ProductColorDto>>.Ok(new PagedResultDto<ProductColorDto>
            {
                Items = paged.Items.Adapt<List<ProductColorDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<ProductColorDto>>.Fail(ex.Message);
        }
    }


    public async Task<ServiceResult<Guid>> AddAsync(ProductColorDto dto)
    {
        try
        {
            var entity = dto.Adapt<ProductColor>();
            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;
            var uid = await _repository.AddAsync(entity);
            return ServiceResult<Guid>.Ok(uid);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateAsync(ProductColorDto dto)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(dto.Uid);
            if (existing == null) return ServiceResult<int>.Fail("数据不存在");

            dto.Adapt(existing);
            await _repository.UpdateAsync(existing);
            return ServiceResult<int>.Ok(1);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteAsync(Guid uid)
    {
        try
        {
            return ServiceResult<int>.Ok(await _repository.DeleteAsync(uid));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }


    #endregion

}
