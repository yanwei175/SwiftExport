using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems
{
    /// <summary>
    /// 货币汇率字典仓储实现
    /// </summary>
    public class SysDicCurrencyExchangeRepository(RepositoryBaseContext context)
        : DapperRepository<SysDicCurrencyExchange>(context), ISysDicCurrencyExchangeRepository
    {
    }
}
