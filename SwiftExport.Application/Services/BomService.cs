using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using SwiftExport.Infrastructure.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class BomService : BaseService<BOM>, IBOMService
    {
        private readonly IBomRepository repo;
        private readonly IProductRepository _pdrepo;
        public BomService(IUintOfWorkFactory _uowFac, IBomRepository _repo, IProductRepository pdrepo) : base(_repo, _uowFac)
        {
            repo = _repo;
            _pdrepo = pdrepo;
        }
        //新增一条BOM
        public async Task<int> AddBomItemAsync(BOM bom)
        {
            using (var uow = _uowFac.CreateUoW())
            {
              try
                {
                        var results = await repo.AddAsync(bom,uow);
                        await uow.CommitAsync();
                        return results;
                }
                catch (Exception)
                {
                    // 【修正】关键：发生错误时回滚事务
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        //批量新增BOM
        public async Task<IEnumerable<int>> AddBomRangeAsync(IEnumerable<BOM> items)
        {
            using(var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var results = await repo.AddRangeAsync(items, uow);

                    // 【修正】提交事务
                    await uow.CommitAsync();

                    return results;
                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }

        }
        //根据BomNo删除单条BOM
        public async Task<int> DeleteBomByBomNoAsync(string bomNo)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await repo.DeleteBomByBomNoAsync(bomNo, uow);
                    // 【修正】提交事务
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【修正】失败时回滚事务
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
  
        public async Task<int> DeleteBomRangeByIdsAsync(IEnumerable<int> ids)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await repo.DeleteRangeByIdsAsync(ids, uow);

                    // 【修正】提交事务
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【修正】失败时回滚事务
                    await uow.RollbackAsync();
                    throw;
                }
            }

        }

        public Task<IEnumerable<BOM>> GetBomByBomNoAsync(string bomNo)
        {
            return repo.GetBomByBomNoAsync(bomNo);
        }

        public async Task<int> UpdateBomItemNoAsync(BOM bom)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await repo.UpdateAsync(bom, uow); // 假设 IRepository 接口方法名

                    // 【修正】提交事务
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【修正】失败时回滚事务
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<int> UpdateBomRangeAsync(IEnumerable<BOM> items)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await repo.UpdateRangeByIdsAsync(items, uow);

                    // 【修正】提交事务
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【修正】失败时回滚事务
                    await uow.RollbackAsync();
                    throw;
                }
            }

        }

        public async Task<bool> ValidateBOMAsync(string productSKU)
        {
            // 0. 获取产品的 BOM 号
            Product pd = await _pdrepo.Get单产品BySku(productSKU);
            if (pd == null || string.IsNullOrEmpty(pd.BomNo))
                return false;

            string bomNo = pd.BomNo;

            // 1. 查询该产品的 BOM 项
            var bomItems = await repo.GetBomByBomNoAsync(bomNo);
            if (bomItems == null || !bomItems.Any())
                return false; // 没有 BOM 数据，视为不合法

            // 2. 数量合法性
            if (bomItems.Any(i => i.Qty <= 0))
                return false;

            // 3. 子件存在性
            foreach (var item in bomItems)
            {
                var exists = await _pdrepo.ExistsIdAsync(Convert.ToInt32(item.SkuID));
                if (!exists) return false;
            }

            // 4. 重复项检查
            if (bomItems.GroupBy(i => i.ProductSKU).Any(g => g.Count() > 1))
                return false;

            // 5. 循环依赖检查（DFS，异步）
            var visited = new HashSet<string>();

            async Task<bool> HasCycleAsync(string currentBomNo)
            {
                if (!visited.Add(currentBomNo)) return true; // 已访问过，说明有环

                var children = await repo.GetBomByBomNoAsync(currentBomNo);
                foreach (var child in children)
                {
                    // 递归检查子件的 BOM
                    if (await HasCycleAsync(child.BomNo)) return true;
                }

                visited.Remove(currentBomNo);
                return false;
            }

            if (await HasCycleAsync(bomNo)) return false;

            return true; // 所有检查通过
        }

        public async Task<int> SyncBomItemsAsync(IEnumerable<BOM> items)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await repo.SyncBomItemsAsync(items, uow);

                    // 【修正】提交事务
                    await uow.CommitAsync();
                    return affectedRows;
                }
                catch (Exception)
                {
                    // 【修正】失败时回滚事务
                    await uow.RollbackAsync();
                    throw;
                }
            }

        }
    }
}
