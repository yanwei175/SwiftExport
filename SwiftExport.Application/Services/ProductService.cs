using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
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
        private readonly IMapper _mapper;
        public ProductService(IProductRepository repo,IMapper mapper) 
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<int> AddProductAsync(Product product)
        {
           try
                {
                  return await _repo.AddSingleReturnNewIDAsync(product);
                }
                catch (Exception ex)
                {
                 throw ex;
            }
        }

        public Task<int> DeleteProductByIDAsync(int id)
        {
            try
                {
                return _repo.DeleteSingleByIdAsync(id);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public Task<int> DisableProductByIDAsync(int id)
        {
            try
                {
                return _repo.DisableSingleByID(id);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<IEnumerable<Product>> GetAll()
        {
            try
             {
                    return await _repo.GetAllAsync();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<Product> GetProductByIDAsync(int id)
        {
            try
                {
                return await _repo.GetSingleByIdAsync(id);
     
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<Product> Get单产品BySkuAsync(string sku)
        {
            try
            {
                return await _repo.Get单产品BySku(sku);
                 
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<Product> Get单产品By客户代码和型号Async(string CustomerCode, string cSKU)
        {
            try
                {
                return await _repo.Get单产品By客户代码和型号(CustomerCode, cSKU);
                
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        public async Task<IEnumerable<Product>> Get多产品BySkusAsync(IEnumerable<string> sku)
        {
            try
                {
                return await _repo.Get多产品BySkus(sku);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<IEnumerable<Product>> Get多产品By动态条件Async(Dictionary<string, string> 条件字典)
        {
           try
                {
                return await _repo.Get多产品By动态条件(条件字典);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<IEnumerable<Product>> Get多产品By单客户代码多客户型号Async(string CustomerCode, IEnumerable<string> cSKUs)
        {
           try
                {
                return await _repo.Get多产品By单客户代码多客户型号(CustomerCode,cSKUs);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<IEnumerable<Product>> Get多产品By多客户代码多客户型号Async(Dictionary<string, string> cSkuAndCustomerCode)
        {
            try
                {
                return await _repo.Get多产品By多客户代码多客户型号(cSkuAndCustomerCode);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<int> UpdateProductAsync(Product product)
        {
            try
            {
                return await _repo.UpdateSingleAsync(_mapper.Map<Product>(product));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<int> 同步产品Async(IEnumerable<Product> products)
        {
           try
           {
                return await _repo.同步产品(_mapper.Map<IEnumerable<Product>>(products));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
