using SwiftExport.AppLayer.DTOs;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IProductService : IBaseService<Product>
    {
        Task<Product> GetBySkuAsync(string sku);
        Task<Product> GetByCustomerCodeWithCSkuAsync(string CustomerCode, string cSKU);
        Task<IEnumerable<Product>> GetBySkuListAsync(IEnumerable<string> skus);
        Task<IEnumerable<Product>> GetByCustomerCodeWithCSkuListAsync(string CustomerCode, IEnumerable<string> cSKUList);
        Task<IEnumerable<Product>> GetByCustomerCodeListWithCSkuListAsync(Dictionary<string, string> cSkuListAndCustomerCodeList);
        Task<int> SyncProductsAsync(IEnumerable<Product> productList);

    }
}
