---
name: wpf-dotnet10-design
description: SwiftExport 当前 .NET 10 WPF 前端技能组入口。用于前端页面、CommunityToolkit MVVM、Fluent 布局、官方主题、lepoco/WPF-UI 控件、自定义控件、搜索选择控件、DataGrid 和页面体验设计实现。
---

# 技能用途

SwiftExport 当前 .NET 10 WPF 前端设计技能组入口。

## 子 Skill 路由

- MVVM、ViewModel、LineModel、命令、绑定属性：读 `../communitytoolkit-mvvm/SKILL.md`。
- Contracts DTO 与 ViewModel、EditModel、LineModel、TreeItemModel 之间的转换、Mapster 配置、`Adapt` 映射：读 `../../aspdotnet10/application-mapster-mapping/SKILL.md`。
- Feature 页面组织、DataGrid 页面、页面命令：读 `../feature-page/SKILL.md`。
- Fluent 布局、4px 间距、卡片、表单、Dialog 视觉：读 `../fluent-layout/SKILL.md`。
- 官方主题、ThemeMode、命名 Style、资源边界：读 `../official-theme/SKILL.md`。
- lepoco/WPF-UI 控件、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon：读 `../wpfui-controls/SKILL.md`。
- 自定义控件、PopupTextBox、SearchTextBox、控件 API：读 `../custom-controls/SKILL.md`。
- 搜索文本框专项：读 `../searchtextbox/SKILL.md`。
- 原生 DataGrid 企业级增强框架、当前行/单元格、编辑导航、列、验证、状态、筛选、导出：先读 `../wpf-enterprise-grid/SKILL.md`，再由其选择 Grid 子 Skill。

## 总原则

- 当前前端主线是 `SwiftExport.Client.Wpf`。
- 不从 Git 历史或归档分支把旧 .NET Framework WPF、VSTO、MateWpfThemes 规则直接带进新客户端。
- 页面优先使用原生 WPF + 官方 Fluent 主题 + lepoco/WPF-UI 控件 + SwiftExport 命名资源。
- ViewModel / Model 必须遵守 CommunityToolkit 源生成器规则。
- DTO 与前端模型之间的结构性字段复制默认使用 Mapster，复杂差异集中到前端 `Mappings` 配置；业务计算、状态决策和跨来源组装保持显式可读。
- Enterprise Grid 框架禁止依赖 WPF-UI、HandyControl、MaterialDesignThemes 或任何商业 Grid；即使宿主应用引用 WPF-UI，Grid 项目也必须保持原生 WPF 依赖边界。
