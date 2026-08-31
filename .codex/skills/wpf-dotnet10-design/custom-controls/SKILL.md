---
name: wpf-dotnet10-custom-controls
description: SwiftExport.Client.Wpf 自定义控件设计规则。用于 PopupTextBox、搜索输入、DataGrid 编辑控件、业务选择器和可复用控件 API。
---

# WPF .NET 10 自定义控件 Skill

## 适用

用于新客户端自定义控件：

- `PopupTextBox`
- 搜索文本框。
- DataGrid 编辑控件。
- Lookup / Selector。
- 业务复用 UserControl。

涉及键盘、焦点、Automation、高对比度、性能或测试证据时，同时读取 `../quality-gates/SKILL.md`。

## 控件设计原则

- 控件 API 用依赖属性表达。
- 控件内部不写业务流程。
- 控件不直接调用 API。
- 控件不直接修改业务集合。
- 控件通过 Binding、Command、Event 或明确 Result 与 ViewModel 通信。
- 控件样式优先使用当前客户端 token、命名 Style 和可复用的 WPF-UI 控件能力。
- 如果控件基于 lepoco/WPF-UI，先读 `../wpfui-controls/SKILL.md`。

## PopupTextBox 规则

- 紧凑状态显示单行或限高预览。
- 编辑状态使用 Popup。
- Popup 内 TextBox 支持 `TextWrapping="Wrap"` 和 `AcceptsReturn="True"`。
- `Text` 双向绑定。
- `Esc` / `Ctrl+Enter` 可关闭 Popup。
- 失焦自动关闭。

## 搜索选择控件规则

- 新搜索选择场景优先设计为输入和选中展示分离。
- 支持本地/远程搜索时，必须明确模式。
- 选择结果通过 `SelectedItem` / `SelectedValue` 或命令提交。
- 不让选中项回填输入框导致重复搜索。

## DataGrid 编辑控件规则

- 控件尺寸稳定，不因内容变化撑坏表格。
- 键盘行为要清楚。
- 编辑值必须回写绑定。
- 错误状态要能被 DataGrid 行/单元格展示。

## 业务抽屉输入规则

- `RightDrawerHost.CloseOnEscape` 默认关闭，必须由页面显式启用。
- Esc 只能在抽屉内部焦点范围内、子编辑器未处理后发起关闭请求；不得在 Preview 阶段抢占 DataGrid、TextBox 或 Popup 的 Esc。
- 已绑定 CloseCommand 时必须尊重 CanExecute，不得回退为直接修改 IsOpen。

## 禁止事项

- 禁止控件直接访问 Repository、API、Application Service。
- 禁止控件硬编码业务模块。
- 禁止为了一个页面写不可复用但放到全局的控件。
- 禁止控件吃掉未保存变更检查。
