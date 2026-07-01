using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Infrastructure.Repositories.Systems;

public partial class SysDicCurrencyRepository
{
    public Task<IReadOnlyList<SysDicCurrency>> GetListAsync(object criteriaObject, bool withUpdLock = false)
    {
        return GetListCoreAsync(criteriaObject, withUpdLock);
    }
}
