# Skill: SwiftExport WPF Theme Fix

## Description

当任务涉及 WPF `ResourceDictionary`、Brush/Color、全局样式、局部样式、MateWpfThemes.FW、自定义控件主题兼容或 DataGrid 视觉修复时使用本 Skill。

## Required Reading

- [WPF / VSTO 前端工作流](../../workflows/frontend/wpf-vsto-workflow.md)
- [Theme / Resource Guide](../../guides/frontend-wpf/wpf-theme-resource.md)
- [WPF DataGrid Guide](../../guides/frontend-wpf/wpf-datagrid.md)
- 目标页面 XAML、相关资源字典、控件模板和现有主题引用。

## Workflow

1. 先定位资源来源：页面局部资源、模块资源、全局 ResourceDictionary、第三方主题或自定义控件模板。
2. 判断问题是单页视觉问题、模块样式问题，还是全局主题问题。
3. 单页问题优先局部修复；全局问题才修改全局资源字典。
4. 修改资源键前先搜索引用范围，避免破坏其他页面。
5. 优先复用已有 Brush、Color、Style 和控件模板，不把颜色硬编码到大量控件上。
6. DataGrid 视觉修复必须同时检查编辑、选择、只读、验证错误和禁用状态。

## Rules

- 资源命名表达用途，而不是只表达具体颜色。
- 全局样式修改必须在最终回复中说明影响范围。
- 不把业务状态写入主题资源。
- 不为了单页问题重写全局控件模板。
- 不忽略 MateWpfThemes.FW 或项目自定义控件默认样式。

## Validation

- 环境具备时运行完整解决方案构建。
- 检查 XAML 资源解析、StaticResource/DynamicResource 和绑定错误。
- 可运行 UI 时验证目标页面、相邻页面、DataGrid 多状态和主题兼容。

## Output

最终回复用中文说明：

- 修改了哪些资源字典、样式、页面或控件模板。
- 是局部修复还是全局修复。
- 是否影响 DataGrid、自定义控件或主题兼容。
- 是否运行构建/测试；未运行时说明原因。
