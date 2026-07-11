---
name: wpf-dotnet10-fluent-layout
description: SwiftExport.Client.Wpf 的 .NET 10 WPF Fluent 布局、lepoco/WPF-UI 控件协作、4px 间距系统、页面结构、卡片、表单、DataGrid 页面和 Dialog 视觉规则。
---

# WPF .NET 10 Fluent 布局 Skill

## 布局原则

- 使用微软官方 WPF Fluent 主题作为默认视觉基础。
- 可使用 lepoco/WPF-UI 的 Fluent 控件补充官方 WPF 控件能力。
- 使用 SwiftExport 自己的命名资源和 token 补充业务视觉。
- 间距遵循 4px 系统：4、8、12、16、24、32。
- 避免 18、20 这类游离间距，除非有明确设计原因。
- 页面应服务 ERP 工作流，优先信息密度、可扫描、可操作。

## 页面结构

管理页推荐：

```text
PageRoot
  Header / Toolbar
  FilterBar
  Content DataGrid
  Pagination / Status
```

Dialog 推荐：

```text
Title
Body
ActionBar
```

## 控件视觉

- 普通控件使用官方默认 Style。
- 业务特殊样式使用命名 Style。
- 不覆盖全局隐式 `Button`、`TextBox`、`DataGrid` 默认样式。
- DataGrid 页面需要清楚区分 hover、selected、error、deleted、readonly。

## 文字规则

- 不用夸张 hero 字号。
- ERP 操作界面标题要紧凑。
- 按钮文案使用明确业务动词。
- 危险动作使用明确提示。

## 禁止事项

- 禁止大面积渐变和装饰性背景。
- 禁止 UI 卡片套卡片。
- 禁止为了好看牺牲表格扫描效率。
- 禁止把营销页布局带进 ERP 工作台。
