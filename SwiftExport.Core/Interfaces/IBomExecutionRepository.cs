using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Interfaces;

/// <summary>执行结构读取所需的窄仓储端口，所有结果在调用方事务内持锁到事务结束。</summary>
public interface IBomExecutionRepository
{
    /// <summary>按稳定身份锁定产品执行字段；包含生命周期字段供业务判断。</summary>
    Task<Product?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>锁定该父产品全部组成行范围，包括空范围，防止读取后新增组成。</summary>
    Task<IReadOnlyList<Bom>> GetLinesAsync(Guid parentProductId, CancellationToken cancellationToken = default);

    /// <summary>锁定 Unit 字典范围，返回单位资格事实供完整读取校验。</summary>
    Task<IReadOnlyList<SysDictData>> GetUnitsAsync(CancellationToken cancellationToken = default);
}
