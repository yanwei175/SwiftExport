using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Products.BomManagement;

/// <summary>在调用方事务内执行产品 BOM 专项字段更新，禁止覆盖其他产品资料。</summary>
public interface IProductBomBindingRepository
{
    /// <summary>按产品身份与旧版本更新绑定、版本和审计字段，候选必须已经通过业务校验。</summary>
    Task<RepositoryWriteResult> BindAsync(Product candidate, int expectedVersion, CancellationToken cancellationToken);
}
