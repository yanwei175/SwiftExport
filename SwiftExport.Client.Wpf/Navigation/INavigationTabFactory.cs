namespace SwiftExportClient.Navigation;

/// <summary>为每个新页签创建独立依赖注入作用域，并返回统一拥有该作用域的内容句柄。</summary>
public interface INavigationTabFactory
{
    /// <summary>在新的依赖注入作用域中解析路由对应的 ViewModel。</summary>
    INavigationTabLease Create(NavigationRoute route);
}
