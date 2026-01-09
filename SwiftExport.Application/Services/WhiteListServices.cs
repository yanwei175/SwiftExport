using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class WhiteListServices : BaseService<WhiteLists>, IWhiteListServices
    {
        private readonly IWhiteListRepository _WlRepo;
        public WhiteListServices(IUintOfWorkFactory _uowFac, IWhiteListRepository repo) : base(repo, _uowFac)
        {
            _WlRepo = repo;
        }
        public async Task<IReadOnlyList<string>> GetVstoWhiteListAsync()
        {
            return (await _WlRepo.GetVstoWhiteListAsync()).ToList();
        }
    }
}
