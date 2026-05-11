using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Services;

namespace SwiftExport.View.FW
{
    public static partial class DependencyInjection
    {
        /// <summary>
        /// 注册前端公共服务：
        ///   - 弹窗服务（IDialogService）
        ///   - 字典缓存服务（IDictCacheService）
        ///
        /// 均为 Singleton：生命周期与应用一致，全局共享同一实例。
        /// 新增公共前端服务在此文件末尾追加。
        /// </summary>
        private static IServiceCollection AddViewCoreServices(this IServiceCollection services)
        {
            // 弹窗服务：提供统一的确认框、消息框封装
            services.AddSingleton<IDialogService, DialogService>();

            // 字典数据缓存服务：应用启动时预加载常用字典，避免每次下拉都走 API
            services.AddSingleton<IDictCacheService, DictCacheService>();

            // 会话服务：管理退出登录和切换用户，Singleton 保证事件订阅全局唯一
            services.AddSingleton<ISessionService, SessionService>();

            return services;
        }
    }
}
