---
name: react-web
description: SwiftExport React Web 任务入口。用于 Web 主壳、Workspace、业务 feature、API 接入、编辑保存及验证；按任务选择执行技能。WPF 仅作参考时仍按 Web 目标处理，不覆盖 WPF 或后端实现规范。
---

# SwiftExport Web 任务入口

## 适用与输入

针对 `SwiftExport.WebApp` 的实现、补测、规划或审查；角色仍由根 [AGENTS](../../../../AGENTS.md) 和 [Router](../../../../.agents/router.toml) 按意图选取，本技能不把只读请求转成实现。输入是用户授权范围、实际目标目录、命中工作流及相关源码/测试。

## 单一规则来源

- [架构决策](../../../../docs/Web客户端架构与脚手架决策.md)：选型、目录/依赖、字段所有权、API 分层及变更门禁。
- [多标签设计](../../../../docs/Web多标签工作区设计.md)：身份、完整输入保护摘要、保存/关闭/恢复公共契约。
- [契约验收](../../../../docs/Web首批接口契约验收.md)：指定范围事实、历史证据及 Product 真实写入前门槛。
- [Web 工作流](../../../../.agents/workflows/Web前端工作流.md)：实施顺序、共享文件归属及阶段交付；已读内容依根规则去重。

## 必要执行技能

| 目标 | 加载 |
| --- | --- |
| 主壳布局、导航、feature、普通 Form/验证、权限消费、跨模块调用 | [react-web-feature](../react-web-feature/SKILL.md) |
| OpenAPI/Orval、传输、查询键、mutation、身份/错误接线 | [react-web-api](../react-web-api/SKILL.md) |
| baseline/dirty、Grid 缓冲、单据头行、冻结、回执/批量保存 | [react-web-editing](../react-web-editing/SKILL.md) |
| Workspace、页签身份、Activity、关闭、登录恢复/登出、未知记录导出 | [react-web-workspace](../react-web-workspace/SKILL.md) |
| 实现/补测交付，或评估证据是否足够 | [react-web-delivery](../react-web-delivery/SKILL.md) |

多命中同时补齐；Web Grid 不因此转 WPF Grid Architect。主壳参考 .NET 10 客户端时，feature + workspace + delivery 通常适用；真实登录再补 api，有保存验证页再补 editing。

## 写入边界与禁止事项

由本次请求及命中角色约束，不因加载技能扩大授权。Web 实现通常限 `SwiftExport.WebApp/**`；WPF、Contracts、后端只读，需改契约另按授权交接。Skill/工作流维护交治理角色；不得新建另一套 API Client、状态库、控制器继承框架或自行换 Router 模式。此组不授权创建任务、并行委派、部署、推送或运行库写入。

技能正文仅在 `.codex/skills/react-web` 维护；`.agents/skills` 的目录链接提供发现，不复制正文。从发现链接读取时，以仓库根对应正文目录解析相对引用；五项子技能亦遵循此约定。不存在的运行时公共入口必须标待实施，不凭规划路径声称已经复用。

## 验收

交付前确认必要技能已加载、规范引用可达、目标/事实/未验证项分开；实现任务按 delivery 执行实际检查。静态接线通过不表示框架组合或业务验收通过。
