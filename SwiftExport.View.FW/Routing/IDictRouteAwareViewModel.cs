namespace SwiftExport.View.FW.Routing
{
    /// <summary>
    /// 字典路由上下文感知接口。
    /// 用于在路由命中后，把当前选中的 DictCode/显示名注入到目标 ViewModel。
    /// </summary>
    public interface IDictRouteAwareViewModel
    {
        /// <summary>
        /// 注入当前路由上下文。
        /// </summary>
        /// <param name="dictCode">字典编码</param>
        /// <param name="dictDisplayName">字典显示名称</param>
        void SetRouteContext(string dictCode, string dictDisplayName);
    }
}
