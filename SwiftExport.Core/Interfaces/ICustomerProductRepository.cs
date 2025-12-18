using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomerProductRepository
    {

        /// <summary>
        /// 调用数据库的存储过程,执行Merge 操作,
        /// 同步客户产品,
        /// 需要数据库里建有对应的存储过程和客户自定义表
        /// </summary>
        /// <param name="products">
        /// 必须须带ID的客户产品列表,如果没有ID会新增客户产品
        /// </param>
        /// <returns>受影响的行数</returns>
        Task<int> SyncCustomerProductsAsync(IEnumerable<CustomerProducts> products, IUnitOfWork uow);
    }
}
