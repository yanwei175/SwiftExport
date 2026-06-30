# Skill: SwiftExport WPF DataGrid Editing

## Description

当任务涉及 WPF `DataGrid` 编辑列、批量编辑、行模型、脏数据追踪、删除池、只读控制、父 ViewModel 绑定或 Excel 宿主快捷操作时使用本 Skill。

## Required Reading

- [WPF / VSTO 前端工作流](../../workflows/frontend/wpf-vsto-workflow.md)
- [WPF DataGrid Guide](../../guides/frontend-wpf/wpf-datagrid.md)
- [WPF Pagination Guide](../../guides/frontend-wpf/wpf-pagination.md)
- [WPF Image Cell Guide](../../guides/frontend-wpf/wpf-image-cell.md)
- [MVVMLight Guide](../../guides/frontend-wpf/wpf-mvvmlight.md)
- [Dialog / Navigation Guide](../../guides/frontend-wpf/wpf-dialog-navigation.md)
- 当前模块已有 View、ViewModel、Model、Mapping 和 API 调用。

## Workflow

1. 先定位目标页面 XAML、ViewModel、行模型/ListModel、样式资源、Mapping 和保存 API。
2. 判断修改是否影响 DTO、前端 Model、保存接口、数据库字段或主题资源。
3. 编辑行优先使用 `SwiftExport.View.FW/**/Models` 下的前端 Model，不直接把 DTO 当作可编辑行状态容器。
4. `IsReadOnly`、`CanEdit`、审批状态、删除状态等业务状态应暴露为可绑定属性，不把业务判断写进 code-behind。
5. 如当前模块已有脏数据、删除池、撤销或 `AcceptChanges` 约定，必须沿用并保持保存流程完整。
6. 需要访问父 ViewModel 时，优先使用项目已有 BindingProxy、RelativeSource 或 DataContext 约定。
7. 修改列模板前先确认现有自定义控件、Assist、ResourceDictionary 和全局样式。

## Rules

- XAML 负责展示和绑定，ViewModel 负责命令、状态和服务调用。
- ViewModel 不直接调用 Repository，不写 SQL。
- DTO 不放 UI 编辑状态、选中状态、脏数据状态或临时校验状态。
- DataGrid 全局样式修改必须评估其他页面影响；单页问题优先局部修复。
- 批量保存必须保留新增、修改、删除的状态区分。

## Validation

- 环境具备时运行完整解决方案构建。
- 检查 XAML 绑定错误、列编辑状态、只读状态、保存/撤销/删除流程。
- 可运行 UI 时手工验证目标页面和至少一个相邻编辑场景。

## Output

最终回复用中文说明：

- 修改了哪些 XAML、ViewModel、Model、Mapping 或 API 调用。
- DataGrid 编辑状态、脏数据追踪、删除池和保存流程是否受影响。
- 是否涉及 Contracts、后端、数据库或主题资源。
- 是否运行构建/测试；未运行时说明原因。
