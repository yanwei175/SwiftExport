# WPF Dialog Navigation Guide

## 适用场景

用于 WPF 页面中的消息提示、确认弹窗、模态窗口、文件选择、页面导航和未保存变更提示。

## SwiftExport 规则

- 优先复用 `SwiftExport.Contracts.UiInterface.IDialogService` 和 `SwiftExport.View.FW/Services/DialogService.cs`。
- 简单成功提示可使用 `IDialogService.ShowCardAsync`；需要确认、输入或错误详情时使用对应 Dialog 方法。
- 页面切换优先使用项目既有 Routing / ViewRouteService / ViewRoutes。
- 更完整的 UI 服务边界见 [WPF UI Services](wpf-ui-services.md)。
- 导航前如有未保存变更，应由当前页面或 Shell 流程确认。
- 弹窗实现注意 Dispatcher、Owner、焦点和窗口遮挡问题。

## 修改前检查

- 当前交互是提示、确认、模态编辑、文件选择还是页面导航。
- 是否需要设置 Owner 或 UI 线程调度。
- 是否影响 Shell、菜单、路由或 DataTemplate。
- 当前页面或宿主是否已有 `mt:DialogHost`。

## 禁止事项

- 禁止 ViewModel 直接 `MessageBox.Show` 作为常规做法。
- 禁止 ViewModel 直接 `new Window()` 或 `new Page()`。
- 禁止 NavigationService 承载业务保存、权限判断或 SQL。
- 禁止在 DialogService 中写具体业务流程。
- 禁止为单个页面新增另一套 DialogService / NavigationService。
