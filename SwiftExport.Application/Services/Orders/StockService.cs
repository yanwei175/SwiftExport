using Dapper;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

public class StockService(IInventoryRepository inventoryRepository, ServiceBaseContext context) : IStockService
{
    private readonly IInventoryRepository _inventoryRepository = inventoryRepository;
    private readonly SwiftExport.Infrastructure.Data.DbSession _session = context.Session;

    public async Task<ServiceResult<bool>> DecreaseStockAsync(string whseCode, string locationCode, string sku, decimal qty, bool allowNegativeStock, string? bizType = null, string? bizNo = null)
    {
        if (string.IsNullOrWhiteSpace(whseCode) || string.IsNullOrWhiteSpace(locationCode) || string.IsNullOrWhiteSpace(sku))
            return ServiceResult<bool>.Fail("库存扣减参数不完整");

        if (qty <= 0)
            return ServiceResult<bool>.Fail("扣减数量必须大于 0");

        var stock = await _inventoryRepository.GetByWhseLocationSkuAsync(whseCode.Trim(), locationCode.Trim(), sku.Trim());
        if (stock == null)
        {
            if (!allowNegativeStock)
                return ServiceResult<bool>.Fail($"库存不足：{locationCode}/{sku} 当前 0，需要 {qty}");

            stock = new Inventory
            {
                WhseCode = whseCode.Trim(),
                LocationCode = locationCode.Trim(),
                Sku = sku.Trim(),
                OnhandQty = -qty,
                CreatedDate = DateTime.Now,
                IsDeleted = false
            };
            await _inventoryRepository.AddAsync(stock);
        }
        else
        {
            if (!allowNegativeStock && stock.OnhandQty < qty)
                return ServiceResult<bool>.Fail($"库存不足：{locationCode}/{sku} 当前 {stock.OnhandQty}，需要 {qty}");

            stock.OnhandQty -= qty;
            stock.UpdatedDate = DateTime.Now;
            await _inventoryRepository.UpdateAsync(stock);
        }

        await WriteInventoryTransactionIfExistsAsync(whseCode.Trim(), locationCode.Trim(), sku.Trim(), -qty, bizType ?? "Decrease", bizNo);
        return ServiceResult<bool>.Ok(true);
    }

    private async Task WriteInventoryTransactionIfExistsAsync(string whseCode, string locationCode, string sku, decimal qty, string bizType, string? bizNo)
    {
        const string sql = @"IF OBJECT_ID('dbo.InventoryTransaction','U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[InventoryTransaction]
    ([WhseCode],[LocationCode],[Sku],[Qty],[BizType],[BizNo],[CreatedDate])
    VALUES (@WhseCode,@LocationCode,@Sku,@Qty,@BizType,@BizNo,@CreatedDate)
END";

        await _session.Connection.ExecuteAsync(
            sql,
            new
            {
                WhseCode = whseCode,
                LocationCode = locationCode,
                Sku = sku,
                Qty = qty,
                BizType = bizType,
                BizNo = bizNo,
                CreatedDate = DateTime.Now
            },
            transaction: _session.Transaction);
    }
}
