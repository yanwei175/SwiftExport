using System;

namespace SwiftExport.View.FW.Routing
{
    /// <summary>
    /// 前端视图路由服务接口（客户端缓存层）
    ///
    /// 职责：
    ///   封装"TargetViewKey → ViewModel Type"的解析逻辑，
    ///   内部优先从数据库路由缓存查找，找不到时降级到静态 ViewRoutes 兜底。
    ///
    /// 使用方：
    ///   DataDictManagerViewModel、主界面路由器等需要动态加载页面的地方。
    /// </summary>
    public interface IViewRouteService
    {
        /// <summary>
        /// 根据 TargetViewKey 解析对应的 ViewModel 类型。
        /// </summary>
        /// <param name="targetViewKey">路由键</param>
        /// <param name="vmType">命中时输出 ViewModel 类型，未命中时为 null</param>
        /// <returns>是否成功解析</returns>
        bool TryResolve(string targetViewKey, out Type vmType);

        /// <summary>
        /// 从后端 API 重新加载路由配置并刷新缓存。
        /// 通常在应用启动或路由配置变更后调用。
        /// </summary>
        System.Threading.Tasks.Task RefreshAsync();

        /// <summary>
        /// 当前缓存是否已完成初始化（区分"未加载"和"已加载但为空"）。
        /// </summary>
        bool IsLoaded { get; }
    }
}
