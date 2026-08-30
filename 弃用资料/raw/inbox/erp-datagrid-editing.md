# SwiftExport ERP DataGrid Editing 草稿

## 适用范围

适用于 SwiftExport 中存在以下流程的 WPF 页面：

```text
查询 -> DataGrid -> 编辑 -> 批量保存
```

典型模块：产品、BOM、采购单、销售单、箱单、库存、需求管理、字典维护。

## 核心原则

ERP 可编辑 DataGrid 不直接绑定 DTO。

推荐结构：

```text
Contracts DTO
    -> 前端 LineModel / Model
    -> ListModel / TableModel
    -> DataGrid
    -> BatchRequestDto / SaveRequestDto
    -> Refit API
```

DTO 是传输模型；UI 状态放在前端 Model。

## 行状态

前端行模型可使用项目已有状态模型，例如：

- `ModelState`
- `IsDirty`
- `IsNew`
- `IsDeleted`
- `IsChecked`
- `IsReadOnly`

具体名称以当前模块已有模型为准，不为了新页面强行改全局状态体系。

## DTO 规则

DTO 禁止承载 UI 状态：

- `IsSelected`
- `IsDirty`
- `IsReadOnly`
- `RowIndex`
- 临时校验错误

如果接口本身需要删除标记或业务状态，应明确区分业务字段和 UI 状态。

## LineModel / ListModel 规则

LineModel 负责：

- 从 DTO 加载数据。
- 转回 DTO。
- 行级脏标记。
- 行级验证。
- 选中、只读、删除等 UI 状态。

ListModel / TableModel 负责：

- Items / SelectedItem。
- 新增、删除、撤销。
- 变更集提取。
- AcceptChanges / RejectChanges。
- Reindex。
- 通知 Save / Delete / InvertCheck 等命令刷新。

LineModel 和 ListModel 禁止调用 Repository、Dapper、SQL、WebApi Controller。

## 保存规则

保存通过 ViewModel 调用 Refit API 或前端服务，一次性提交变更集。

ViewModel 禁止逐行调用 Repository。

保存成功后：

- 接受变更。
- 清理删除池。
- 刷新命令状态。
- 用现有 `IDialogService` 或页面状态提示用户。

## 刷新 / 查询 / 关闭规则

刷新、搜索、分页、关闭页面前，如果当前列表有未保存变更，应提示用户确认。

不要静默覆盖当前编辑数据。

## Excel 风格编辑

优先复用 MateWpfThemes / 项目已有 DataGrid 扩展：

- `DataGridExcelAssist`
- `UndoManager`
- 现有 DataGrid 样式和单元格模板

不要为单个页面重造 Excel 编辑行为。
