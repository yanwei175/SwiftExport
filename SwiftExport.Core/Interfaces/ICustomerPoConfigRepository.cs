using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomerPoConfigRepository : IRepository<CustomerPoConfig>
    {
        /// <summary>
        /// 获取非唯一项
        /// </summary>
        /// <param name="poConfigCheckList">PoConfigCheck</param>
        /// <returns>PoConfigCheck</returns>
        Task<IReadOnlyList<PoConfigCheck>> GetNonUniqueByPoConfigCheckList(IEnumerable<PoConfigCheck> poConfigCheckList);
    }
}