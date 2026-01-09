using SwiftExport.Core.Entities;
using SwiftExport.Core.Entities.OtherEntities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IProductService : IBaseService<Product>
    {
        Task<Product> GetBySkuAsync(string sku);
        Task<Product> GetByCustomerCodeWithCSkuAsync(string CustomerCode, string cSKU);
        Task<IReadOnlyList<Product>> GetBySkuListAsync(IReadOnlyList<string> skus);
        Task<IReadOnlyList<Product>> GetByCustomerCodeWithCSkuListAsync(string CustomerCode, IReadOnlyList<string> cSKUList);
        Task<IReadOnlyList<Product>> GetByCustomerCodeListWithCSkuListAsync(Dictionary<string, string> cSkuListAndCustomerCodeList);
        Task<List<string>> GetAllCustomerCodeListAsync();

        /// <summary>
        /// 搜索联想
        /// </summary>
        Task<List<ProductSearchMeta>> GetAllSearchMetaAsync();

        Task<int> SyncProductsAsync(IReadOnlyList<Product> productList);

    }
}
