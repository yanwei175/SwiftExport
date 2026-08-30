# Skill: WPF DialogService

## 适用范围

本 skill 用于在 WPF + CommunityToolkit.Mvvm 项目中设计弹窗服务。

适合：

- 确认删除
- 显示错误详情
- 打开编辑窗口
- 文件选择
- 需要阻塞用户继续操作的交互

不适合：

- 保存成功提示
- 普通状态栏消息
- 非阻塞提示

这些场景应交给 `IMessageService`。

## 设计原则

`IDialogService` 不是 CommunityToolkit.Mvvm 内置能力，需要项目自行定义。

ViewModel 不直接调用：

```csharp
MessageBox.Show(...);
new SomeEditWindow().ShowDialog();
```

ViewModel 只依赖接口：

```csharp
private readonly IDialogService _dialogService;
```

## 接口

```csharp
using System.Windows;

namespace YourApp.Client.Services;

public interface IDialogService
{
    MessageBoxResult Show(
        string message,
        string? caption = null,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None);

    Task<MessageBoxResult> ShowAsync(
        string message,
        string? caption = null,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None);

    Task<bool> ConfirmAsync(string message, string? caption = null);

    Task ShowInfoAsync(string message, string? caption = null);

    Task ShowWarningAsync(string message, string? caption = null);

    Task ShowErrorAsync(string message, string? caption = null);
}
```

## 实现

为避免命名冲突，必须显式使用 WPF 别名：

```csharp
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfWindow = System.Windows.Window;

namespace YourApp.Client.Services;

public sealed class DialogService : IDialogService
{
    private const string DefaultCaption = "应用程序";

    public MessageBoxResult Show(
        string message,
        string? caption = null,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var app = WpfApplication.Current;
        if (app?.Dispatcher == null)
        {
            return WpfMessageBox.Show(message, GetCaption(caption), button, icon);
        }

        if (app.Dispatcher.CheckAccess())
        {
            return ShowCore(message, caption, button, icon);
        }

        return app.Dispatcher.Invoke(
            () => ShowCore(message, caption, button, icon),
            DispatcherPriority.Send);
    }

    public Task<MessageBoxResult> ShowAsync(
        string message,
        string? caption = null,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var app = WpfApplication.Current;
        if (app?.Dispatcher == null)
        {
            return Task.FromResult(WpfMessageBox.Show(message, GetCaption(caption), button, icon));
        }

        if (app.Dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowCore(message, caption, button, icon));
        }

        return app.Dispatcher
            .InvokeAsync(() => ShowCore(message, caption, button, icon), DispatcherPriority.Send)
            .Task;
    }

    public async Task<bool> ConfirmAsync(string message, string? caption = null)
    {
        var result = await ShowAsync(
            message,
            caption ?? "确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    public async Task ShowInfoAsync(string message, string? caption = null)
    {
        await ShowAsync(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public async Task ShowWarningAsync(string message, string? caption = null)
    {
        await ShowAsync(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public async Task ShowErrorAsync(string message, string? caption = null)
    {
        await ShowAsync(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static MessageBoxResult ShowCore(
        string message,
        string? caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        var app = WpfApplication.Current;
        if (app == null)
        {
            return WpfMessageBox.Show(message, GetCaption(caption), button, icon);
        }

        var window = new AppDialogWindow(message, GetCaption(caption), button, icon)
        {
            Owner = GetOwner()
        };

        window.ShowDialog();
        return window.Result;
    }

    private static string GetCaption(string? caption)
    {
        return string.IsNullOrWhiteSpace(caption) ? DefaultCaption : caption;
    }

    private static WpfWindow? GetOwner()
    {
        var app = WpfApplication.Current;
        if (app?.Windows == null)
        {
            return null;
        }

        return app.Windows
            .OfType<WpfWindow>()
            .FirstOrDefault(window => window.IsActive && window is not AppDialogWindow)
            ?? app.MainWindow;
    }
}
```

## DI 注册

```csharp
services.AddSingleton<IDialogService, DialogService>();
```

`DialogService` 本身无状态，每次弹窗创建新窗口，所以可以注册为 Singleton。

## ViewModel 用法

```csharp
private readonly IDialogService _dialogService;

public ExamplePageViewModel(IDialogService dialogService)
{
    _dialogService = dialogService;
}

private async Task DeleteAsync()
{
    if (!await _dialogService.ConfirmAsync("确定删除选中记录吗？"))
    {
        return;
    }

    // 执行删除
}
```

## 常见编译问题

如果出现：

```text
命名空间 YourApp.Application 中不存在 Current
```

说明项目命名空间遮住了 WPF `Application`。使用：

```csharp
using WpfApplication = System.Windows.Application;
```

如果出现：

```text
MessageBox 是 System.Windows.Forms.MessageBox 和 System.Windows.MessageBox 之间的不明确引用
```

使用：

```csharp
using WpfMessageBox = System.Windows.MessageBox;
```

## 使用规则

- 删除确认：`ConfirmAsync`
- 需要阻塞的错误详情：`ShowErrorAsync`
- 保存成功：不要弹窗，优先用 `IMessageService.ShowSuccess`
- ViewModel 不直接 new Window
- ViewModel 不直接调用 MessageBox
