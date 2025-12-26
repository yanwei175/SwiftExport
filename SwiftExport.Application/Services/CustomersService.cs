using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class CustomersService : BaseService<Customers> , ICustomersService
    {
        private readonly ICustomersRepository  _thisrepo;
        public CustomersService(ICustomersRepository repo, 
            IUintOfWorkFactory _uowFac) : base(repo, _uowFac)
        {
            _thisrepo = repo;
        }

        public async Task<ServiceResult<Customers>> GetByCustomerCodeAsync(string CustomerCode)
        {
            if (CustomerCode == null) {
                return ServiceResult<Customers>.Fail("客户代码不能为空");
            }
            var rst= await _thisrepo.GetByCustomerCodeAsync(CustomerCode);
            return ServiceResult<Customers>.Ok(rst);
        }

        public async Task<ServiceResult<CUDResult>> BatchCUDBy_UQ_CustomersCodeAsync(IEnumerable<Customers> createList, IEnumerable<Customers> updateList, IEnumerable<Customers> deleteList)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                int totalRowsAffected = 0;
                CUDResult cUDResult = new CUDResult();
                try
                {
                    List<string> WrongAdd = new List<string>();
                    List<string> NeedAdd = new List<string>();
                    List<int> WrongUpdate = new List<int>();
                    List<int> NeedUpdate = new List<int>();
                    List<int> WrongDelete = new List<int>();  
                    List<int> NeedDelete = new List<int>();
                    // 1. 检查新增的数据
                    if (createList != null && createList.Any())
                    {
                        foreach (var item in createList)
                        {
                            if (item.CustomerCode == null || item.CustomerCode.Trim() == "")
                                return ServiceResult<CUDResult>.Fail("客户代码不能为空"); 
                            NeedAdd.Add(item.CustomerCode);
                        }

                        IEnumerable<string> 已存在客户代码= await _thisrepo.GetExistingCustomerCodesAsync(NeedAdd);
                        if (已存在客户代码.Any())
                            WrongAdd.AddRange(已存在客户代码);
                    }

                    // 2. 检查更新的数据
                    if (updateList != null && updateList.Any())
                    {
                        foreach (var customer in updateList)
                        {
                            if (customer.ID <= 0)
                                WrongUpdate.Add(customer.ID);
                            NeedUpdate.Add(customer.ID);
                        }

                        var existingIds = await _repo.GetExistingIdsAsync(NeedUpdate);
                        //如果所有的都不存在
                        if (existingIds == null || !existingIds.Any())
                        {
                            WrongUpdate.AddRange(NeedUpdate);
                        }
                        else
                        {
                            // int 类型默认使用数值比较，不需要额外的比较器。
                            var allidSet = new HashSet<int>(NeedUpdate);
                            var existingidSet = new HashSet<int>(existingIds);

                            // 使用 Except 方法计算不存在的集合。
                            // Except 方法在 HashSet 上执行效率最高，因为它使用了底层的哈希查找。
                            var nonExistentIds = allidSet.Except(existingidSet).ToList();
                            if (nonExistentIds.Any())
                                WrongUpdate.AddRange(nonExistentIds);
                        }
                    }

                    // 3. 检查删除数据
                    if (deleteList != null && deleteList.Any())
                    {
                        foreach (var entity in deleteList)
                        {
                            // 校验实体ID是否有效
                            if (entity.ID <= 0)
                            {
                                WrongDelete.Add(entity.ID);
                            }
                            NeedDelete.Add(entity.ID);
                        }
                    }

                    // 如果有错误，则返回错误信息
                    if (WrongAdd.Any() || WrongUpdate.Any() || WrongDelete.Any())
                    {
                        // 创建一个自定义异常，包含错误信息
                        var errorMessage = new StringBuilder();
                        if (WrongAdd.Any())
                            errorMessage.AppendLine($"新创建的实体不能有ID,ID会由数据库自动创建。ID: {string.Join(", ", WrongAdd)}");
                        if (WrongUpdate.Any())
                            errorMessage.AppendLine($"批量更新列表包含无效ID，事务已终止。ID: {string.Join(", ", WrongUpdate)}");
                        if (WrongDelete.Any())
                            errorMessage.AppendLine($"删除操作列表包含无效ID的实体。实体ID: {string.Join(", ", WrongDelete)}");

                        return ServiceResult<CUDResult>.Fail(errorMessage.ToString());
                    }

                    //uow 开始写数据库
                    //新增
                    if (createList.Any())
                    {
                        cUDResult.AddIIds = (await _thisrepo.AddRangeAsync(createList, uow)).ToList();
                        totalRowsAffected += cUDResult.AddIIds.Count;
                    }

                    //更新
                    if (updateList.Any())
                    {
                        totalRowsAffected += await _thisrepo.UpdateRangeByIdsAsync(updateList, uow);
                        cUDResult.UpdateIIds = NeedUpdate;
                    }

                    //删除
                    if (NeedDelete.Any())
                    {
                        totalRowsAffected += await _thisrepo.DeleteRangeByIdsAsync(NeedDelete, uow);
                        cUDResult.DeleteIIds = NeedDelete;
                    }
                    // uow 提交事务
                    await uow.CommitAsync();
                    cUDResult.AffectedRowsTotal = totalRowsAffected;
                    return ServiceResult<CUDResult>.Ok(cUDResult);
                }
                catch (Exception)
                {
                    // 回滚整个事务
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<bool> ExistByCustomerCode(string customerCode)
        {
           return await _thisrepo.ExistByCustomerCode(customerCode);
        }

        public async Task<ServiceResult<IReadOnlyList<string>>> GetNonExistingCustomerCodesAsync(IEnumerable<string> customerCodes)
        {
            if (customerCodes == null || !customerCodes.Any())
                return ServiceResult<IReadOnlyList<string>>.Fail("客户代码列表不能为空");

            if (customerCodes.Any(code => string.IsNullOrWhiteSpace(code)))
            {
                // 说明集合里至少有一个是空的或者全是空格
                return ServiceResult<IReadOnlyList<string>>.Fail("客户代码列表中包含空值。");
            }

            // 1. 调用 Repository 获取数据库中已存在的代码
            var existingCodes = await _thisrepo.GetExistingCustomerCodesAsync(customerCodes);

            if (existingCodes == null || !existingCodes.Any())
                return ServiceResult<IReadOnlyList<string>>.Ok(customerCodes.ToList());


                // 注意：HashSet<T> 的 Except 方法效率最高，且能处理大小写问题（如果使用 StringComparer）
                var allCodesSet = new HashSet<string>(customerCodes, StringComparer.OrdinalIgnoreCase);
                var existingCodesSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
                // 从所有代码中排除已存在的代码 = 不存在的代码
                var nonExistentCodes = allCodesSet.Except(existingCodesSet).ToList();
                return ServiceResult<IReadOnlyList<string>>.Ok(nonExistentCodes);

        }

    }
}
