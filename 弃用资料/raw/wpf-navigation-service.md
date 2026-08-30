# Skill: WPF NavigationService

## 适用范围

本 skill 用于在 WPF + CommunityToolkit.Mvvm 项目中设计页面导航服务。

推荐用于：

- Shell / MainWindow 中切换页面
- `ContentControl + CurrentViewModel`
- ViewModel 之间通过 route 导航
- 避免 ViewModel 直接依赖具体 View

不建议把业务流程、保存逻辑、权限判断都塞进 NavigationService。

## 推荐模式

推荐使用：

```text
ShellViewModel.Navigation.CurrentViewModel
    -> ContentControl.Content
    -> DataTemplate 根据 ViewModel 类型匹配 View
```

不要在 ViewModel 中直接：

```csharp
new SomeWindow().Show();
new SomePage();
```

## 接口

```csharp
using System.ComponentModel;

namespace YourApp.Client.Services;

public interface INavigationService : INotifyPropertyChanged
{
    object? CurrentViewModel { get; }
    string? CurrentRoute { get; }

    void Register<TViewModel>(string route)
        where TViewModel : class;

    void NavigateTo(string route);

    bool CanNavigateTo(string route);
}
```

## 实现

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace YourApp.Client.Services;

public sealed partial class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, Type> _routes = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private object? currentViewModel;

    [ObservableProperty]
    private string? currentRoute;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public void Register<TViewModel>(string route)
        where TViewModel : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        _routes[route] = typeof(TViewModel);
    }

    public bool CanNavigateTo(string route)
    {
        return !string.IsNullOrWhiteSpace(route) && _routes.ContainsKey(route);
    }

    public void NavigateTo(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        if (!_routes.TryGetValue(route, out var viewModelType))
        {
            throw new InvalidOperationException($"未注册导航路由：{route}");
        }

        CurrentViewModel = _services.GetRequiredService(viewModelType);
        CurrentRoute = route;
    }
}
```

## DI 注册

```csharp
services.AddSingleton<INavigationService, NavigationService>();

services.AddTransient<HomeViewModel>();
services.AddTransient<ExamplePageViewModel>();
```

路由注册可以放在应用启动阶段：

```csharp
var navigation = services.BuildServiceProvider()
    .GetRequiredService<INavigationService>();

navigation.Register<HomeViewModel>("Home");
navigation.Register<ExamplePageViewModel>("ExamplePage");
navigation.NavigateTo("Home");
```

更推荐在项目中封装一个启动初始化方法，避免到处手写路由。

## ShellViewModel 用法

```csharp
public sealed class ShellViewModel
{
    public INavigationService Navigation { get; }

    public ShellViewModel(INavigationService navigation)
    {
        Navigation = navigation;
    }
}
```

## Shell XAML

```xml
<ContentControl Content="{Binding Navigation.CurrentViewModel}" />
```

## DataTemplate 映射

```xml
<Application.Resources>
    <DataTemplate DataType="{x:Type vm:HomeViewModel}">
        <views:HomeView />
    </DataTemplate>

    <DataTemplate DataType="{x:Type vm:ExamplePageViewModel}">
        <views:ExamplePageView />
    </DataTemplate>
</Application.Resources>
```

## 页面 ViewModel 用法

```csharp
private readonly INavigationService _navigationService;

public ExamplePageViewModel(INavigationService navigationService)
{
    _navigationService = navigationService;
}

private void GoBack()
{
    _navigationService.NavigateTo("Home");
}
```

## 使用规则

- NavigationService 只负责页面切换。
- 不在 NavigationService 中保存数据。
- 不在 NavigationService 中写 SQL。
- 不在 NavigationService 中弹确认框。
- 离开页面前是否需要保存，应由当前页面 ViewModel 或上层 Shell 流程决定。
- ViewModel 导航只传 route，不直接 new View。
