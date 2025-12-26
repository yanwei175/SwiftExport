using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class ProductService : BaseService<Product>, IProductService
    {
        private readonly IProductRepository _tsrepo;
        public ProductService(IUintOfWorkFactory _uowFac, IProductRepository repo) : base(repo, _uowFac)
        {
            _tsrepo = repo;
        }

        public async Task<IEnumerable<Product>> GetAll()
        {
               return await _tsrepo.GetAllAsync();
        }


        public async Task<Product> GetBySkuAsync(string sku)
        {
                return await _tsrepo.Get单产品BySku(sku);
        }

        public Task<IEnumerable<Product>> GetBySkuListAsync(IEnumerable<string> skus)
        {
            throw new NotImplementedException();
        }

        public async Task<Product> GetByCustomerCodeWithCSkuAsync(string CustomerCode, string cSKU)
        {
                return await _tsrepo.Get单产品By客户代码和型号(CustomerCode, cSKU);

        }

        public async Task<IEnumerable<Product>> Get多产品BySkusAsync(IEnumerable<string> sku)
        {
                return await _tsrepo.Get多产品BySkus(sku);
        }

        public async Task<IEnumerable<Product>> Get多产品By动态条件Async(Dictionary<string, QueryCondition> 条件字典)
        {
                return await _tsrepo.GetByUserWhereAsync(条件字典);
        }

        public async Task<IEnumerable<Product>> GetByCustomerCodeWithCSkuListAsync(string CustomerCode, IEnumerable<string> cSKUs)
        {
                return await _tsrepo.Get多产品By单客户代码多客户型号(CustomerCode,cSKUs);
        }

        public async Task<IEnumerable<Product>> GetByCustomerCodeListWithCSkuListAsync(Dictionary<string, string> cSkuAndCustomerCode)
        {
                return await _tsrepo.Get多产品By多客户代码多客户型号(cSkuAndCustomerCode);
        }

        public async Task<int> SyncProductsAsync(IEnumerable<Product> products)
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










    }
}
