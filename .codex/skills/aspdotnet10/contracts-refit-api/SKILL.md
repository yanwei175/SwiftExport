---
name: aspdotnet10-contracts-refit-api
description: SwiftExport Contracts Refit API 技能。用于 SwiftExport.Contracts/Apis、IGenericApi、IAuthApi、Refit Get/Post/Put/Delete、Body、Controller 路由对齐、前端依赖 API 接口设计相关任务。
---

# Contracts Refit API Skill

## 必读

- `../contracts-netstandard20/SKILL.md`
- `SwiftExport.Contracts/Apis/BaseApi/IGenericApi.cs`
- `SwiftExport.WebApi/Controllers/Base/BaseController.cs`
- 目标 `SwiftExport.Contracts/Apis/**`
- 对应 WebApi Controller
- 涉及 Controller 边界时读 `../webapi-controller-boundary/SKILL.md`

## 什么时候使用

- 新增或修改 `SwiftExport.Contracts/Apis/**`。
- 对齐 Refit 接口和 Controller action。
- 修改 `IGenericApi<TDto>` 或通用 API 路由。
- 新增前端依赖的后端接口。
- 修改 Auth API、业务命令 API、分页/批量保存 API。

## 核心规则

- Refit API Interface 放 `SwiftExport.Contracts/Apis/<Module>`。
- Controller 路由、HTTP 方法、参数来源、Request/Response 必须与 Refit 接口一致。
- `IGenericApi<TDto>` 对应 WebApi `BaseController` 的基础 CRUD 概念，但当前返回类型和请求模型要以源码事实为准，不要盲目套用。
- 业务命令必须有明确路由，如 `confirm`、`cancel-confirm`、`post`、`reverse-post`。
- Request/Response 使用 Contracts DTO，不使用匿名对象。
- 需要 body 时使用 `[Body]`。

## 详细参考

- `references/Refit接口规则.md`
