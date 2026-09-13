namespace SwiftExportClient.Navigation;

/// <summary>封装页签 ViewModel 及其作用域的唯一释放入口。</summary>
public interface INavigationTabLease : IDisposable
{
    /// <summary>获取供 DataTemplate 呈现的页签 ViewModel。</summary>
    object ViewModel { get; }
}
