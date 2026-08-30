using Mapster;
using Microsoft.Extensions.Logging;
using {{ProjectName}}.Application.Contexts;
using {{ProjectName}}.Application.Interfaces;
using {{ProjectName}}.Contracts.DTOs.Sample;
using {{ProjectName}}.Contracts.Wrappers;
using {{ProjectName}}.Core.Entities.Sample;
using {{ProjectName}}.Core.Interfaces;

namespace {{ProjectName}}.Application.Services.Sample;

public class SampleItemService(ISampleItemRepository sampleRepository, ServiceBaseContext context) : ISampleItemService
{
    private readonly ISampleItemRepository _sampleRepository = sampleRepository;
    private readonly ServiceBaseContext _context = context;
    private ILogger Logger => _context.LoggerFactory.CreateLogger(GetType());

    public async Task<ServiceResult<SampleItemDto>> GetByIdAsync(Guid uid)
    {
        var entity = await _sampleRepository.GetByIdAsync(uid);
        return entity == null
            ? ServiceResult<SampleItemDto>.Fail("数据不存在")
            : ServiceResult<SampleItemDto>.Ok(entity.Adapt<SampleItemDto>());
    }

    public async Task<ServiceResult<PagedResultDto<SampleItemDto>>> SearchPageAsync(PagedRequestDto request)
    {
        var whereSql = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : "Code LIKE @Keyword OR Name LIKE @Keyword";
        var param = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : new { Keyword = $"%{request.Keyword}%" };

        var page = await _sampleRepository.GetPagedListAsync(whereSql, param, request.PageIndex, request.PageSize);
        return ServiceResult<PagedResultDto<SampleItemDto>>.Ok(new PagedResultDto<SampleItemDto>
        {
            Items = page.Items.Adapt<List<SampleItemDto>>(),
            TotalCount = page.TotalCount,
            PageIndex = page.PageIndex,
            PageSize = page.PageSize
        });
    }

    public async Task<ServiceResult<Guid>> CreateAsync(SampleItemDto dto)
    {
        var duplicated = await _sampleRepository.GetByCodeAsync(dto.Code);
        if (duplicated != null) return ServiceResult<Guid>.Fail("编码已存在");

        var uid = await _sampleRepository.AddAsync(dto.Adapt<SampleItem>());
        return ServiceResult<Guid>.Ok(uid);
    }

    public async Task<ServiceResult<int>> UpdateAsync(Guid uid, SampleItemDto dto)
    {
        dto.Uid = uid;
        await _sampleRepository.UpdateAsync(dto.Adapt<SampleItem>());
        return ServiceResult<int>.Ok(1);
    }

    public async Task<ServiceResult<int>> DeleteAsync(Guid uid)
    {
        var rows = await _sampleRepository.DeleteSoftAsync(uid);
        return ServiceResult<int>.Ok(rows);
    }

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SampleItemDto> request)
    {
        if (!request.HasChanges) return ServiceResult<BatchResultDto>.Ok(new BatchResultDto(), "没有需要保存的变更");

        _context.Session.BeginTransaction();
        try
        {
            var result = new BatchResultDto();

            foreach (var dto in request.AddList)
            {
                var uid = await _sampleRepository.AddAsync(dto.Adapt<SampleItem>());
                result.AddIdList.Add(uid);
            }

            foreach (var dto in request.UpdateList)
            {
                await _sampleRepository.UpdateAsync(dto.Adapt<SampleItem>());
                result.UpdateIdList.Add(dto.Uid);
            }

            foreach (var uid in request.DeleteIdList)
            {
                await _sampleRepository.DeleteSoftAsync(uid);
                result.DeleteIdList.Add(uid);
            }

            _context.Session.Commit();
            return ServiceResult<BatchResultDto>.Ok(result);
        }
        catch (Exception ex)
        {
            _context.Session.Rollback();
            Logger.LogError(ex, "SampleItem 批量保存失败");
            throw;
        }
    }

    public async Task<SampleItemDto?> GetByCodeAsync(string code)
    {
        var entity = await _sampleRepository.GetByCodeAsync(code);
        return entity == null ? null : entity.Adapt<SampleItemDto>();
    }
}
