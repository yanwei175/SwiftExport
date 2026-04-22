using Microsoft.Extensions.DependencyInjection;

namespace SwiftExport.View.FW
{
    /// <summary>
    /// 兼容保留壳（已重构）。
    ///
    /// 原有注册逻辑已按职责拆分到 DI/ 目录下的 partial class DependencyInjection：
    ///   DI/DependencyInjection.cs          ← 总入口
    ///   DI/DependencyInjection.Apis.cs     ← Refit API
    ///   DI/DependencyInjection.Services.cs ← 公共服务
    ///   DI/DependencyInjection.Routing.cs  ← 路由缓存服务
    ///   DI/DependencyInjection.Pages.cs    ← ViewModel + View
    ///
    /// 此类仅作向后兼容保留，内部直接转发至新入口。
    /// 后续新代码请直接调用 services.AddViewServices()，不再使用 DI_View。
    /// </summary>
    [System.Obsolete("请改用 services.AddViewServices()（DependencyInjection.cs 中的新入口）")]
    public static class DI_View
    {
        /// <summary>
        /// 兼容入口，内部转发至 DependencyInjection.AddViewServices()。
        /// </summary>
        public static IServiceCollection AddViewServices(this IServiceCollection services)
        {
            return DependencyInjection.AddViewServices(services);
        }
    }
}
