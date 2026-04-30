using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

public interface IStockService
{
    Task<ServiceResult<bool>> DecreaseStockAsync(string whseCode, string locationCode, string sku, decimal qty, bool allowNegativeStock, string? bizType = null, string? bizNo = null);
}
