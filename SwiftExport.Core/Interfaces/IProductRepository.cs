using SwiftExport.Core.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IProductRepository :IRepository<Product>
    {

        /// <summary>
        ///根据Sku查询一个产品
        /// </summary>
        /// <param name="sku">
        /// 产品的Sku
        /// </param>
        /// <returns>一个产品实体</returns>
        Task<Product> Get单产品BySku(string sku);

        /// <summary>
        ///根据SKU删除一个产品,软删除
        /// </summary>
        /// <param name="sku">
        /// 产品的Sku
        /// </param>
        /// <returns>受影响的行数</returns>
        Task<int> Disable单产品BySku(string sku);

        /// <summary>
        ///根据SKU删除一个产品,硬删除
        /// </summary>
        /// <param name="sku">
        /// 产品的Sku
        /// </param>
        /// <returns>受影响的行数</returns>
        Task<int> Delete单产品BySku(string sku);

        /// <summary>
        ///根据客户型号和客户代码查询一个产品
        /// </summary>
        /// <param name="CustomerCode">
        /// 客户代码
        /// </param>
        /// <param name="cSKU">
        /// 客户型号
        /// </param>
        /// <returns>一个产品实体</returns>
        Task<Product> Get单产品By客户代码和型号(string CustomerCode, string cSKU);

        /// <summary>
        ///根据一个Sku列表查询多个产品
        /// </summary>
        /// <param name="skus">
        /// 包含了多个SKU的列表
        /// </param>
        /// <returns>一个多产品列表</returns>
        Task<IEnumerable<Product>> Get多产品BySkus(IEnumerable<String> skus);

        /// <summary>
        ///动态查询参数查询
        /// </summary>
        /// <param name="条件字典">
        /// 一个Key为字段名,称,Value为字段值的查询字典
        /// </param>
        /// <returns>一个多产品列表</returns>
        Task<IEnumerable<Product>> Get多产品By动态条件(Dictionary<string, string> 条件字典);

        /// <summary>
        ///适用于一个客户代码下多个客户型号的查询
        /// </summary>
        /// <param name="CustomerCode">
        /// 客户代码
        /// </param>
        /// <param name="cSKUs">
        /// 客户型号的集合
        /// </param>
        /// <returns>一个多产品列表</returns>
        Task<IEnumerable<Product>> Get多产品By单客户代码多客户型号(string CustomerCode, IEnumerable<string> cSKUs);

        /// <summary>
        ///根据多个客户代码和多个客户型号取多个产品,主要用于匹配数据,Excel里用的最多
        /// </summary>
        /// <param name="cSkuAndCustomerCode">
        /// 一个Key为客户产品型号和Value为客户代码的字典
        /// </param>
        /// <returns>一个多产品列表</returns>
        Task<IEnumerable<Product>> Get多产品By多客户代码多客户型号(Dictionary<string, string> cSkuAndCustomerCode);

        /// <summary>
        /// 调用数据库的存储过程,执行Merge 操作,
        /// 同步产品,
        /// 需要数据库里建有对应的存储过程和客户自定义表
        /// </summary>
        /// <param name="products">
        /// 必须须带ID的产品列表,如果没有ID会新增产品
        /// </param>
        /// <returns>受影响的行数</returns>
        Task<int> 同步产品(IEnumerable<Product> products);

    }
}
