using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Query;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Suppliers.SupplierProducts.Query;

/// <summary>读取供应商资料联表投影，保留供应商所属过滤并允许公司产品为空。</summary>
public sealed class SupplierProductQueryService(ISupplierProductRepository repository, ServiceBaseContext context) : ISupplierProductQueryService
{
    private readonly ILogger<SupplierProductQueryService> _logger = context.LoggerFactory.CreateLogger<SupplierProductQueryService>();

    /// <inheritdoc />
    public async Task<ServiceResult<SupplierProductDto>> GetByIdAsync(Guid uid)
    {
        var dto = await repository.GetDtoByIdAsync(uid);
        return dto is null
            ? ServiceResult<SupplierProductDto>.Fail("未找到供应商产品。")
            : ServiceResult<SupplierProductDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync(SupplierProductSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.SupplierId == Guid.Empty)
            return ServiceResult<PagedResponseBase<SupplierProductDto>>.Fail("供应商 ID 不能为空。");
        try
        {
            return ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(await repository.GetPagedAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<SupplierProductDto>>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }
}
