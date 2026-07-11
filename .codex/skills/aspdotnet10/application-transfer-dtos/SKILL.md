---
name: aspdotnet10-application-transfer-dtos
description: SwiftExport CRUD、批量保存、分页传输 DTO 基类技能。用于 CreateItemRequest、UpdateItemRequest、DeleteItemRequest、SaveItemResult、BatchSaveRequestBase、BatchSaveResponseBase、PagedRequestBase、PagedResponseBase 的后端和前端契约设计。
---

# Application Transfer DTOs Skill

## 必读

- `SwiftExport.Contracts/Wrappers/BatchSaveRequestBase.cs`
- `SwiftExport.Contracts/Wrappers/BatchSaveResponseBase.cs`
- `SwiftExport.Contracts/Wrappers/PagedRequestBase.cs`
- `SwiftExport.Contracts/Wrappers/PagedResponseBase.cs`
- `SwiftExport.Contracts/Common/SaveItemResultFactory.cs`
- 涉及 API 契约时读 `../contracts-refit-dto/SKILL.md`

## 什么时候使用

- 新增批量保存 Request / Response。
- 新增分页查询 Request / Response。
- 设计前后端共享 CRUD 契约。
- 前端 BaseListModel / 行编辑保存需要对接后端批量保存。
- 服务层需要返回 `SaveItemResult` 或 `BatchSaveResponseBase`。

## 核心规则

- 这些类型属于 `SwiftExport.Contracts`，前后端都可以依赖。
- 标准保存使用 `CreateItemRequest<TDto>`、`UpdateItemRequest<TDto>`、`DeleteItemRequest`。
- 批量保存 Request 继承或复用 `BatchSaveRequestBase<TDto>`。
- 批量保存 Response 继承或复用 `BatchSaveResponseBase`。
- 分页查询使用 `PagedRequestBase` 和 `PagedResponseBase<T>`。
- `ClientKey`、`RowIndex` 用于前端行与后端结果对齐，不是数据库字段。
- `Uid`、`Version` 用于乐观并发和实体定位。

## 详细参考

需要字段解释和使用模式时读：

- `references/传输DTO基类.md`
