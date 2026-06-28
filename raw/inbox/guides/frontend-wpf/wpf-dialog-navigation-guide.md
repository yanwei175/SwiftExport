# WPF Dialog Navigation Guide

## 适用场景

适用于 WPF 页面中的消息提示、确认弹窗、模态窗口、文件选择和页面导航。

## 推荐做法

- ViewModel 通过 `IMessageService` 发送非阻塞提示。
- 需要用户确认、文件选择或模态交互时使用 `IDialogService`。
- 页面切换使用 `INavigationService` 或项目既有导航服务。
- ViewModel 到 View 的映射优先使用 DataTemplate。
- 导航前如有未保存变更，应由当前页面或 Shell 流程确认。
- WPF Dialog 实现中注意 Dispatcher、Owner 和命名空间冲突。

## 禁止事项

- 禁止 ViewModel 直接 `MessageBox.Show`。
- 禁止 ViewModel 直接 `new Window()` 或 `new Page()`。
- 禁止 NavigationService 承载业务保存、权限判断或 SQL。
- 禁止用 MessageService 处理必须阻塞用户的确认动作。
- 禁止在 DialogService 中写具体业务流程。

## Codex 修改前检查清单

- 项目是否已有消息、弹窗、导航服务接口。
- 当前交互是提示、确认、模态编辑、文件选择还是页面导航。
- 是否需要设置窗口 Owner 或在 UI 线程调度。
- 导航是否需要处理未保存变更。
- 是否影响 Shell、菜单、路由或 DataTemplate。

## 常见错误

- Message、Dialog、Navigation 三类服务职责混乱。
- 导航服务直接创建 View，绕过 DataTemplate 约定。
- 弹窗没有 Owner，导致窗口焦点和遮挡问题。
- ViewModel 依赖具体 Window 类型，难以测试。
- 删除确认用非阻塞消息提示，用户无法取消。
