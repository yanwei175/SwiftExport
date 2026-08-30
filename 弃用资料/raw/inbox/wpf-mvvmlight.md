# SwiftExport WPF MVVMLight 草稿

## 适用范围

适用于 SwiftExport：

- .NET Framework 4.8 WPF
- MVVMLight
- `GalaSoft.MvvmLight`
- `GalaSoft.MvvmLight.Command`
- Refit API Client
- MateWpfThemes.FW

## 基本原则

```text
View / XAML
    -> Binding / Command
ViewModel
    -> Refit API Client / UI Service
Contracts DTO
    -> Backend
```

ViewModel 允许页面状态、Binding 属性、Command、简单 UI 编排、API 调用、Dialog / Navigation 调用。

ViewModel 禁止 SQL、Dapper、Repository、事务控制、直接 new Repository / Service。

## 兼容性规则

默认兼容 .NET Framework 4.8 和项目当前 C# 写法。

普通任务禁止默认引入：

- CommunityToolkit.Mvvm
- `[ObservableProperty]`
- `[RelayCommand]`
- record / init / required
- file-scoped namespace
- primary constructor
- nullable reference types 强依赖

## 项目基类

页面 ViewModel 优先继承：

- `SwiftExport.View.FW.Mvvm.ViewModelBaseEx`

该基类已有：

- Busy 状态。
- `RegisterBusyNotifyCommand`。
- MVVMLight Messenger。
- 可选 Navigation。
- `DisabledStateScope`。
- `SetProperty`。

## 命令规则

- 使用 `RelayCommand` / `RelayCommand<T>`。
- 异步命令用 `async () => await XxxAsync()`。
- Busy 影响的命令注册到 `RegisterBusyNotifyCommand`。
- 状态变化后调用 `RaiseCanExecuteChanged`。

## DataGrid 规则

可编辑 DataGrid 不直接绑定 DTO。

优先结构：

```text
DTO -> Model / LineModel -> ListModel / TableModel -> DataGrid -> DTO -> Refit API
```

## 消息 / 弹窗 / 导航

- 弹窗与提示优先使用项目已有 `IDialogService`。
- 导航优先使用当前 Routing / ViewRoutes 体系。
- ViewModel 不直接 new Window。

## Codex 修改要求

修改 WPF MVVMLight 代码时：

1. 先确认当前模块已有 ViewModel、Model、ListModel、DI 注册和资源。
2. 保持 Binding 属性名称稳定。
3. 不混用 CommunityToolkit。
4. 不把 SQL 写进 ViewModel。
5. 每次优先只处理一个页面或一个 ViewModel。
