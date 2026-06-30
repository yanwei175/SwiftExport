# SwiftExport WPF DialogService 草稿

> 已按 SwiftExport 项目口径整理：当前项目已有 `IDialogService`，不要照搬 CommunityToolkit 示例。

## 当前项目事实

接口：

- `SwiftExport.Contracts/UiInterface/IDialogService.cs`

实现：

- `SwiftExport.View.FW/Services/DialogService.cs`

底层：

- `MateWpfThemes.FW.Services.Dialogs.DialogProvider`
- 页面 XAML 中需要放置 `mt:DialogHost`

DI：

- `SwiftExport.View.FW/DI/DependencyInjection.Services.cs`

## 使用场景

使用 `IDialogService`：

- 删除确认
- 保存前确认
- 错误详情
- 警告提示
- 输入框
- 页面内卡片提示

不要在 ViewModel 中直接调用：

```csharp
MessageBox.Show(...);
new SomeWindow().ShowDialog();
```

## 当前接口语义

- `ShowInfoAsync`
- `ShowWarningAsync`
- `ShowErrorAsync`
- `ShowConfirmAsync`
- `ShowInputAsync`
- `ShowCardAsync`

## SwiftExport 规则

- ViewModel 注入 `IDialogService`。
- 阻塞式用户确认使用 `ShowConfirmAsync`。
- 成功后的轻量提示可以用 `ShowCardAsync`。
- 新增页面如果需要弹窗能力，XAML 根部或宿主区域应包含 `mt:DialogHost`。
- 不要把 DialogService 放入 Contracts 以外的业务层；它是前端 UI 服务。
