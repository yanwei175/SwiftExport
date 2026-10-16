---
name: wpf-grid-editing
description: SwiftExport 原生 WPF DataGrid 编辑兼容技能。用于确保客户端适配不接管双击、F2、Enter、Tab、Shift+Tab、Esc，并验证原生编辑事务与自定义编辑器兼容。
---

# WPF Grid Editing

## 编辑真相源

- `DataGrid`、列和编辑器自身拥有双击、F2、Enter、Tab、Shift+Tab、Esc。
- 客户端适配不暴露 BeginEditOnDoubleClick、BeginEditOnF2、EnterKeyMode 或 TabNavigationMode。
- 活动行直接绑定原生 CurrentItem，选区按命令读取原生集合；不恢复选择桥接或主动设置 Focus/BeginEdit 的后备逻辑。
- Ctrl+C 完全使用原生 DataGrid，不拦截命令、不重写系统剪贴板。Grid 级 Ctrl+V 和 Delete 清空已退出；不解析剪贴板或按字段反射批量粘贴，不以 PreviewKeyDown 统一吞掉编辑器按键。TextBox 使用原生 Copy/Paste 和 EditingCommands.Delete；业务关联字段沿用现有清空命令。
- Grid Undo 和提交历史已退出。编辑器自身 Undo 保留；页面 RejectChanges 表示恢复整行/整表已接纳基线，不是逐步撤销，禁止悄悄绑定到 Ctrl+Z。
- 客户端 Ctrl+D 先由原生 CommitEdit 提交单元格及行；失败保留输入，不发送填充命令。行为不安装 Undo，不移动焦点或重建编辑事务。

## 自定义编辑器

- Lookup/Popup 使用原生 `DataGridTemplateColumn.CellEditingTemplate`。
- 编辑控件自行拥有 Popup 内部按键；未处理的 Tab/Esc 继续路由给 DataGrid。
- 框架不得为每行常驻编辑器或 Popup。

## 验证规则

- 业务必填、跨字段错误默认采用软提示，允许离开 Cell。
- 强类型转换无法写回源属性时仍由 WPF 原生提交事务阻断。
- 保存命令负责最终业务校验和阻断。

## 验收矩阵

覆盖原生 Text/CheckBox/Template 列、编辑后单击其他 Cell、原生 Tab/Esc、转换失败、Popup 编辑器、IME 和功能关闭场景。三种 SelectionUnit 下验证编辑器复制/粘贴/Delete 与编辑器 Undo；提交后无 Grid Undo；Grid 粘贴不得写字段，Delete 不得清空或删行业务数据（宿主 CanUserDeleteRows=False）。验证原生复制的整行/不连续选区、模板列、排序、隐藏/重排列、多行文本及客户端填充行为卸载重载。
