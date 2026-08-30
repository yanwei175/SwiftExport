# WPF Pagination Guide

## 适用场景

用于 WPF 列表页、选择器、管理页面的分页控件、分页命令和分页前未保存变更拦截。

## SwiftExport 事实

主题库已有分页控件：

- `MateWpfThemes/MateWpfThemes.FW/Controls/PaginationControl.xaml`
- `MateWpfThemes/MateWpfThemes.FW/Controls/PageNavigator.xaml`

业务页面当前多处使用 `mt:PaginationControl`。

## SwiftExport 规则

- 新增页面优先沿用当前模块已有分页控件和绑定方式。
- ViewModel 负责 `TotalCount`、`PageIndex`、`PageSize`、加载命令和未保存变更确认。
- 分页控件只表达分页 UI 和用户请求，不负责保存、确认、权限或 API 调用。
- 有未保存变更时，分页前应通过当前项目 `IDialogService.ShowConfirmAsync` 确认。
- 需要强拦截分页变化时，优先采用命令驱动：用户请求分页 -> ViewModel 确认 -> 更新页码并加载。

## 修改前检查

- 当前页面使用 `PaginationControl` 还是 `PageNavigator`。
- `PageIndex` 是 0 基还是 1 基。
- `PageSize` 变化是否需要重置页码。
- 分页前是否需要检查 DataGrid/ListModel 的未保存变更。

## 禁止事项

- 禁止因为单页需求替换全项目分页控件。
- 禁止分页控件直接调用业务 API。
- 禁止分页时静默覆盖未保存编辑数据。
