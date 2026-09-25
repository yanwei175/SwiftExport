using SwiftExport.Core.Interfaces.Customers.CustomerProducts;
using SwiftExport.Core.Interfaces.Customers.Management;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Customers.CustomerProducts.Query;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Customers.CustomerProducts.Query;

/// <summary>读取客户产品的客户与可选公司产品联表资料。</summary>
public sealed class CustomerProductQueryService(
    ICustomerRepository customerRepository,
    ICustomerProductRepository repository,
    ServiceBaseContext context) : ICustomerProductQueryService
{
    private readonly ILogger<CustomerProductQueryService> _logger = context.LoggerFactory.CreateLogger<CustomerProductQueryService>();

    /// <inheritdoc />
    public async Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid uid)
    {
        var dto = await repository.GetDtoByIdAsync(uid);
        if (dto is null)
        {
            _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的客户产品", uid);
            return ServiceResult<CustomerProductDto>.Fail("未找到数据");
        }
        return ServiceResult<CustomerProductDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, Guid customerId)
    {
        var entity = await repository.GetByCustomerSkuAsync(customerSku, customerId);
        if (entity is null)
        {
            _logger.LogWarning("查询失败：客户 {CustomerId} 下未找到料号 {CustomerSku}", customerId, customerSku);
            return ServiceResult<CustomerProductDto>.Fail($"未找到客户产品型号为 {customerSku} 的产品");
        }
        var dto = await repository.GetDtoByIdAsync(entity.Uid);
        return ServiceResult<CustomerProductDto>.Ok(dto!);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest request)
    {
        try
        {
            var result = await repository.GetPagedAsync(request);
            return result is null
                ? ServiceResult<PagedResponseBase<CustomerProductDto>>.Fail("未找到数据")
                : ServiceResult<PagedResponseBase<CustomerProductDto>>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<CustomerProductDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync()
    {
        var ids = await repository.GetAllCustomerIdListAsync();
        if (ids is null || ids.Count == 0)
            return ServiceResult<IReadOnlyList<CustomerDto>>.Fail("暂无客户产品记录");
        var list = await customerRepository.GetByIdListAsync(ids);
        return list is null || list.Count == 0
            ? ServiceResult<IReadOnlyList<CustomerDto>>.Fail("未找到对应客户")
            : ServiceResult<IReadOnlyList<CustomerDto>>.Ok(list.Adapt<IReadOnlyList<CustomerDto>>());
    }
}
