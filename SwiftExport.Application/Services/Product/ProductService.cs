using SwiftExport.Application.Interfaces;

namespace SwiftExport.Application.Services.Product
{
    public class ProductService : BaseService<Product>, IProductService
    {
        private readonly IProductRepository _tsrepo;
        public ProductService(IUintOfWorkFactory _uowFac, IProductRepository repo) : base(repo, _uowFac)
        {
            _tsrepo = repo;
        }


        public async Task<Product> GetBySkuAsync(string sku)
        {
            return await _tsrepo.GetBySkuAsync(sku);
        }

        public async Task<IReadOnlyList<Product>> GetBySkuListAsync(IReadOnlyList<string> skuList)
        {
            return await _tsrepo.GetListBySkuListAsync(skuList);
        }

        public async Task<Product> GetByCustomerCodeWithCSkuAsync(string CustomerCode, string cSKU)
        {
            return await _tsrepo.GetByCustomerInfoAsync(CustomerCode, cSKU);

        }

        public async Task<IReadOnlyList<Product>> GetByCustomerCodeWithCSkuListAsync(string CustomerCode, IReadOnlyList<string> cskuList)
        {
            return await _tsrepo.GetListByCustomerCodeAndcSkuListAsync(CustomerCode, cskuList);
        }

        public async Task<IReadOnlyList<Product>> GetByCustomerCodeListWithCSkuListAsync(Dictionary<string, string> cSkuAndCustomerCode)
        {
            return await _tsrepo.GetProductListByCustomerSkuAndCodeAsync(cSkuAndCustomerCode);
        }

        public async Task<int> SyncProductsAsync(IReadOnlyList<Product> products)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _tsrepo.SyncProductsAsync(products, uow); // 假设 repo 方法已规范

                    // 【事务管理】提交
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【事务管理】回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }

        }

        public async Task<List<ProductSearchMeta>> GetAllSearchMetaAsync()
        {
            return await _tsrepo.GetAllSearchMetaAsync();
        }

        public async Task<List<string>> GetAllCustomerCodeListAsync()
        {
            return await _tsrepo.GetAllCustomerCodeListAsync();
        }
    }
}
