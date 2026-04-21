using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 货币汇率字典服务
    /// 继承 BaseService，提供标准 CRUD / BatchSave / 分页查询能力
    /// </summary>
    public class SysDicCurrencyExchangeService(ISysDicCurrencyExchangeRepository repository, ServiceBaseContext context)
        : BaseService<SysDicCurrencyExchange, SysDicCurrencyExchangeDto>(repository, context), ISysDicCurrencyExchangeService
    {
        /// <summary>
        /// 删除策略：汇率字典使用软删除（IsDeleted 标记）
        /// </summary>
        protected override bool IsSoftDelete => true;

        /// <summary>
        /// 新增前校验：
        /// 1. 源币种/目标币种必填
        /// 2. 源币种不能等于目标币种
        /// 3. 精度范围 0~6
        /// 4. 汇率必须大于 0
        /// </summary>
        protected override Task<string?> ValidateBeforeAddAsync(List<SysDicCurrencyExchange> entities)
        {
            return Task.FromResult(ValidateEntities(entities));
        }

        /// <summary>
        /// 更新前校验：复用新增校验规则
        /// </summary>
        protected override Task<string?> ValidateBeforeUpdateAsync(List<SysDicCurrencyExchange> entities)
        {
            return Task.FromResult(ValidateEntities(entities));
        }

        /// <summary>
        /// 统一实体校验（新增/更新共用）
        /// </summary>
        private string? ValidateEntities(List<SysDicCurrencyExchange> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return null;
            }

            // 逐行校验基础字段
            foreach (var item in entities)
            {
                if (string.IsNullOrWhiteSpace(item.FromCurrencyCode))
                {
                    return "源货币代码(FromCurrencyCode)不能为空";
                }

                if (string.IsNullOrWhiteSpace(item.ToCurrencyCode))
                {
                    return "目标货币代码(ToCurrencyCode)不能为空";
                }

                if (string.Equals(item.FromCurrencyCode, item.ToCurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    return "源货币代码与目标货币代码不能相同";
                }

                if (item.Precision < 0 || item.Precision > 6)
                {
                    return "汇率精度(Precision)范围必须在 0~6 之间";
                }

                if (item.Rate == null || item.Rate <= 0)
                {
                    return "汇率(Rate)必须大于 0";
                }
            }

            // 批次内去重校验：同一条汇率关系不能重复提交
            var duplicatePair = entities
                .GroupBy(x => new
                {
                    From = (x.FromCurrencyCode ?? string.Empty).Trim().ToUpperInvariant(),
                    To = (x.ToCurrencyCode ?? string.Empty).Trim().ToUpperInvariant()
                })
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicatePair != null)
            {
                return $"存在重复的汇率关系：{duplicatePair.Key.From} -> {duplicatePair.Key.To}";
            }

            return null;
        }
    }
}
