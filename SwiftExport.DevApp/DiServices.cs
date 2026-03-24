using Microsoft.Extensions.DependencyInjection;
using System;

public static class DiServices
{
    private static readonly object _lock = new object();
    private static IServiceProvider _provider;
    public static IServiceProvider Provider
    {
        get
        {
            if (_provider == null)
                throw new InvalidOperationException("ServiceProvider 尚未初始化，请先调用 AppServices.Init()");
            return _provider;
        }
    }

    public static void Init(IServiceProvider provider)
    {
        if (_provider != null)
            return; // 防止重复初始化

        lock (_lock)
        {
            if (_provider == null)
                _provider = provider;
        }
    }
    /// <summary>
    /// 获取服务的快捷方法
    /// </summary>
    public static T GetService<T>()
    {
        // GetRequiredService 如果找不到服务会抛出异常，比 GetService (返回 null) 更容易排查错误
        return Provider.GetRequiredService<T>();
    }

    /// <summary>
    /// 重置内部 Provider，便于 ThisAddIn.Shutdown 时释放单例持有的资源（例如 Excel COM 对象）。
    /// </summary>
    public static void Reset()
    {
        _provider = null;
    }








}

