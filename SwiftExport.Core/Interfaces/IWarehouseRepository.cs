using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 仓库仓储接口。
/// </summary>
public interface IWarehouseRepository : IRepository<Warehouse>
{
    /// <summary>
    /// 按仓库代码查询仓库。
    /// </summary>
    Task<Warehouse?> GetByWhseCodeAsync(string whseCode);
}
