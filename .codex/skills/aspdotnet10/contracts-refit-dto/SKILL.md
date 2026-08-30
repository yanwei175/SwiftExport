---
name: aspdotnet10-contracts-refit-dto
description: SwiftExport Contracts 共享契约技能组入口。用于 SwiftExport.Contracts、netstandard2.0 兼容、Refit API、DTO、Wrappers、SysEnums、Common 工具、Request/Response、前后端共享枚举和 Mapster 映射规则。
---

# Contracts / Refit / DTO Skill

## 必读

- `AGENTS.md`
- `.agents/references/工程架构总纲.md`
- `../contracts-netstandard20/SKILL.md`
- 目标 Contracts API、DTO、Request、Response
- 改 `Apis/**` 或 Refit：读 `../contracts-refit-api/SKILL.md`
- 改 `DTOs/**`：读 `../contracts-dto-models/SKILL.md`
- 改 `Wrappers/**`：读 `../contracts-wrappers/SKILL.md`
- 改 `SysEnums/**`：读 `../contracts-shared-enums/SKILL.md`
- 改 `Common/**`：读 `../contracts-common-helpers/SKILL.md`
- 使用通用保存、批量保存或分页模型时读 `../application-transfer-dtos/SKILL.md`
- 涉及 Entity/DTO/前端模型转换时读 `../application-mapster-mapping/SKILL.md`
- 对应 Controller action
- 对应前端调用方

## Contracts 职责

`SwiftExport.Contracts` 保存：

- DTO。
- Refit API Interface。
- Request / Response。
- 前后端共享枚举。
- 通用响应包装。

`SwiftExport.Contracts` 目标框架是 `netstandard2.0`，不使用 `record`、`init`、nullable reference type、file-scoped namespace、collection expression 等新语法。

Core 不负责前端需要的 DTO、Refit API 或枚举。

## API 契约规则

- Controller 路由与 Refit 路由保持一致。
- Request/Response 类型明确，不使用匿名结构。
- 分页接口优先基于 `PagedRequestBase` / `PagedResponseBase<T>` 建立专用 SearchRequest / Response。
- 批量保存优先基于 `BatchSaveRequestBase<TDto>` / `BatchSaveResponseBase` 建立专用 Request / Response。
- 业务命令使用明确命令接口，不伪装成普通 Update。

## DTO 规则

- DTO 只承载传输数据。
- DTO 不承载 UI 状态，例如 `IsSelected`、`IsDirty`、控件对象。
- API/前端联表只读模型优先放 Contracts DTO。
- Dapper 可直接映射 Contracts DTO。
- 字段名要稳定，避免破坏 Refit 和 WPF 绑定。
- DTO 以 API 用例和契约语义为边界；页面不是 DTO 聚合边界，字段相似也不是复用依据。
- 新建或复用 DTO 前必须按 `../contracts-dto-models/references/DTO复用与文件组织门禁.md` 完成复用判断。

## Mapster 规则

- Entity -> DTO、DTO -> Entity 的复杂映射放 Application Mapping。
- DTO -> WPF LineModel 的映射放 `SwiftExport.Client.Wpf` 前端 Mapping 或模型方法中。
- 超过 10 个字段的手写复制应改为 Mapster 或明确 Mapping 类。
- 不在 Repository 中配置复杂 Mapster。
