# Web 前端 Skills 接线与职责

原规划：2026-10-07；接线更新：2026-10-09。已按 [架构权威表](Web客户端架构与脚手架决策.md#web-authority) 建立 **1 个入口 + 5 个执行 Skill**，替代早期 feature-page/api-query/form-validation/grid-editing/auth-permission 分类。Form 与权限消费归模块，编辑算法归编辑，认证查询归 API，会话与关闭归 Workspace；不维护两套执行规范。

## 1. 正式入口与发现

正文目录为 `.codex/skills/react-web`，由 [根 AGENTS](../AGENTS.md) → [Router](../.agents/router.toml) → [Web 工作流](../.agents/workflows/Web前端工作流.md) → [技能组入口](../.codex/skills/react-web/index/SKILL.md) 接线。原生发现目录 `.agents/skills/<skill-name>` 通过目录链接指向相应正文，不复制内容或维护同名副本；无需修改全局配置。

Codex 从 CWD 向仓库根扫描 `.agents/skills` 并支持技能目录链接；[官方说明](https://learn.chatgpt.com/docs/build-skills)。本轮校验文件/链接及结构可达，不把静态校验写成新会话实际发现通过；如选择器未刷新，重新启动对应会话再核验。

## 2. 六项职责及唯一正文

| Skill | 正文 | 适用边界 |
| --- | --- | --- |
| react-web | [入口](../.codex/skills/react-web/index/SKILL.md) | 意图/对象、规则读取、按需选择，不自动委派 |
| react-web-feature | [模块与主壳](../.codex/skills/react-web/react-web-feature/SKILL.md) | feature、应用组合、Form/验证、数值/日期/候选和字段错误呈现、权限及显示偏好消费 |
| react-web-api | [API 接入](../.codex/skills/react-web/react-web-api/SKILL.md) | 真实契约导出/权限投影、Orval、传输事实、Query Key/策略、认证/错误接线 |
| react-web-editing | [编辑保存](../.codex/skills/react-web/react-web-editing/SKILL.md) | 字段 owner、baseline/dirty、原始缓冲、冻结及逐类回执 |
| react-web-workspace | [多标签](../.codex/skills/react-web/react-web-workspace/SKILL.md) | 身份、Activity、完整保护、关闭、会话及未知恢复 |
| react-web-delivery | [验证交付](../.codex/skills/react-web/react-web-delivery/SKILL.md) | 类型/依赖/生成、状态/浏览器/契约证据及最终整合 |

每项含 frontmatter、适用/输入、允许范围、复用入口、禁止事项和验收；技术正文只引用 [架构决策](Web客户端架构与脚手架决策.md)、[多标签契约](Web多标签工作区设计.md)、[真实契约证据](Web首批接口契约验收.md) 及各自领域细则。这里是接线目录，不另维护伪代码与检查清单副本。

## 3. 典型任务组合

| 请求 | 必要技能 | 限制 |
| --- | --- | --- |
| 参考 .NET 10 客户端建立 Web 主壳 | feature + workspace + delivery；登录接入再加 api，保存验证页再加 editing | WPF 只读行为参考；不复制 MVVM/AttachedProperty，不默认接产品真实写入 |
| 会话、缓存、偏好、诊断及公共编辑设施 | api + feature + delivery；会话/恢复补 workspace，编辑补 editing | 复用 [公共设施基线](Web客户端架构与脚手架决策.md#web-common-services) 与 [实际入口](../SwiftExport.WebApp/docs/前端基础设施与使用入口.md)，不另建 Storage/缓存/窗口框架 |
| Product 批量编辑 | feature + api + editing + workspace + delivery | 先满足 [Product 门槛](Web首批接口契约验收.md#product-recovery)，主壳/模拟通过不能代替 |
| 只审查未失焦输入关闭保护 | workspace + editing + delivery | Reviewer，只读，不能因技能可写而修代码 |
| 维护 Web Skills/路由 | 根治理工作流及 skill-creator | 仅授权治理文件，不生成应用 |

## 4. 验证范围

结构验证使用 skill-creator 的 quick_validate；额外核对 TOML、引用/锚点、唯一 name、根/子目录入口、路由场景及 diff。治理接线阶段未实施应用功能，其历史记录保留在 [治理接线说明](web-agent-workflow.md#4-交付证据)。后续工程与公共设施已建立，使用点见工程文档；新会话发现、当前整合、真实 Product/PG 与发布分别记录在 [骨架验收记录](../SwiftExport.WebApp/docs/骨架验收记录.md) 及对应业务验收中，不把历史计数改为当前通过。
