---
name: wpf-grid-editing
description: SwiftExport 原生 WPF DataGrid 编辑技能。用于单元格编辑生命周期、双击/F2/Enter 启动编辑、Tab 导航、Shift+Tab 回退、Esc 取消和提交失败处理。
---

# WPF Grid Editing

## 默认交互契约

- 单击只选择，不启动编辑。
- 双击、F2、空闲态 Enter 对当前可编辑单元格调用原生 `BeginEdit`。
- 编辑态 Enter 默认提交当前 Cell 并停留；多行编辑器可通过显式策略保留 Enter 输入。
- Tab 提交当前 Cell 后移动到下一个可见、可聚焦、可编辑单元格；Shift+Tab 反向移动并允许跨行。
- Esc 沿用原生语义：先取消当前 Cell；再次 Esc 是否取消 Row 由 DataGrid 原生编辑事务决定。

## 状态机

按 `Idle -> Current -> Editing -> Committing/Canceling -> Current` 处理。任何提交失败或验证错误都停留在原 Cell，不移动、不清空输入。

## 实现规则

- 统一在 Grid 协调器的输入路由中处理优先级，Selection 先定位，Editing 再决定是否开始编辑。
- 使用 `BeginEdit`、`CommitEdit(DataGridEditingUnit.Cell, true)` 和 `CancelEdit`；不模拟鼠标、不直接操作编辑元素值。
- 导航依据 `DisplayIndex`、可见性、只读状态和列策略，不依据 Columns 集合插入顺序。
- 需要虚拟化容器时使用 `ScrollIntoView(item, column)`，不得关闭虚拟化。
- Button、CheckBox、ComboBox、Popup 等编辑器必须验证按键所有权，不能无条件吞 Tab/Esc。

## 验收矩阵

覆盖原生 Text/CheckBox/Template 列、首末 Cell 跨行、只读/隐藏列、验证失败、NewItemPlaceholder、排序后行顺序、Popup 编辑器和 IME 输入。
