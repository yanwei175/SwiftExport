---
name: wpf-grid-editing
description: SwiftExport 原生 WPF DataGrid 编辑兼容技能。用于确保 EnterpriseGrid 不接管双击、F2、Enter、Tab、Shift+Tab、Esc，并验证原生编辑事务与自定义编辑器兼容。
---

# WPF Grid Editing

## 编辑真相源

- `DataGrid`、列和编辑器自身拥有双击、F2、Enter、Tab、Shift+Tab、Esc。
- EnterpriseGrid 不暴露 BeginEditOnDoubleClick、BeginEditOnF2、EnterKeyMode 或 TabNavigationMode。
- Selection 只定位 CurrentCell，不得因单击调用 BeginEdit。
- Operations 只在提交完成后记录 Undo，不得决定下一 Cell 是否进入编辑。

## 自定义编辑器

- Lookup/Popup 使用原生 `DataGridTemplateColumn.CellEditingTemplate`。
- 编辑控件自行拥有 Popup 内部按键；未处理的 Tab/Esc 继续路由给 DataGrid。
- 框架不得为每行常驻编辑器或 Popup。

## 验证规则

- 业务必填、跨字段错误默认采用软提示，允许离开 Cell。
- 强类型转换无法写回源属性时仍由 WPF 原生提交事务阻断。
- 保存命令负责最终业务校验和阻断。

## 验收矩阵

覆盖原生 Text/CheckBox/Template 列、编辑后单击其他 Cell、原生 Tab/Esc、转换失败、Popup 编辑器、IME 和功能关闭场景。
