using SwiftExport.Contracts.Apis.SystemApis;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Routing
{
    /// <summary>
    /// 前端视图路由缓存服务（Singleton）
    ///
    /// 工作流程：
    ///   1. 应用启动时调用 RefreshAsync()，从后端 /api/SysViewRouteModel/enabled 拉取全部路由配置
    ///   2. 将 ViewModelTypeName（字符串）反射为 Type，缓存到内存字典
    ///   3. DataDictManagerViewModel 等调用 TryResolve() 解析路由
    ///   4. 若数据库缓存未命中，自动降级到静态 ViewRoutes.cs（兜底兼容）
    ///
    /// 线程安全：
    ///   RefreshAsync 使用 lock 防止并发刷新，TryResolve 只读操作，字典引用替换是原子的。
    /// </summary>
    public class ViewRouteService : IViewRouteService
    {
        private readonly ISysViewRouteApi _api;

        // 运行时路由缓存：TargetViewKey（小写）→ ViewModel Type
        // 使用 volatile 引用替换保证读写的可见性
        private volatile Dictionary<string, Type> _routeCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        // 刷新锁（防止并发同时刷新）
        private readonly object _refreshLock = new object();

        /// <summary>当前缓存是否已完成初始化</summary>
        public bool IsLoaded { get; private set; }

        public ViewRouteService(ISysViewRouteApi api)
        {
            _api = api;
        }

        // ----------------------------------------------------------------
        // 公开方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 解析 TargetViewKey → ViewModel Type。
        /// 优先查数据库缓存，未命中时降级到静态 ViewRoutes.cs。
        /// </summary>
        public bool TryResolve(string targetViewKey, out Type vmType)
        {
            vmType = null;
            if (string.IsNullOrWhiteSpace(targetViewKey)) return false;

            // 优先：数据库缓存
            if (_routeCache.TryGetValue(targetViewKey, out vmType))
                return true;

            // 降级兜底：静态路由表（ViewRoutes.cs）
            // 保证迁移期间旧路由仍然可用
            return ViewRoutes.TryResolve(targetViewKey, out vmType);
        }

        /// <summary>
        /// 从后端 API 拉取路由配置，刷新内存缓存。
        /// 异常时不抛出，记录日志并保留旧缓存，保证应用不崩溃。
        /// </summary>
        public async Task RefreshAsync()
        {
            try
            {
                var result = await _api.GetEnabledRoutesAsync();

                if (!result.Success || result.Data == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewRouteService] 路由加载失败：{result.Message}");
                    IsLoaded = true; // 标记已尝试加载，降级到兜底
                    return;
                }

                // 在本地构建新字典，构建完成后一次性替换，避免读取到中间状态
                var newCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

                foreach (var dto in result.Data)
                {
                    if (string.IsNullOrWhiteSpace(dto.TargetViewKey) ||
                        string.IsNullOrWhiteSpace(dto.ViewModelTypeName))
                        continue;

                    // 反射解析 ViewModel 类型
                    var vmType = ResolveType(dto.ViewModelTypeName, dto.AssemblyName);
                    if (vmType == null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ViewRouteService] 类型解析失败，跳过：{dto.ViewModelTypeName}");
                        continue;
                    }

                    newCache[dto.TargetViewKey] = vmType;
                }

                // 原子替换缓存引用（volatile 写）
                lock (_refreshLock)
                {
                    _routeCache = newCache;
                    IsLoaded = true;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[ViewRouteService] 路由缓存刷新完成，共加载 {newCache.Count} 条");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewRouteService] RefreshAsync 异常：{ex.Message}");
                IsLoaded = true; // 异常也标记为已加载，降级到静态兜底
            }
        }

        // ----------------------------------------------------------------
        // 私有辅助
        // ----------------------------------------------------------------

        /// <summary>
        /// 按类型全名反射查找 Type。
        /// 优先在当前已加载程序集中查找（单程序集场景）；
        /// 若指定了 assemblyName，则按名称加载程序集后再查找（多程序集扩展场景）。
        /// </summary>
        private static Type ResolveType(string typeName, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            // 未指定程序集：遍历当前已加载的所有程序集查找（View.FW 单程序集场景足够）
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType(typeName);
                    if (t != null) return t;
                }
                return null;
            }

            // 指定了程序集：按名称加载后查找
            try
            {
                var asm = Assembly.Load(assemblyName);
                return asm?.GetType(typeName);
            }
            catch
            {
                return null;
            }
        }
    }
}
