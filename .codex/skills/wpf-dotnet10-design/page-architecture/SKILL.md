---
name: wpf-dotnet10-page-architecture
description: SwiftExport .NET 10 WPF 页面架构边界技能。用于 ViewModel 与 Refit/Feature Service 职责、API 依赖、页面权限、普通表单验证、导航参数、Document Tab identity，以及 Dialog、抽屉和子页面返回协议。
---

# WPF 页面架构边界 Skill

## 阶段二边界

本 Skill 建立可自动触发和审查的页面横向架构门禁：依赖编排、权限表达、普通表单验证、页面打开与 Tab identity、Dialog/抽屉/子页面返回协议。

不建立模块能力地图，不重做阶段一运行时安全规则，也不扩展到自动化测试体系、可访问性、本地化、性能、日志合规、缓存和导入导出治理。

## 必读

公共启动沿用 `AGENTS.md`；以下仅补齐当前业务任务尚未读取的依赖，不回溯已读入口。

- 改变分层、公共依赖或边界不明时读 `.agents/references/工程架构总纲.md` 相关章节
- 新增或改变页面组成、命令或 Grid 交互边界时读 `../feature-page/SKILL.md`
- `../communitytoolkit-mvvm/SKILL.md`
- 涉及类型、目录或文件职责时读 `../file-organization/SKILL.md`
- `references/页面架构边界决策表.md`
- 涉及生命周期、Busy、错误反馈或未保存关闭时读 `../runtime-safety/SKILL.md` 及 `../runtime-safety/references/运行时安全门禁.md`
- 新增或改变 Refit 契约、DTO 或模型映射时分别读 `../../aspdotnet10/contracts-refit-api/SKILL.md`、`../../aspdotnet10/contracts-dto-models/SKILL.md`、`../../aspdotnet10/application-mapster-mapping/SKILL.md`；调用既有 API 只核实其契约及本次调用行为
- 目标 ViewModel、Feature Services、API/DI 注册、Navigation、Document Tab、Dialog/Message Service、权限和验证真实源码

## 核心门禁

- ViewModel 负责页面 UI 状态、命令、调用顺序和结果接纳。简单、窄、单一用例的 Refit API 可以直接注入；多接口编排、跨调用协调或结果归一化进入 Feature Service。
- Feature Service 不持有可绑定页面状态、不弹窗、不访问 View/Window/控件；业务 ViewModel 不处理 HTTP、序列化和 Refit 注册细节。
- 导航访问、页面操作、字段编辑权限分别表达；禁止硬编码角色名。隐藏用于入口不适用或不可发现，禁用用于需要解释的可见操作；后端授权始终权威。
- 字段、本地跨字段、异步验证、服务端字段错误、业务/状态失败各有 owner。保存前阻断无效输入并提供首错反馈，失败保留编辑和脏状态；setter 不承载业务流程。
- 页面打开请求、Dialog 输入和结果等客户端协作类型由目标 Feature 拥有；具体目录和文件边界服从文件组织 Skill。Document Tab identity 必须稳定且包含路由/文档类型语义，重复打开和重复激活策略显式。
- Dialog、抽屉、子页面通过明确结果表达确认、取消、关闭；调用方只接纳确认结果。业务 ViewModel 不 new View/Window，也不依赖 WPF-UI 具体控件。

## 与既有门禁衔接

- 生命周期、查询取消、Busy、错误分类、反馈 owner、资源释放和未保存关闭只服从 `../runtime-safety/SKILL.md`；本 Skill 只定义参数、identity、结果和职责接口，不重复定义运行时机制。
- DTO 按 API 用例和契约语义设计；DTO↔前端 Model 的结构性复制继续服从 Mapster/DTO 门禁。
- 类型 owner 已确定后的目录与文件拆分只服从 `../file-organization/SKILL.md`；本 Skill 不以物理文件结构替代协作协议。
- 采用“触碰即治理”：只治理本次触碰的页面协作边界；记录范围外偏差，不批量重构客户端。

## 详细参考

- `references/页面架构边界决策表.md`
