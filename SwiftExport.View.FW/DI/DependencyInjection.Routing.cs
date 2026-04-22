using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW.Routing;

namespace SwiftExport.View.FW
{
    public static partial class DependencyInjection
    {
        /// <summary>
        /// 注册视图路由相关服务。
        ///
        /// ViewRouteService（Singleton）：
        ///   - 应用启动时从后端 /api/SysViewRouteModel/enabled 拉取路由配置到内存
        ///   - 提供 TryResolve(TargetViewKey → ViewModel Type) 解析能力
        ///   - 找不到时自动降级到静态 ViewRoutes.cs 兜底
        ///   - 可通过管理页面"刷新路由缓存"按钮触发 RefreshAsync() 热更新
        /// </summary>
        private static IServiceCollection AddViewRouting(this IServiceCollection services)
        {
            // 动态路由缓存服务（Singleton：全局唯一，生命周期与应用一致）
            services.AddSingleton<IViewRouteService, ViewRouteService>();

            return services;
        }
    }
}
