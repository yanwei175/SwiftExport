# SwiftExport WPF MessageService 草稿

> 已按 SwiftExport 项目口径整理：当前项目未单独抽象 `IMessageService`，普通提示优先复用现有 `IDialogService.ShowCardAsync` 或页面状态栏。

## 当前项目建议

短期默认：

- 成功提示 / 普通提示：`IDialogService.ShowCardAsync`
- 阻塞确认：`IDialogService.ShowConfirmAsync`
- 错误详情：`IDialogService.ShowErrorAsync`
- 页面状态：放在对应 ViewModel 的状态属性或基类状态中

## 什么时候考虑新增 IMessageService

只有当项目需要统一 Shell 状态栏、Toast 队列、消息等级颜色、消息历史时，再单独设计：

- `IMessageService`：页面 ViewModel 发消息。
- `IStatusMessageService`：Shell / MainWindow 绑定状态。

新增前必须先检查现有 DialogHost / DialogProvider 能否满足需求。

## 禁止事项

- 不要为单个页面新增一套消息服务。
- 不要让 ViewModel 直接操作 TextBlock、StatusBar、Toast 控件。
- 不要用非阻塞消息替代必须确认的危险操作。
