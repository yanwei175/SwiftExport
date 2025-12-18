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
    public class ProductService : IProductService
    {
        private readonly IProductRepository _repo;
        private readonly IUintOfWorkFactory _uowFac;
        public ProductService(IUintOfWorkFactory uintOfWorkFactory, IProductRepository repo) 
        {
            _repo = repo;
            _uowFac = uintOfWorkFactory;
        }

        public async Task<int> AddAsync(Product product)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    // 假设 AddAsync 返回 Product 实体
                    var affectedRows = await _repo.AddAsync(product, uow); // 假设 repo 方法已规范为 AddAsync

                    // 【事务管理】提交
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【事务管理】回滚
                    await uow.RollbackAsync();
                    throw; // 抛出原始异常
                }
            }

        }
       
        public async Task<int> DeleteByIdAsync(int id)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _repo.DeleteAsync(id, uow); // 假设 repo 方法已规范

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
        public async Task<int> DeleteSoftAsync(int id)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _repo.DeleteSoftAsync(id, uow); // 假设 repo 方法已规范

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
        public async Task<int> UpdateAsync(Product product)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _repo.UpdateAsync(product, uow); // 假设 repo 方法已规范
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
        public async Task<IEnumerable<Product>> GetAll()
        {
               return await _repo.GetAllAsync();
        }
        public async Task<Product> GetByIdAsync(int id)
        {
                return await _repo.GetByIdAsync(id);
        }
        public async Task<Product> Get单产品BySkuAsync(string sku)
        {
                return await _repo.Get单产品BySku(sku);
        }

        public async Task<Product> Get单产品By客户代码和型号Async(string CustomerCode, string cSKU)
        {
                return await _repo.Get单产品By客户代码和型号(CustomerCode, cSKU);

        }

        public async Task<IEnumerable<Product>> Get多产品BySkusAsync(IEnumerable<string> sku)
        {
                return await _repo.Get多产品BySkus(sku);
        }

        public async Task<IEnumerable<Product>> Get多产品By动态条件Async(Dictionary<string, QueryCondition> 条件字典)
        {
                return await _repo.GetByUserWhereAsync(条件字典);
        }

        public async Task<IEnumerable<Product>> Get多产品By单客户代码多客户型号Async(string CustomerCode, IEnumerable<string> cSKUs)
        {
                return await _repo.Get多产品By单客户代码多客户型号(CustomerCode,cSKUs);
        }

        public async Task<IEnumerable<Product>> Get多产品By多客户代码多客户型号Async(Dictionary<string, string> cSkuAndCustomerCode)
        {
                return await _repo.Get多产品By多客户代码多客户型号(cSkuAndCustomerCode);
        }

        public async Task<int> SyncProductsAsync(IEnumerable<Product> products)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _repo.SyncProductsAsync(products, uow); // 假设 repo 方法已规范

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
