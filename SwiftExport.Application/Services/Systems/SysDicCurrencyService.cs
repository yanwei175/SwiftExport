using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 货币主数据服务
    /// 核心职责：
    /// 1. 继承泛型 CRUD（新增/修改/删除/查询）
    /// 2. 扩展本位币唯一性校验（批量保存时的前置拦截）
    /// 3. 提供"获取本位币"、"获取启用列表"等业务查询
    /// </summary>
    public class SysDicCurrencyService(ISysDicCurrencyRepository repository, ServiceBaseContext context,ISysDicCurrencyExchangeRepository exchangeRepository)
        : BaseService<SysDicCurrency, SysDicCurrencyDto>(repository, context), ISysDicCurrencyService
    {


        public override async Task<ServiceResult<int>> UpdateAsync(SysDicCurrencyDto newSysDicCurrencyDto)
        {
            var entity = newSysDicCurrencyDto.Adapt<SysDicCurrency>();
            var rst = await _repository.UpdateAsync(entity);
            if (rst == 0)
            {
                Logger.LogWarning("更新失败，未找到匹配的货币代码：{CurrencyCode}", newSysDicCurrencyDto.CurrencyCode);
                return ServiceResult<int>.Fail("更新失败，未找到匹配的货币代码");
            }
            return ServiceResult<int>.Ok(rst, "成功更新货币信息");
        }

        // ============================================================
        // 删除策略：货币主数据使用软删除（IsDeleted 标记）
        // ============================================================
        protected override bool IsSoftDelete => true;

        // ============================================================
        // 业务查询扩展
        // ============================================================
        public async Task<ServiceResult<int>> SetNewBaseCurrencyAsync(SysDicCurrencyDto newSysDicCurrencyDto)
        {
            _session.Transaction = _session.Connection.BeginTransaction();
            try
            {
                var entity = newSysDicCurrencyDto.Adapt<SysDicCurrency>();
                await repository.ResetAllBaseCurrencyAsync();
                entity.IsBase = true;
                var rst = await _repository.UpdateAsync(entity);
                //var rst = 1;
                if (rst == 0)
                {
                    _session.Transaction.Rollback();
                    Logger.LogWarning("重置本位币失败，未找到匹配的货币代码：{CurrencyCode}", newSysDicCurrencyDto.CurrencyCode);
                    return ServiceResult<int>.Fail("重置本位币失败，未找到匹配的货币代码");
                }
                _session.Transaction.Commit();
                 return ServiceResult<int>.Ok(rst, "成功重置本位币");
            }
            catch (Exception ex)
            {
                _session.Transaction.Rollback();
                if (Logger.IsEnabled(LogLevel.Error)) Logger.LogError(ex, "[{EntityName}] 保存时发生错误，事务已回滚！", EntityName);
                throw;
            }
            finally
            {
                _session.Transaction.Dispose();
                _session.Transaction = null;
            }

        }

        /// <summary>
        /// 获取当前本位币
        /// </summary>
        public async Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync()
        {
            // 直接用索引器访问第一个元素，避免在 List<T> 上用 FirstOrDefault()
            var list = await repository.GetListAsync(new { IsBase = true, IsDeleted = false });
            SysDicCurrency? baseCurrency = (list.Count > 0) ? list[0] : null;

            if (baseCurrency == null)
            {
                Logger.LogWarning("查询失败：系统中未设置本位币");
                return ServiceResult<SysDicCurrencyDto>.Fail("系统中未设置本位币");
            }

            return ServiceResult<SysDicCurrencyDto>.Ok(baseCurrency.Adapt<SysDicCurrencyDto>());
        }

        /// <summary>
        /// 获取所有启用的货币（用于下拉选择器）
        /// </summary>
        public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync()
        {
            var list = await repository.GetListAsync(new { IsEnabled = true, IsDeleted = false });
            var dtos = list.Adapt<IReadOnlyList<SysDicCurrencyDto>>();
            return ServiceResult<IReadOnlyList<SysDicCurrencyDto>>.Ok(dtos);
        }





        // ============================================================
        // 新增前校验：
        // 1. CurrencyCode 必填 + 不能重复
        // 2. 传入列表中最多只能有 1 个 IsBase = true
        // 3. 若新增行中含 IsBase = true，数据库已有本位币则报错
        // ============================================================
        protected override async Task<string?> ValidateBeforeAddAsync(List<SysDicCurrency> entities)
        {
            // —— 校验 1：CurrencyCode 必填 ——
            var emptyCodeItems = entities.Where(x => string.IsNullOrWhiteSpace(x.CurrencyCode)).ToList();
            if (emptyCodeItems.Count != 0)
            {
                return "货币代码(CurrencyCode)不能为空";
            }

            // —— 校验 2：传入批次内 IsBase 唯一性 ——
            var baseInBatch = entities.Where(x => x.IsBase).ToList();
            if (baseInBatch.Count > 1)
            {
                var codes = string.Join(", ", baseInBatch.Select(x => x.CurrencyCode));
                return $"一次提交中只能有一个本位币，当前批次中有 {baseInBatch.Count} 个 IsBase=true：[{codes}]";
            }

            // —— 校验 3：若本次新增含本位币，检查数据库是否已存在本位币 ——
            if (baseInBatch.Count == 1)
            {
                var existingBaseIds = await repository.GetBaseIsBaseCurrencyIdsAsync();
                if (existingBaseIds.Count > 0)
                {
                    return $"系统中已存在本位币（ID: {string.Join(", ", existingBaseIds)}），无法再设置新的本位币。请先取消原有本位币设置后再操作。";
                }
            }

            // —— 校验 4：CurrencyCode 在数据库中不能重复 ——
            var codes2 = entities.Select(x => x.CurrencyCode!).ToList();
            var existedCodes = await repository.GetExistingCurrencyCodesAsync(codes2);
            if (existedCodes.Count > 0)
            {
                return $"以下货币代码已存在，拒绝新增：{string.Join(", ", existedCodes)}";
            }

            return null; // null = 校验全部通过
        }

        // ============================================================
        // 修改前校验：
        // 1. 传入列表中最多只能有 1 个 IsBase = true
        // 2. 若某行要更新为 IsBase = true，数据库中不能有除了这行自身以外的本位币
        // ============================================================
        protected override async Task<string?> ValidateBeforeUpdateAsync(List<SysDicCurrency> entities)
        {
            // —— 校验 1：传入批次内 IsBase 唯一性 ——
            var baseInBatch = entities.Where(x => x.IsBase).ToList();
            if (baseInBatch.Count > 1)
            {
                var codes = string.Join(", ", baseInBatch.Select(x => x.CurrencyCode));
                return $"一次提交中只能有一个本位币，当前批次中有 {baseInBatch.Count} 个 IsBase=true：[{codes}]";
            }

            // —— 校验 2：如果某行要被设为本位币，检查数据库中是否有其他本位币 ——
            if (baseInBatch.Count == 1)
            {
                // 排除本次要更新的这些行自身（避免误判：原来它就是本位币，现在只是更新其他字段）
                var updatingIds = entities.Select(x => x.Id).ToList();
                var existingBaseIds = await repository.GetBaseIsBaseCurrencyIdsAsync(updatingIds);

                if (existingBaseIds.Count > 0)
                {
                    return $"系统中已存在本位币（ID: {string.Join(", ", existingBaseIds)}），无法将多个货币同时设为本位币。请先取消原有本位币设置后再操作。";
                }
            }

            return null; // null = 校验全部通过
        }

        public async Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrencyAsync()
        {
            
            var currency = await GetBaseCurrencyAsync();
            if (!currency.Success)
            {
               
                var id = currency.Data?.Id ?? 0;
                if (id == 0) { return ServiceResult<SysDicCurrencyDetailDto>.Fail(currency.Message); }

                var detailDto = new SysDicCurrencyDetailDto { Currency = currency.Data };

                var exchange = await exchangeRepository.GetListAsync(id);
                var exchangeDto= exchange?.Adapt<List<SysDicCurrencyExchangeDto>>();
                if (exchangeDto != null)
                {
                    detailDto.CurrencyRates = exchangeDto;
                }
               return ServiceResult<SysDicCurrencyDetailDto>.Ok(detailDto);
           }
            else
            {
                return ServiceResult<SysDicCurrencyDetailDto>.Fail(currency.Message);
            }

                
       }
    }
}
