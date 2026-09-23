---
name: aspdotnet10-contracts-wrappers
description: SwiftExport Contracts Wrappers 技能。用于 ServiceResult、RepositoryWriteResult、CreateItemRequest、UpdateItemRequest、DeleteItemRequest、SaveItemResult、BatchSaveRequestBase、BatchSaveResponseBase、PagedRequestBase、PagedResponseBase、SaveDocumentRequestBase 的任务。
---

# Contracts Wrappers Skill

## 必读

- `../contracts-netstandard20/SKILL.md`
- `SwiftExport.Contracts/Wrappers/ServiceResult.cs`
- `SwiftExport.Contracts/Wrappers/BatchSaveRequestBase.cs`
- `SwiftExport.Contracts/Wrappers/BatchSaveResponseBase.cs`
- `SwiftExport.Contracts/Wrappers/PagedRequestBase.cs`
- `SwiftExport.Contracts/Wrappers/PagedResponseBase.cs`
- `SwiftExport.Contracts/Wrappers/RepositoryWriteResult.cs`
- `SwiftExport.Contracts/Wrappers/SaveDocumentRequestBase.cs`
- 涉及 Application CRUD 时读 `../application-transfer-dtos/SKILL.md`

## 什么时候使用

- 修改统一返回、批量保存、分页、保存结果、Repository 写入结果。
- 前端 BaseListModel / 批量保存需要对接后端。
- Application Service 需要返回保存结果。
- Repository 写入需要返回乐观并发结果。

## 核心规则

- `ServiceResult<T>` 是统一服务返回包装。
- 新接口失败优先使用结构化 `Error.Code`、`Error.Message` 和可选 `Error.Field`；中文 Message 用于展示，不作为客户端分支协议。
- `RepositoryWriteResult` 是 Repository 写入结果，不直接替代 API response。
- 批量保存使用 `BatchSaveRequestBase<TDto>` 和 `BatchSaveResponseBase`。
- 单行保存结果用 `SaveItemResult`，前端通过 `ClientKey` / `RowIndex` 对齐行。
- 分页使用 `PagedRequestBase` 和 `PagedResponseBase<T>`。
- 单据头行保存可使用 `SaveDocumentRequestBase<THead,TLine>` 或专用 Request。
- Version 冲突、命令忙、幂等冲突、状态阻断和未知结果必须使用不同稳定错误码。

## 详细参考

- `references/Wrappers规则.md`
