using SwiftExport.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IWhiteListServices : IBaseService<WhiteLists>
    {
        Task<IReadOnlyList<string>> GetVstoWhiteListAsync();
    }
}
