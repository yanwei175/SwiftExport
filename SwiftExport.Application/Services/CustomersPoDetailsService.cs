using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class CustomersPoDetailsService : BaseService<CustomersPoDetails>, ICustomersPoDetailsService
    {
        private readonly ICustomersPoDetailsRepository _tsrepo;
        public CustomersPoDetailsService(ICustomersPoDetailsRepository _repo, IUintOfWorkFactory uowFac) : base(_repo,uowFac)
        {
            _tsrepo = _repo;
        }

        public async Task<ServiceResult<CustomersPoDetails>> AddOrUpdateAsync(CustomersPoDetails ety)
        {
            if (ety == null)
            {
                return ServiceResult<CustomersPoDetails>.Fail("参数错误!");
            }
            if (ety.ID <= 0)
            {
                return await base.AddAsync(ety);
            }
            else
            {
                if (await _tsrepo.ExistsIdAsync(ety.ID))
                {
                    return await base.UpdateAsync(ety);
                }
                else
                { 
                    return ServiceResult<CustomersPoDetails>.Fail("要更新的数据不存在!");
                }
            }
        }

        public async Task<ServiceResult<bool>> DeleteByCustomerCodeAndSupplierCodeAsync(string customerCode, string supplierCode)
        {
            using (var uow = base._uowFac.CreateUoW())
            {
                try
                {
                    if (string.IsNullOrEmpty(customerCode) || string.IsNullOrEmpty(supplierCode))
                    {
                        return ServiceResult<bool>.Fail("客户代码和供应商代码不能为空!");
                    }
                    var rst = await _tsrepo.DeleteByCustomerCodeWithSupplierCodeAsync(customerCode, supplierCode, uow);
                    await uow.CommitAsync();
                    if (rst >0)
                    {
                        return ServiceResult<bool>.Fail("不存在的数据!");
                    }
                    return  ServiceResult<bool>.Ok(true,"删除成功!");
                }
                catch (Exception)
                {
                    // uow 回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }
            
        }
        
        public async Task<ServiceResult<CustomersPoDetails>> GetByCustomerCodeWithSupplierCodeAsync(string customerCode, string supplierCode)
        {
            if (string.IsNullOrEmpty(customerCode) || string.IsNullOrEmpty(supplierCode))
            {
                return ServiceResult<CustomersPoDetails>.Fail("客户代码和供应商代码不能为空!");
            }
            var rst = await _tsrepo.GetByCustomerCodeWithSupplierCodeAsync(customerCode, supplierCode);
            if (rst == null)
            {
                return ServiceResult<CustomersPoDetails>.Fail("不存在的数据!");
            }
            return ServiceResult<CustomersPoDetails>.Ok(rst);
        }




    }
}
