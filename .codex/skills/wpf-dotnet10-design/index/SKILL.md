---
name: wpf-dotnet10-design
description: SwiftExport .NET 10 WPF 任务的技能选择入口；按实际页面、MVVM、控件或交互改动选择专项规则。
---

# 技能用途

SwiftExport 当前 .NET 10 WPF 前端设计技能组入口。

子Skill按本次改变的职责/行为选择，不是整套必读清单；局部修正复用有效页面与架构结论，已有上下文不重新加载。技术约束仍有效，新增共享行为或有具体风险证据时补齐相关专项规则。

## 子 Skill 路由

- MVVM、ViewModel、LineModel、命令、绑定属性：读 `../communitytoolkit-mvvm/SKILL.md`。
- 决定新类型职责/目录、拆分合并、公共复用或专项职责审查：读 `../file-organization/SKILL.md`；现有类型内局部修正不自动触发。
- 新增、修改或为新用例复用 Contracts 契约：读 `../../aspdotnet10/contracts-refit-dto/SKILL.md`，由该入口选择兼容性、DTO、Refit 等专项规则；仅消费既有响应不触发契约设计。
- 新增或修改 DTO 与前端模型之间的转换、Mapster 配置、`Adapt` 映射：读 `../../aspdotnet10/application-mapster-mapping/SKILL.md`，核对现有全局及 Feature 映射配置。
- 新增或改变页面组成、DataGrid页面交互或页面命令边界：读 `../feature-page/SKILL.md`；局部VM计算不自动升级为整页设计。
- 客户端共享 RowDragReorder、行首手柄、SortNo / LineNumber 及箱单受限分组重排：读 `../wpf-row-drag-reorder/SKILL.md`（WPF 行拖拽重排），不建设独立 Grid 模块项目。
- Fluent 布局、4px 间距、卡片、表单、Dialog 视觉：读 `../fluent-layout/SKILL.md`。
- 官方主题、ThemeMode、命名 Style、资源边界：读 `../official-theme/SKILL.md`。
- lepoco/WPF-UI 控件、FluentWindow、TitleBar、InfoBar、Snackbar、ContentDialog、SymbolIcon：读 `../wpfui-controls/SKILL.md`。
- 自定义控件、PopupTextBox、SearchTextBox、控件 API：读 `../custom-controls/SKILL.md`。
- 搜索文本框专项：读 `../searchtextbox/SKILL.md`。
- 新增或改变 ViewModel/API/Feature Service 的职责边界、页面权限、表单验证、导航或 Dialog/抽屉/子页面协议：读 `../page-architecture/SKILL.md`；既有 ViewModel 内局部计算不因此展开整页架构。
- 页面异步加载、搜索防抖、写命令并发、Busy、导航生命周期、错误分类、资源释放和未保存变更：读 `../runtime-safety/SKILL.md`。
- 前端测试证据、可访问性、本地化与文化格式、UI 性能、日志与敏感信息、缓存、文件导入导出安全：读 `../quality-gates/SKILL.md`。
- 原生 DataGrid 企业级增强框架、当前行/单元格、编辑导航、列、验证、状态、筛选、导出：先读 `../wpf-enterprise-grid/SKILL.md`，再由其选择 Grid 子 Skill。

## 总原则

- 当前前端主线是 `SwiftExport.Client.Wpf`。
- 不从 Git 历史或归档分支把旧 .NET Framework WPF、VSTO、MateWpfThemes 规则直接带进新客户端。
- 页面优先使用原生 WPF + 官方 Fluent 主题 + lepoco/WPF-UI 控件 + SwiftExport 命名资源。
- ViewModel / Model 必须遵守 CommunityToolkit 源生成器规则。
- DTO 与前端模型之间的结构性字段复制默认使用 Mapster，复杂差异集中到前端 `Mappings` 配置；业务计算、状态决策和跨来源组装保持显式可读。
- Enterprise Grid 框架禁止依赖 WPF-UI、HandyControl、MaterialDesignThemes 或任何商业 Grid；即使宿主应用引用 WPF-UI，Grid 项目也必须保持原生 WPF 依赖边界。
