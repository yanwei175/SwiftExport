# Skill: WPF DataGrid Editing

## When to use

当任务涉及 WPF DataGrid 编辑列、批量编辑、LineModel/RowModel、脏数据追踪、只读控制、父 ViewModel 绑定、Excel 快捷键或自定义控件协作时使用。

## Required reading

- `guides/frontend-wpf/wpf-datagrid-guide.md`
- `guides/frontend-wpf/wpf-communitytoolkit-mvvm-guide.md`
- `guides/frontend-wpf/wpf-custom-controls-guide.md`
- `guides/frontend-wpf/wpf-dialog-navigation-guide.md`

## Workflow

1. 先定位相关文件：XAML 页面、ViewModel、LineModel/RowModel、ListModel、样式资源、自定义控件和 Application Service。
2. 先给计划，说明列类型、绑定路径、编辑状态、保存流程和 UI 风险；除非用户明确要求直接执行，否则不要跳过计划。
3. 判断是否影响 Shared DTO、前端 LineModel、后端保存接口、数据库字段或主题资源。
4. 修改 DataGridTextColumn / DataGridTemplateColumn 前，先确认现有 Assist、自定义控件和样式。
5. 需要访问父 ViewModel 时优先使用项目已有 BindingProxy、RelativeSource 或 DataContext 约定。
6. IsReadOnly / CanEdit / IsApproved 等状态应放在 ViewModel/LineModel 可绑定属性，不写业务判断到 code-behind。
7. 保持脏数据追踪、撤销、删除池和批量保存路径完整。
8. 修改后运行 `dotnet build`；可运行 UI 时进行关键页面手工验证。

## Rules

- DataGrid 编辑状态由 LineModel/ListModel 管理。
- DTO 不承载 UI 编辑状态。
- XAML 负责绑定和展示，ViewModel 负责命令和状态。
- 复用已有自定义控件、Assist、样式和绑定模式。
- Excel 快捷键和批量操作必须保持现有页面行为一致。

## Do not

- 不要绕过已有自定义控件和 Assist。
- 不要在 code-behind 写业务逻辑。
- 不要让 ViewModel 直接调用 Repository。
- 不要直接绑定 DTO 作为可编辑行。
- 不要为单个列改坏全局 DataGrid 样式。

## Validation

- 运行 `dotnet build`。
- 检查 XAML 绑定错误、列编辑状态、只读状态、保存/撤销/删除流程。
- 手工验证 DataGridTextColumn、DataGridTemplateColumn、快捷键和父 ViewModel 绑定。
- TODO：待人工确认项目级 Excel 快捷键标准。

## Output

- 汇报修改的 XAML、ViewModel、LineModel/ListModel 和服务调用。
- 说明编辑状态、脏数据追踪和保存流程是否受影响。
- 说明 Shared、后端、数据库和主题资源影响。
- 说明验证命令和结果。
