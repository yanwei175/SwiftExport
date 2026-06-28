# WPF Dialog Navigation Guide

## 适用场景

用于 WPF 页面中的消息提示、确认弹窗、模态窗口、文件选择、页面导航和未保存变更提示。

## SwiftExport 规则

- 优先复用 `SwiftExport.View.FW/Dialog` 和 `SwiftExport.View.FW/Services` 中已有服务。
- 简单提示可使用统一消息服务；需要确认、文件选择或模态交互时使用 Dialog Service。
- 页面切换优先使用项目既有 Routing / ViewRouteService。
- 导航前如有未保存变更，应由当前页面或 Shell 流程确认。
- 弹窗实现注意 Dispatcher、Owner、焦点和窗口遮挡问题。

## 修改前检查

- 当前交互是提示、确认、模态编辑、文件选择还是页面导航。
- 是否需要设置 Owner 或 UI 线程调度。
- 是否影响 Shell、菜单、路由或 DataTemplate。

## 禁止事项

- 禁止 ViewModel 直接 `MessageBox.Show` 作为常规做法。
- 禁止 ViewModel 直接 `new Window()` 或 `new Page()`。
- 禁止 NavigationService 承载业务保存、权限判断或 SQL。
- 禁止在 DialogService 中写具体业务流程。

