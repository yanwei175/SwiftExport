# WPF UI Services Guide

## 适用场景

用于 SwiftExport WPF 前端中的弹窗、消息、导航、DialogHost 和 ViewModel 与 UI 服务的边界。

## SwiftExport 事实

Dialog 接口：

- `SwiftExport.Contracts/UiInterface/IDialogService.cs`

Dialog 实现：

- `SwiftExport.View.FW/Services/DialogService.cs`

Dialog 底层：

- `MateWpfThemes.FW.Services.Dialogs.DialogProvider`
- 页面或宿主 XAML 中的 `mt:DialogHost`

路由：

- `SwiftExport.View.FW/Routing/IViewRouteService.cs`
- `SwiftExport.View.FW/Routing/ViewRouteService.cs`
- `SwiftExport.View.FW/Routing/ViewRoutes.cs`
- `SwiftExport.View.FW/DI/DependencyInjection.Routing.cs`

## Dialog 规则

- ViewModel 注入 `IDialogService`。
- 删除、保存前确认、危险操作确认使用 `ShowConfirmAsync`。
- 错误详情使用 `ShowErrorAsync`。
- 普通成功或轻量提示可使用 `ShowCardAsync`。
- 新增页面如需 DialogHost 能力，应在页面或宿主合适位置放置 `mt:DialogHost`。

## Message 规则

- 当前项目不默认新增独立 `IMessageService`。
- 普通状态可用页面状态属性、StatusBar 绑定或 `ShowCardAsync`。
- 只有需要统一 Shell 状态栏、Toast 队列、消息等级颜色时，才单独设计 MessageService。

## Navigation 规则

- 新增导航入口优先沿用 `ViewRouteService` / `ViewRoutes` / DI 路由注册。
- ViewModel 不直接 new View / Window。
- NavigationService 只负责页面定位，不负责保存、弹确认框、业务权限或 SQL。
- 离开页面前未保存检查由当前页面 ViewModel 或上层流程处理。

## 禁止事项

- 禁止 ViewModel 直接 `MessageBox.Show` 作为常规做法。
- 禁止 ViewModel 直接 `new Window()` 或 `new Page()`。
- 禁止为单个页面新增另一套路由字典或消息服务。
- 禁止把 UI 服务注入后端 Application、Infrastructure 或 Core。
