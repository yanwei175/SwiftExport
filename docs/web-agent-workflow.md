# Web Agent 工作流与治理接线

原规划：2026-10-07；接线更新：2026-10-09。正式角色、候选路由、工作流和六项技能已落盘；不代表 Web 工程、自动路由执行器或浏览器验收已经实现。执行顺序只维护在 [Web 前端工作流](../.agents/workflows/Web前端工作流.md)，本文保留入口和阶段边界，不再复制 SOP。

## 1. 角色与读取入口

入口：[AGENTS](../AGENTS.md) → [Router](../.agents/router.toml) 及 [判定 policy](../.agents/references/架构治理与路由判定规则.md) → 命中角色/工作流 → [Web 技能组](../.codex/skills/react-web/index/SKILL.md) → 实际源码/测试。

| 意图/目标 | 主角色与入口 | 边界 |
| --- | --- | --- |
| React Web 实现/补测 | [web-frontend](../.agents/web-frontend.toml)、[Web 工作流](../.agents/workflows/Web前端工作流.md) | 授权 SwiftExport.WebApp 文件/测试；WPF 只读参考 |
| Web 业务页面/API 用例规划 | 既有 Planner 及命中规划工作流 | 方案不自动变成代码写入授权 |
| Web 代码审查 | 既有 Reviewer/代码审查工作流，加相关 Web 技能 | 只读指定 diff 及必要调用链 |
| Router/AGENTS/Skills/工作流同步 | architecture-governor/架构治理工作流 | 只落实授权治理规则 |
| 后端真实接口或 DTO 变更 | 既有 Backend，按独立授权交接 | Web 角色不借依赖扩权 |

任务关键词只召回候选；“参考 .NET 10 ShellWindow 做 Web 主壳”按 Web 写入目标处理。仅选角色不授权新建聊天、并行 Agent、共享文件接管、部署或推送。

## 2. 规则所有权

| 材料 | 维护内容 |
| --- | --- |
| [架构决策](Web客户端架构与脚手架决策.md#web-authority) | 技术选择、依赖/字段所有权、API 分层、架构变更 |
| [多标签设计](Web多标签工作区设计.md#workspace-contract) | 完整输入保护、保存分类/锁、关闭、恢复及公共接口 |
| [契约验收](Web首批接口契约验收.md#product-recovery) | 指定范围后端事实、历史证据与接入缺口 |
| [编辑基础设施](web-frontend-architecture.md) / [权限设计](web-permission-design.md) | 集合算法、UI 交互及权限领域细则，遵守上层规则 |
| [Skills 接线](web-skills-plan.md) | 六项技能位置及职责；具体做法只在正式 SKILL 维护 |
| 正式工作流 | 执行顺序、文件归属、选择验证及交付阶段 |

## 3. 首期阶段顺序

| 工作包 | 依赖与交付 | 当前状态 |
| --- | --- | --- |
| 治理接线 | 角色/路由/工作流/技能结构、引用及判定场景 | 本轮建立；实际新会话发现另核验 |
| 工程/主壳 | 官方 Framework SPA、独立构建、两编辑验证页签及 Ant Tabs + Activity 联合验收 | 未实施；下一任务可参考现有 WPF Shell 行为 |
| 公共前端底座 | 唯一 transport/Query/权限接线，按真实用例建立 editing/Grid | 未实施；不提前建万能框架 |
| Product 读取/整页 | 真实 login/me、候选权限及页面可用性 | 浏览器/真实账号未验收 |
| Product 真实写入 | [稳定结果分类门槛](Web首批接口契约验收.md#product-recovery)、完整回执及未知恢复 | 门槛未完成；不能以已有九端点生成链路代替 |
| 最终整合/发布 | 当前版本定向检查，真实环境及部署分别验收 | 未验收；旧隔离/模拟通过不替代 |

## 4. 交付证据

按 [delivery Skill](../.codex/skills/react-web/react-web-delivery/SKILL.md) 和 [测试总纲](../.agents/references/测试与验收总纲.md) 选择检查；记录当前版本、命令、通过/失败/未运行范围。纯治理不运行 npm/.NET/PG。业务实现必须验证适用的“输入保护→保存分类→回执接纳/解锁→未知恢复→人工终止”链条，不能只看 dirty 或状态码。

2026-10-09 本轮静态校验：7 个角色 TOML、14 条唯一路由及其引用通过；六项 Skill 的 quick_validate、UI 元数据及发现链接目标/正文一致；18 个范围文件的 UTF-8、围栏及本地引用/显式锚点可达。按 policy 人工核对 Web Shell 实施→web-frontend、WPF 页面→frontend、Web 审查→reviewer、规则同步→architecture-governor、跨端规划→planner，实际判断与 [代表性场景](../.agents/references/架构治理与路由判定规则.md) 一致；这不是自动路由测试。未启动新会话测试发现，未创建 Web 工程，未安装依赖、运行业务构建/测试或数据库。
