using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class BomService : IBOMService
    {
        private readonly IBomRepository _repo;
        private readonly IProductRepository _pdrepo;
        public BomService(IBomRepository repo, IProductRepository pdrepo)
        {
            _repo = repo;
            _pdrepo = pdrepo;
        }
        //新增一条BOM
        public async Task<int> AddBomItemAsync(BOM bom)
        {
            try
                {
                return await _repo.AddSingleReturnNewIDAsync(bom);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目添加错误 item: {ex.Message}", ex);
            }
        }
        //批量新增BOM
        public async Task<IEnumerable<BOM>> AddBomRangeAsync(IEnumerable<BOM> items)
        {
            try
                {
                return await _repo.AddRangeAsync(items);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目批量添加错误 item: {ex.Message}", ex);
            }
        }
        //根据BomNo删除单条BOM
        public async Task<int> DeleteBomByBomNoAsync(string bomNo)
        {
           try
                {
                return await _repo.DeleteBomByBomNoAsync(bomNo);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目删除错误 item: {ex.Message}", ex);
            }
        }
        //根据ID删除单条BOM
        public Task<int> DeleteBomItemByIdAsync(int id)
        {
            try
            {
                return _repo.DeleteSingleByIdAsync(id);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目删除错误 item: {ex.Message}", ex);
            }
        }

        public async Task<int> DeleteBomRangeByIdsAsync(IEnumerable<int> ids)
        {
            try
            {
                return await _repo.DeleteRangeByIdsAsync(ids);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目获取错误 item: {ex.Message}", ex);
            }
        }

        public Task<IEnumerable<BOM>> GetBomByBomNoAsync(string bomNo)
        {
            try
                {
                return _repo.GetBomByBomNoAsync(bomNo);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目获取错误 item: {ex.Message}", ex);
            }
        }

        public async Task<int> UpdateBomItemNoAsync(BOM bom)
        {
            try
                {
                return await _repo.UpdateSingleAsync(bom);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目更新错误 item: {ex.Message}", ex);
            }
        }

        public async Task<int> UpdateBomRangeAsync(IEnumerable<BOM> items)
        {
            try
            {
                return await _repo.UpdateRangeAsync(items);
            }
            catch (Exception ex)
            {
                throw new Exception($"BOM条目更新错误 item: {ex.Message}", ex);
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
            var bomItems = await _repo.GetBomByBomNoAsync(bomNo);
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

                var children = await _repo.GetBomByBomNoAsync(currentBomNo);
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



        public async Task<int> 同步BomItemsAsync(IEnumerable<BOM> items)
        {
            return await _repo.同步BomItemsAsync(items);
        }
    }
}
