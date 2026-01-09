using SwiftExport.Core.Entities;
using SwiftExport.Core.Entities.OtherEntities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IProductRepository : IRepository<Product>
    {
        // ---------------------------------------------------------
        // 查询 - 辅助/元数据
        // ---------------------------------------------------------

        /// <summary>
        /// 获取所有用于内存搜索的元数据（包含拼接好的搜索字符串）
        /// </summary>
        Task<List<ProductSearchMeta>> GetAllSearchMetaAsync();

        // ---------------------------------------------------------
        // 查询 - 单个实体
        // ---------------------------------------------------------

        /// <summary>
        /// 根据 Sku 查询一个产品
        /// </summary>
        /// <param name="sku">产品的 SKU</param>
        /// <returns>一个产品实体，如果未找到则返回 null</returns>
        Task<Product> GetBySkuAsync(string sku);

        /// <summary>
        /// 根据客户代码和客户型号查询一个产品
        /// </summary>
        /// <param name="customerCode">客户代码</param>
        /// <param name="cSku">客户型号</param>
        /// <returns>一个产品实体</returns>
        Task<Product> GetByCustomerInfoAsync(string customerCode, string cSku);

        // ---------------------------------------------------------
        // 查询 - 批量/列表 (返回 IReadOnlyList 以确保安全)
        // ---------------------------------------------------------

        /// <summary>
        /// 根据 SKU 列表查询多个产品
        /// </summary>
        /// <param name="skus">SKU 集合</param>
        Task<IReadOnlyList<Product>> GetListBySkuListAsync(IEnumerable<string> skuList);

        /// <summary>
        /// 查询指定客户代码下的多个客户型号对应的产品
        /// </summary>
        /// <param name="customerCode">客户代码</param>
        /// <param name="cSkus">客户型号集合</param>
        Task<IReadOnlyList<Product>> GetListByCustomerCodeAndcSkuListAsync(string customerCode, IEnumerable<string> cSkuList);

        /// <summary>
        /// 根据多个 [客户代码 + 客户型号] 组合查询产品
        /// (主要用于 Excel 导入时的复杂匹配)
        /// </summary>
        /// <param name="cSkuAndCustomerCode">
        /// 字典 Key: 客户型号 (CSku)
        /// 字典 Value: 客户代码 (CustomerCode)
        /// </param>
        Task<IReadOnlyList<Product>> GetProductListByCustomerSkuAndCodeAsync(Dictionary<string, string> cSkuAndCustomerCode);

        Task<List<string>> GetAllCustomerCodeListAsync();



        // ---------------------------------------------------------
        // 写入 - 增删改
        // ---------------------------------------------------------

        /// <summary>
        /// 软删除：根据 SKU 将产品标记为已删除 (IsDelete = 1)
        /// </summary>
        /// <param name="sku">产品 SKU</param>
        /// <param name="uow">工作单元 (用于事务控制)</param>
        Task<int> DisableBySkuAsync(string sku, IUnitOfWork uow);

        /// <summary>
        /// 硬删除：根据 SKU 从数据库物理删除产品 (慎用)
        /// </summary>
        /// <param name="sku">产品 SKU</param>
        /// <param name="uow">工作单元</param>
        Task<int> DeleteBySkuAsync(string sku, IUnitOfWork uow);




        /// <summary>
        /// 批量同步产品 (Merge操作)
        /// 调用存储过程，存在则更新，不存在则新增
        /// </summary>
        /// <param name="products">产品列表</param>
        /// <param name="uow">工作单元</param>
        Task<int> SyncProductsAsync(IEnumerable<Product> products, IUnitOfWork uow);
    }
}