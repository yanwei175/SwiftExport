# WPF DataGrid Guide

## 适用场景

用于 ERP 查询、批量编辑、批量保存的 WPF DataGrid 页面，例如采购、销售、库存、BOM、箱单等模块。

## SwiftExport 规则

- 可编辑 DataGrid 绑定前端 Model / ListModel，不直接绑定 DTO。
- DTO 只属于传输层，不包含 UI 状态。
- 行模型负责行状态、脏数据、选中、删除、验证和 DTO 转换。
- 列表模型负责 Items、SelectedItem、变更集、删除池、AcceptChanges、RejectChanges。
- 保存优先通过一次 Application Service / API 调用完成，避免逐行保存。
- 删除已持久化行时进入删除池；删除新增行时可直接移除。
- 保存成功后接受列表和页面变更；撤销时恢复列表和页面状态。
- 刷新、搜索、分页、关闭页面前，如果列表有未保存变更，必须提示用户确认，不能静默覆盖。
- Excel 风格编辑优先复用 `DataGridExcelAssist`、`UndoManager` 和 MateWpfThemes 现有 DataGrid 扩展。
- 图片列规则见 [WPF Image Cell](wpf-image-cell.md)，分页规则见 [WPF Pagination](wpf-pagination.md)。

## 修改前检查

- 项目是否已有 BaseLineModel、BaseListModel、变更集和验证基类。
- 新增、修改、删除、撤销、批量保存的 UI 行为是否已确认。
- DTO、前端 Model、API、Application Service、Repository 边界是否已列出。
- 是否影响数据库表、字段或批量保存接口。
- 是否需要保留当前模块的 `ModelState`、`IsDirty`、`IsNew`、`IsDeleted`、`IsChecked` 命名和行为。

## 禁止事项

- 禁止 DataGrid 直接绑定 DTO 作为可编辑行。
- 禁止 ViewModel 逐行调用 Repository 保存。
- 禁止 LineModel 调用 Repository、Dapper、Dialog 或 Application Service。
- 禁止刷新、搜索、关闭页面时无提示丢弃未保存变更。
- 禁止为单个页面重造 Excel 粘贴、撤销、批量填充等已有通用能力。
