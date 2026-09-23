# Wrappers 规则

## ServiceResult<T>

路径：`SwiftExport.Contracts/Wrappers/ServiceResult.cs`

字段：

- `Success`
- `Message`
- `Data`
- `Error`：结构化错误，包含 `Code`、`Message` 和可选 `Field`；旧式失败结果可以为空

静态方法：

- `Ok(T data, string msg = "成功")`
- `Fail(string msg)`
- `Fail(string code, string message, string field = null)`

新接口应使用稳定错误码；前端不得解析中文 `Message` 判断 Version、权限、幂等或状态分支。

## BatchSaveRequestBase<TDto>

用于前端提交变更集：

- `CreateItems`
- `UpdateItems`
- `DeleteItems`
- `TotalCount`
- `HasChanges`

## SaveItemRequestBase

用于前端行结果回填：

- `ClientKey`
- `RowIndex`

## SaveItemResult

字段：

- `ClientKey`
- `RowIndex`
- `Operation`
- `Uid`
- `Success`
- `OldVersion`
- `NewVersion`
- `ErrorCode`
- `Message`

## PagedRequestBase / PagedResponseBase<T>

分页请求：

- `Keyword`
- `PageIndex`
- `PageSize`

分页响应：

- `Items`
- `TotalCount`
- `PageIndex`
- `PageSize`
- `TotalPages`

## RepositoryWriteResult

Repository 写入结果：

- `Success`
- `Uid`
- `OldVersion`
- `NewVersion`
- `AffectedRows`
- `ErrorCode`
- `Message`

由 Application Service 转换为 `SaveItemResult` 或 `ServiceResult<T>`。

## 禁止事项

- 禁止把 RepositoryWriteResult 直接作为前端长期契约，除非明确设计。
- 禁止删除 `ClientKey` / `RowIndex`，否则前端批量保存无法稳定回填。
- 禁止把业务状态枚举塞进通用 Wrapper。
