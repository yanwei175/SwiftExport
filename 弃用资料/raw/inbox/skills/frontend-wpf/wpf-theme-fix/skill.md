# Skill: WPF Theme Fix

## When to use

当任务涉及 WPF ResourceDictionary、Brush/Color、全局样式、局部样式、HandyControl、自定义主题兼容或 DataGrid 视觉修复时使用。

## Required reading

- `guides/frontend-wpf/wpf-theme-resource-guide.md`
- `guides/frontend-wpf/wpf-custom-controls-guide.md`
- `guides/frontend-wpf/wpf-datagrid-guide.md`

## Workflow

1. 先定位相关文件：资源字典、主题文件、控件样式、页面 XAML、第三方控件主题和自定义控件模板。
2. 先给计划，说明资源来源、影响范围、全局/局部选择和视觉风险；除非用户明确要求直接执行，否则不要跳过计划。
3. 判断是否影响前端多个页面、DataGrid、自定义控件、Shared 或后端。
4. 修改主题前先确认资源键的引用范围，优先复用已有 Brush/Color/Style。
5. 单页问题优先局部修复；全局问题才修改全局 ResourceDictionary。
6. HandyControl 或自定义主题兼容问题应先定位覆盖链和默认样式来源。
7. 修改后运行 `dotnet build`；可运行 UI 时进行目标页面和至少一个相邻页面的视觉验证。

## Rules

- 优先使用 ResourceDictionary 和已有资源键。
- Brush / Color 资源命名应表达用途。
- 全局样式修改必须说明影响范围。
- DataGrid 主题修复不能破坏编辑、选择、只读和验证状态。

## Do not

- 不要硬编码颜色到大量控件。
- 不要为了单页问题破坏全局主题。
- 不要未定位资源来源就重命名资源键。
- 不要把业务状态写入主题资源。
- 不要忽略 HandyControl 或自定义控件默认样式。

## Validation

- 运行 `dotnet build`。
- 检查 XAML 资源解析和绑定错误。
- 手工验证目标页面、全局样式影响、DataGrid 状态和主题兼容。
- TODO：待人工确认统一视觉回归截图流程。

## Output

- 汇报修改的资源字典、样式、页面和控件模板。
- 说明全局/局部影响范围。
- 说明是否影响 DataGrid、自定义控件或 HandyControl。
- 说明验证命令、视觉验证结果和剩余风险。
