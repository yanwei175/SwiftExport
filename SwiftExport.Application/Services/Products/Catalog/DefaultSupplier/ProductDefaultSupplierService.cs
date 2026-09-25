using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.Catalog.DefaultSupplier;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Products.Catalog.DefaultSupplier;

/// <summary>以 Product 为目标设置默认供应商；供应商产品普通保存不触发此操作。</summary>
public sealed class ProductDefaultSupplierService(
    ISupplierProductRepository relationRepository,
    ISupplierRepository supplierRepository,
    IProductRepository productRepository,
    ServiceBaseContext context) : IProductDefaultSupplierService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<ProductDefaultSupplierService> _logger = context.LoggerFactory.CreateLogger<ProductDefaultSupplierService>();

    /// <inheritdoc />
    public async Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(SetProductDefaultSupplierCommand command)
    {
        if (command is null
            || command.SupplierProductId == Guid.Empty
            || command.ProductId == Guid.Empty
            || command.SupplierId == Guid.Empty
            || command.ExpectedProductVersion <= 0)
            return ServiceResult<SetProductDefaultSupplierResult>.Fail("供应商产品、产品、供应商和产品版本均不能为空。");
        if (_session.Transaction is not null)
            return ServiceResult<SetProductDefaultSupplierResult>.Fail("当前请求已存在未完成事务。");

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 保持原锁顺序：关系、产品、供应商。
            var relation = await relationRepository.GetByIdAsync(command.SupplierProductId, withUpdLock: true);
            if (relation is null || relation.SkuId != command.ProductId || relation.SupplierId != command.SupplierId)
                throw new InvalidOperationException("供应商产品关系不存在、已删除或与提交的产品/供应商不一致。");

            var product = await productRepository.GetByIdAsync(command.ProductId, withUpdLock: true)
                ?? throw new InvalidOperationException("关联产品不存在或已删除。");
            var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, withUpdLock: true)
                ?? throw new InvalidOperationException("关联供应商不存在或已删除。");
            if (product.Version != command.ExpectedProductVersion)
                throw new InvalidOperationException("产品已被其他用户修改，请刷新后重试。");
            if (!string.Equals(relation.ProductSku, product.Sku, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(relation.SupplierCode, supplier.SupplierCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("供应商产品的编码快照与当前产品或供应商不一致，请先修复主数据。");

            var write = await productRepository.SetDefaultSupplierAsync(
                product.Uid, command.ExpectedProductVersion, supplier.Uid, supplier.SupplierCode);
            if (!write.Success)
                throw new InvalidOperationException(write.Message ?? "默认供应商设置失败。");

            var dto = await relationRepository.GetDtoByIdAsync(relation.Uid)
                ?? throw new InvalidOperationException("默认供应商已更新，但重新加载供应商产品失败。");
            _session.Transaction.Commit();
            return ServiceResult<SetProductDefaultSupplierResult>.Ok(new SetProductDefaultSupplierResult
            {
                ProductId = product.Uid,
                SupplierId = supplier.Uid,
                SupplierCode = supplier.SupplierCode,
                OldProductVersion = command.ExpectedProductVersion,
                NewProductVersion = write.NewVersion ?? command.ExpectedProductVersion + 1,
                SupplierProduct = dto
            }, "已设为产品默认供应商。");
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            _logger.LogWarning(ex, "设置产品默认供应商失败。");
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<SetProductDefaultSupplierResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
}
