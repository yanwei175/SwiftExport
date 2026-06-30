# SwiftExport WPF MVVM 模板草稿

> 已按 SwiftExport 项目口径整理：当前默认是 .NET Framework 4.8 + MVVMLight，不采用 CommunityToolkit.Mvvm 作为普通功能开发默认方案。

## 适用范围

本草稿只用于 SwiftExport WPF 前端页面、ViewModel、行模型和列表模型的规则参考。

默认技术约束：

- .NET Framework 4.8
- MVVMLight / `GalaSoft.MvvmLight`
- `GalaSoft.MvvmLight.Command.RelayCommand`
- `Microsoft.Extensions.DependencyInjection`
- Refit API Client
- MateWpfThemes.FW

## SwiftExport 默认调用方向

```text
View / XAML
    -> Binding / Command
ViewModel
    -> Refit API Client / UI Service
Contracts DTO
    -> WebApi / Application / Repository
```

ViewModel 可以包含页面状态、Binding 属性、Command、简单 UI 编排、调用 API、调用 DialogService / NavigationService。

ViewModel 禁止包含 SQL、Dapper、Repository 实现、业务事务控制、复杂业务计算、直接 new Repository / Service。

## ViewModel 模板规则

- 页面 ViewModel 优先继承项目已有 `SwiftExport.View.FW.Mvvm.ViewModelBaseEx`。
- 属性使用 MVVMLight 的 `Set` / `SetProperty` 模式。
- 命令使用 `RelayCommand` / `RelayCommand<T>`。
- 异步命令用 `async () => await XxxAsync()` 包装，不写业务 `async void`。
- Busy 相关命令注册优先使用 `RegisterBusyNotifyCommand`。
- 页面加载通过 Loaded Command、导航进入、或外部 Shell 调用，不在构造函数中执行大量异步加载。

## DataGrid 模板规则

可编辑 DataGrid 不直接绑定 DTO。

```text
DTO
    -> LineModel
    -> ListModel
    -> DataGrid
    -> ToDto()
    -> Refit API
```

LineModel 承载 UI 状态和 DTO 转换；ListModel 承载集合、变更集、删除池、AcceptChanges / RejectChanges。

## Dialog / Message / Navigation

- 当前项目已有 `SwiftExport.Contracts.UiInterface.IDialogService` 和 `SwiftExport.View.FW.Services.DialogService`。
- 阻塞确认、错误详情、输入框走 `IDialogService`。
- 非阻塞卡片提示可使用 `ShowCardAsync`。
- 导航优先沿用 `SwiftExport.View.FW.Routing.ViewRouteService` / `ViewRoutes` 和当前 DI 路由注册方式。

## 禁止引入的默认写法

普通 WPF 功能开发不要引入：

- `CommunityToolkit.Mvvm`
- `[ObservableProperty]`
- `[RelayCommand]`
- `ObservableObject`
- C# 9/10 语法，例如 record、init、required、file-scoped namespace、global using、primary constructor

除非用户明确要求框架迁移，并单独制定迁移计划。
