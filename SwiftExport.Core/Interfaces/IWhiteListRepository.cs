using SwiftExport.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IWhiteListRepository : IRepository<WhiteLists>
    {
        Task<IEnumerable<string>> GetVstoWhiteListAsync();
    }
}
