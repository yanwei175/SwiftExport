using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Services
{
    public interface IDictCacheService
    {
        Task PreloadAsync(IEnumerable<string> dictCodes = null);
        Task RefreshAsync(string dictCode);
        Task RefreshAllAsync();
        IReadOnlyList<SysDictDataDto> GetItems(string dictCode);
        IReadOnlyList<string> GetValues(string dictCode);
    }

    public class DictCacheService : IDictCacheService
    {
        // 核心字典代码列表，预加载时默认使用该列表，如果需要加载其他字典，可以在调用 PreloadAsync 时指定 dictCodes 参数
        private static readonly string[] CoreDictCodes =
        {
            DictCodes.SupplyType,
            DictCodes.Currency,
            DictCodes.CustomerType,
            DictCodes.ProductType
        };

        private readonly ISysDictDataApi _dictDataApi;
        private readonly ConcurrentDictionary<string, List<SysDictDataDto>> _cache = new ConcurrentDictionary<string, List<SysDictDataDto>>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _locker = new SemaphoreSlim(1, 1);

        public DictCacheService(ISysDictDataApi dictDataApi)
        {
            _dictDataApi = dictDataApi;
        }
   /// <summary>
   /// 中文注释
   ///  重新获取指定字典的数据并更新缓存，如果获取失败则保持原有数据不变；如果 dictCodes 为空或 null，则预加载 CoreDictCodes 中的字典数据
   /// </summary>
   /// <param name="dictCodes"></param>
   /// <returns></returns>
        public async Task PreloadAsync(IEnumerable<string> dictCodes = null)
        {
            var targets = (dictCodes ?? CoreDictCodes)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var code in targets)
            {
                await RefreshAsync(code).ConfigureAwait(false);
            }
        }
 

        //刷新所有核心字典数据，适用于需要确保核心字典数据最新的场景，该方法会捕获并记录所有异常，确保调用者不受影响

        public async Task RefreshAllAsync()
        {
            await PreloadAsync(CoreDictCodes).ConfigureAwait(false);
        }
        /// <summary>
        /// 重新获取指定字典的数据并更新缓存，如果获取失败则保持原有数据不变,该方法会捕获并记录所有异常，确保调用者不受影响
        /// </summary>
        /// <param name="dictCode"></param>
        /// <returns></returns>
 
        public async Task RefreshAsync(string dictCode)
        {
            if (string.IsNullOrWhiteSpace(dictCode)) return;

            await _locker.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    var result = await _dictDataApi.GetByCodeAsync(dictCode).ConfigureAwait(false);
                    if (result == null)
                    {
                        Debug.WriteLine($"[DictCache] Refresh '{dictCode}' failed: result is null.");
                        return;
                    }

                    if (!result.Success)
                    {
                        Debug.WriteLine($"[DictCache] Refresh '{dictCode}' failed: {result.Message}");
                        return;
                    }

                    if (result.Data == null)
                    {
                        Debug.WriteLine($"[DictCache] Refresh '{dictCode}' failed: data is null.");
                        return;
                    }

                    var items = result.Data.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
                    _cache.AddOrUpdate(dictCode, items, (k, old) => items);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DictCache] Refresh '{dictCode}' exception: {ex.Message}");
                }
            }
            finally
            {
                _locker.Release();
            }
        }


        /// 获取指定字典的所有数据项，如果缓存中不存在或 dictCode 无效，则返回空列表
        public IReadOnlyList<SysDictDataDto> GetItems(string dictCode)
        {
            if (string.IsNullOrWhiteSpace(dictCode)) return Array.Empty<SysDictDataDto>();

            List<SysDictDataDto> items;
            if (!_cache.TryGetValue(dictCode, out items) || items == null)
            {
                return Array.Empty<SysDictDataDto>();
            }

            return items;
        }

        // 获取指定字典的所有值（DictValue），去除空白和重复项，如果缓存中不存在或 dictCode 无效，则返回空列表
        public IReadOnlyList<string> GetValues(string dictCode)
        {
            return GetItems(dictCode)
                .Select(x => x.DataNameEn)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
