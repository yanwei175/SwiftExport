using SwiftExport.AppLayer.DTOs;
using SwiftExport.Core.Entities;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IProductService
    {
        Task<IEnumerable<Product>> GetAll();
        Task<int> AddProductAsync(Product product);
        Task<Product> GetProductByIDAsync(int id);
        Task<int> DisableProductByIDAsync(int id);
        Task<int> DeleteProductByIDAsync(int id);
        Task<int> UpdateProductAsync(Product product);

        Task<Product> Get单产品BySkuAsync(string sku);
        Task<Product> Get单产品By客户代码和型号Async(string CustomerCode, string cSKU);
        Task<IEnumerable<Product>> Get多产品BySkusAsync(IEnumerable<string> skus);
        Task<IEnumerable<Product>> Get多产品By单客户代码多客户型号Async(string CustomerCode, IEnumerable<string> cSKUs);
        Task<IEnumerable<Product>> Get多产品By多客户代码多客户型号Async(Dictionary<string, string> cSkuAndCustomerCode);
        Task<IEnumerable<Product>> Get多产品By动态条件Async(Dictionary<string, string> 条件字典);
        Task<int> 同步产品Async(IEnumerable<Product> products);

    }
}
