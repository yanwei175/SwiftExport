using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Services
{
    public interface IDictCacheService
    {
        /// <summary>
        ///  预加载：如果指定了 codes 则按需刷新，否则执行全量刷新
        /// </summary>
        /// <param name="dictCodes"></param>
        /// <returns></returns>
        Task PreloadAsync(IEnumerable<string> dictCodes = null);

        /// <summary>
        ///  局部刷新：仅重新获取单个字典
        /// </summary>
        /// <param name="dictCode"></param>
        /// <returns></returns>
        Task RefreshAsync(string dictCode);

        /// <summary>
        /// 一次性获取所有字典明细，并在内存中完成分组
        /// </summary>
        /// <returns></returns>
        Task RefreshAllAsync();

        /// <summary>
        /// 获取字典明细
        /// </summary>
        /// <param name="dictCode"></param>
        /// <returns></returns>
        IReadOnlyList<SysDictDataDto> GetItems(string dictCode);
        /// <summary>
        /// 返回指定字典的所有值（DataNameEn），去除空白和重复项，如果缓存中不存在或 dictCode 无效，则返回空列表
        /// </summary>
        /// <param name="dictCode"></param>
        /// <returns></returns>
        IReadOnlyList<string> GetEnValues(string dictCode);
        IReadOnlyList<string> GetZhValues(string dictCode);

    }

    public class DictCacheService : IDictCacheService
    {
        private readonly ISysDictDataApi _dictDataApi;
        private readonly ConcurrentDictionary<string, List<SysDictDataDto>> _cache = new ConcurrentDictionary<string, List<SysDictDataDto>>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _locker = new SemaphoreSlim(1, 1);
        public DictCacheService(ISysDictDataApi dictDataApi)
        {
            _dictDataApi = dictDataApi;
        }

        /// <summary>
        /// 预加载：如果指定了 codes 则按需刷新，否则执行全量刷新
        /// </summary>
        public async Task PreloadAsync(IEnumerable<string> dictCodes = null)
        {
            if (dictCodes == null || !dictCodes.Any())
            {
                await RefreshAllAsync().ConfigureAwait(false);
            }
            else
            {
                // 如果只想要特定的几个字典，可以循环刷新，也可以简单执行全量刷新
                foreach (var code in dictCodes.Distinct())
                {
                    await RefreshAsync(code).ConfigureAwait(false);
                }
            }
        }


        /// <summary>
        /// 一次性获取所有字典明细，并在内存中完成分组
        /// </summary>
        public async Task RefreshAllAsync()
        {
            await _locker.WaitAsync().ConfigureAwait(false);
            try
            {
                Debug.WriteLine("[DictCache] Starting global refresh...");
                var result = await _dictDataApi.GetAllAsync().ConfigureAwait(false);

                if (result?.Success == true && result.Data != null)
                {
                    // 1. 在内存中进行分组和排序
                    var groupedData = result.Data
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Uid)
                        .GroupBy(x => x.DictCode, StringComparer.OrdinalIgnoreCase);

                    // 2. 清空当前缓存并批量填充
                    _cache.Clear();
                    foreach (var group in groupedData)
                    {
                        if (!string.IsNullOrWhiteSpace(group.Key))
                        {
                            _cache.TryAdd(group.Key, group.ToList());
                        }
                    }
                    Debug.WriteLine($"[DictCache] Global refresh completed. {_cache.Count} dictionaries loaded.");
                }
                else
                {
                    Debug.WriteLine($"[DictCache] Global refresh failed: {result?.Message ?? "Unknown error"}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DictCache] RefreshAllAsync exception: {ex.Message}");
            }
            finally
            {
                _locker.Release();
            }
        }

        /// <summary>
        /// 局部刷新：仅重新获取单个字典
        /// </summary>
        public async Task RefreshAsync(string dictCode)
        {
            if (string.IsNullOrWhiteSpace(dictCode)) return;

            await _locker.WaitAsync().ConfigureAwait(false);
            try
            {
                var result = await _dictDataApi.GetByCodeAsync(dictCode).ConfigureAwait(false);
                if (result?.Success == true && result.Data != null)
                {
                    var items = result.Data.OrderBy(x => x.SortOrder).ThenBy(x => x.Uid).ToList();
                    _cache.AddOrUpdate(dictCode, items, (k, old) => items);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DictCache] Refresh '{dictCode}' exception: {ex.Message}");
            }
            finally
            {
                _locker.Release();
            }
        }
        /// <summary>
        /// 获取字典明细
        /// </summary>
        public IReadOnlyList<SysDictDataDto> GetItems(string dictCode)
        {
            if (string.IsNullOrWhiteSpace(dictCode)) return Array.Empty<SysDictDataDto>();

            if (!_cache.TryGetValue(dictCode, out List<SysDictDataDto> items) || items == null)
            {
                return Array.Empty<SysDictDataDto>();
            }

            return items;
        }
        /// <summary>
        /// 获取字典值的字符串集合
        /// </summary>
        public IReadOnlyList<string> GetEnValues(string dictCode)
        {
            return GetItems(dictCode)
                            .Select(x => x.DataNameEn) // 统一取英文名
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }

        /// <summary>
        /// 获取字典值的字符串集合
        /// </summary>
        public IReadOnlyList<string> GetZhValues(string dictCode)
        {
            return GetItems(dictCode)
                            .Select(x => x.DataNameZh) // 中文名
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }



    }
}
