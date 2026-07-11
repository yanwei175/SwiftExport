# WPF 企业级 DataGrid 工作流

## 适用

原生 WPF `DataGrid` 企业级增强框架的架构、API、实现、专项审查和演进。

## 执行顺序

1. 读取 `AGENTS.md`、`.agents/router.toml` 和 `.agents/wpf-enterprise-grid-architect.toml`。
2. 读取 `.codex/skills/wpf-dotnet10-design/wpf-enterprise-grid/SKILL.md` 及其 `references/总体架构.md`。
3. 搜索真实源码：目标 DataGrid、样式、业务行模型、选择绑定和现有测试；源码优先于文档。
4. 判断任务属于架构、核心、选择、编辑、列、验证、状态、筛选或导出，并只加载命中的子 Skill。
5. 涉及 ViewModel/LineModel 时，额外读取 `communitytoolkit-mvvm/SKILL.md`；涉及主题时读取 `official-theme/SKILL.md`，但遵守本框架禁用第三方控件的更严格规则。
6. 先确认原生行为、事件顺序、编辑事务和虚拟化影响，再设计 API；不得从视觉模板反推业务状态。
7. 公开 API 优先使用附加属性、命令、Binding 和小型策略接口；保持可禁用、可解绑和幂等。
8. 用户只要求架构或审查时，不修改业务代码。用户明确要求实现后，按一个可验收能力切片落地。
9. 每个切片检查：原生列兼容、鼠标/键盘、验证失败、虚拟化、MVVM 双向同步、内存释放、无第三方依赖。
10. 实现阶段优先运行框架单元测试、WPF STA 交互测试和客户端构建。

## Skill 选择

| 任务 | 必读 Skill |
|---|---|
| 框架入口、附加属性、生命周期、资源 | `wpf-grid-core` |
| CurrentRow、SelectedItem、CurrentCell | `wpf-grid-selection` |
| 双击、F2、Enter、Tab、Esc | `wpf-grid-editing` |
| 列标识、Lookup、Enum、Numeric | `wpf-grid-column` |
| Cell/Row Validation、错误导航 | `wpf-grid-validation` |
| 业务状态到视觉状态的适配 | `wpf-grid-state` |
| FilterRow、ColumnFilter、本地/远程筛选 | `wpf-grid-filter` |
| CSV/Excel、可见列与原始值导出 | `wpf-grid-export` |

跨模块任务由 `wpf-enterprise-grid-architect` 作为唯一主 Agent 统一 API 和事件顺序，不把模块边界变成重复监听同一事件的孤岛。

## 架构评审门槛

- 不替换 `DataGrid` 或重建 `DataGridColumn` 基类体系。
- 不让框架引用业务 Model、业务状态枚举、Contracts 或服务层。
- 不使用 VisualTree 扫描导出数据，不关闭行/列虚拟化来换取功能。
- 输入事件只有一个协调点；模块声明优先级，避免 Selection 与 Editing 互相抢事件。
- 双向附加属性有重入保护，禁用或 Unloaded 时解绑事件。
- 验证失败时不得移动焦点或静默丢弃编辑值。
- 公开 API 具备默认值、绑定方向、空值、只读和版本兼容说明。

## 输出

1. 当前源码事实与约束。
2. 决策及备选方案。
3. 公共 API 变化。
4. 事件顺序和 MVVM 数据流。
5. 测试与验收场景。
6. 风险、未决问题和下一切片。
