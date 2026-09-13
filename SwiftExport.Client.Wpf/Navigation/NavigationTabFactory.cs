using Microsoft.Extensions.DependencyInjection;

namespace SwiftExportClient.Navigation;

/// <summary>使用独立 IServiceScope 创建页签 ViewModel，使 scoped 依赖按页签隔离。</summary>
public sealed class NavigationTabFactory(IServiceScopeFactory scopeFactory) : INavigationTabFactory
{
    /// <inheritdoc />
    public INavigationTabLease Create(NavigationRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var scope = scopeFactory.CreateScope();
        try
        {
            var viewModel = scope.ServiceProvider.GetRequiredService(route.ViewModelType);
            return new ScopedNavigationTabLease(viewModel, scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class ScopedNavigationTabLease(object viewModel, IServiceScope scope) : INavigationTabLease
    {
        private IServiceScope? _scope = scope;

        public object ViewModel { get; } = viewModel;

        /// <summary>仅释放作用域，由容器统一释放其中的 ViewModel 和 scoped 依赖。</summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _scope, null)?.Dispose();
        }
    }
}
