# Skill: WPF MessageService

## 适用范围

本 skill 用于在 WPF + CommunityToolkit.Mvvm 项目中设计非阻塞消息服务。

适合：

- 状态栏消息
- Toast 消息
- 保存成功提示
- 普通警告
- 非阻塞错误提示

不适合：

- 用户确认
- 文件选择
- 编辑弹窗
- 必须打断用户操作的错误详情

这些场景应交给 `IDialogService`。

## 设计原则

`IMessageService` 不是 CommunityToolkit.Mvvm 内置能力，需要项目自行定义。

推荐拆成两个接口：

- `IMessageService`：给页面 ViewModel 发消息。
- `IStatusMessageService`：给 Shell / MainWindowViewModel 绑定状态。

这样普通页面只知道“我要发消息”，主窗口负责“怎么显示消息”。

## 接口

```csharp
using System.ComponentModel;

namespace YourApp.Client.Services;

public enum MessageLevel
{
    Info,
    Success,
    Warning,
    Error
}

public interface IMessageService
{
    void Show(string message, MessageLevel level = MessageLevel.Info);
    void ShowInfo(string message);
    void ShowSuccess(string message);
    void ShowWarning(string message);
    void ShowError(string message);
    void Clear();
}

public interface IStatusMessageService : IMessageService, INotifyPropertyChanged
{
    string? StatusMessage { get; }
    MessageLevel StatusLevel { get; }
    DateTime? LastMessageAt { get; }
}
```

## 实现

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace YourApp.Client.Services;

public sealed partial class MessageService : ObservableObject, IStatusMessageService
{
    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private MessageLevel statusLevel = MessageLevel.Info;

    [ObservableProperty]
    private DateTime? lastMessageAt;

    public void Show(string message, MessageLevel level = MessageLevel.Info)
    {
        StatusMessage = message;
        StatusLevel = level;
        LastMessageAt = DateTime.Now;
    }

    public void ShowInfo(string message) => Show(message, MessageLevel.Info);

    public void ShowSuccess(string message) => Show(message, MessageLevel.Success);

    public void ShowWarning(string message) => Show(message, MessageLevel.Warning);

    public void ShowError(string message) => Show(message, MessageLevel.Error);

    public void Clear()
    {
        StatusMessage = null;
        StatusLevel = MessageLevel.Info;
        LastMessageAt = null;
    }
}
```

## DI 注册

必须注册同一个实例：

```csharp
services.AddSingleton<IStatusMessageService, MessageService>();
services.AddSingleton<IMessageService>(sp =>
    sp.GetRequiredService<IStatusMessageService>());
```

原因：

- 页面 ViewModel 注入 `IMessageService` 发消息。
- ShellViewModel 注入 `IStatusMessageService` 绑定状态。
- 两边必须指向同一个 `MessageService` 实例。

## ShellViewModel 用法

```csharp
public sealed class ShellViewModel
{
    public IStatusMessageService Status { get; }

    public ShellViewModel(IStatusMessageService status)
    {
        Status = status;
    }
}
```

## 页面 ViewModel 用法

```csharp
private readonly IMessageService _messageService;

public ExamplePageViewModel(IMessageService messageService)
{
    _messageService = messageService;
}

private void SaveCompleted()
{
    _messageService.ShowSuccess("保存成功。");
}
```

## XAML 示例

```xml
<TextBlock Text="{Binding Status.StatusMessage}" />
```

如果要根据消息级别变色，可绑定 `Status.StatusLevel` 写 Converter 或 Style Trigger。

## 使用规则

- 保存成功：`ShowSuccess`
- 普通提示：`ShowInfo`
- 非阻塞警告：`ShowWarning`
- 非阻塞错误：`ShowError`
- 用户确认：不要用 `IMessageService`，使用 `IDialogService`
- 页面内校验摘要：优先使用页面的 `ValidationSummary`
