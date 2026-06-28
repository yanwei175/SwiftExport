# WPF DataGrid Guide

## 适用场景

适用于 ERP、采购、销售、库存、价格、BOM 等存在查询、批量编辑、批量保存的 WPF DataGrid 页面。

## 推荐做法

- 可编辑 DataGrid 绑定 LineModel/ListModel，不直接绑定 DTO。
- DTO 只属于应用层传输数据，不包含 UI 状态。
- LineModel 负责行状态、脏数据、选中、删除、验证和 DTO 转换。
- ListModel 负责 Items、SelectedItem、变更集、删除池、AcceptChanges、RejectChanges。
- 保存通过一次批量 Application Service 调用完成。
- 删除已持久化行时进入删除池；删除新增行时可直接移除。
- 保存成功后同时接受列表和页面变更；撤销时恢复列表和页面状态。

## 禁止事项

- 禁止 DTO 直接携带 `IsDirty`、`IsSelected`、`IsDeleted`、`RowIndex`。
- 禁止 ViewModel 逐行调用 Repository 保存。
- 禁止 LineModel 调用 Repository、Dapper、Dialog 或 Application Service。
- 禁止删除已持久化行时立即丢失其删除信息。
- 禁止刷新、搜索覆盖或关闭页面前无提示丢弃未保存变更。

## Codex 修改前检查清单

- 项目是否已有 LineModel、BaseListModel、变更集和验证基类。
- 新增、修改、删除、撤销、批量保存的 UI 行为是否已确认。
- DTO、LineModel、Application Service、Repository 边界是否已列出。
- 是否影响数据库表、字段或批量保存接口。
- 与 `skills/frontend-wpf/dotnet-wpf-toolkitmvvm` 的 DataGrid 流程是否一致。

## 常见错误

- DataGrid 直接绑定 DTO 列表，后续无法可靠撤销和批量保存。
- 新增行和已有行删除规则混在一起。
- 保存后未调用 AcceptChanges，导致页面仍显示有变更。
- 验证错误放在 DTO 中，污染应用层模型。
- 为一个页面写一套独立状态机制，项目内无法复用。
