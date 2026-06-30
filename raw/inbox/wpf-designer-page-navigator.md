# SwiftExport WPF PageNavigator / Pagination 草稿

## 当前项目事实

主题库已有分页控件：

- `MateWpfThemes/MateWpfThemes.FW/Controls/PaginationControl.xaml`
- `MateWpfThemes/MateWpfThemes.FW/Controls/PageNavigator.xaml`

业务页面当前多处使用 `mt:PaginationControl`。

## 适用范围

用于 WPF 列表页、选择器、管理页面的分页控制。

## SwiftExport 规则

- 新增页面优先沿用当前模块已有分页控件和绑定方式。
- 需要拦截未保存变更的分页场景，优先使用命令驱动思路：用户请求分页 -> ViewModel 检查未保存变更 -> 确认后更新 PageIndex/PageSize 并加载。
- 分页控件不负责保存、确认、业务权限判断。
- ViewModel 负责 `TotalCount`、`PageIndex`、`PageSize`、加载命令和未保存变更确认。

## 迁移注意

- 不要因为一个页面需要分页拦截就替换全项目分页控件。
- 如果要从 `PaginationControl` 切到 `PageNavigator`，必须逐页检查绑定模式和命令参数。
- 分页切换前如有脏数据，应使用当前项目 `IDialogService.ShowConfirmAsync` 提示。
