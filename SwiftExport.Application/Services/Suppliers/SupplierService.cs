using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Suppliers;

public partial class SupplierService(ISupplierRepository repository, ServiceBaseContext context)
    : BaseService<Supplier, SupplierDto>(repository, context), ISupplierService
{
    private readonly ILogger<SupplierService> _logger = context.LoggerFactory.CreateLogger<SupplierService>();
    private readonly ISupplierRepository repository = repository;

    public Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest batchReq)
    {
        throw new NotImplementedException();
    }








    #region 显式基础读写

    public async Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync()
    {
        try
        {
            var entities = await repository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SupplierDto>>.Ok(entities.Adapt<IReadOnlyList<SupplierDto>>())
                : ServiceResult<IReadOnlyList<SupplierDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SupplierDto>>.Fail(ex.Message);
        }
    }

    public Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync(SupplierSearchRequest request)
    {
        throw new NotImplementedException();
    }
    #endregion

}

