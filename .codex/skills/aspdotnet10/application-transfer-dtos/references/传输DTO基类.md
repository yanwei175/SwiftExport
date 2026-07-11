# 传输 DTO 基类

## BatchSaveRequestBase<TDto>

路径：`SwiftExport.Contracts/Wrappers/BatchSaveRequestBase.cs`

用于前端一次提交新增、修改、删除变更集：

- `CreateItems: List<CreateItemRequest<TDto>>`
- `UpdateItems: List<UpdateItemRequest<TDto>>`
- `DeleteItems: List<DeleteItemRequest>`
- `TotalCount`
- `HasChanges`

## SaveItemRequestBase

保存请求项基类：

- `ClientKey`：前端临时行标识，用于回填保存结果。
- `RowIndex`：前端行号或显示行索引，用于定位错误。

## CreateItemRequest<TDto>

新增项：

- `Data`：新增 DTO。

新增时通常没有持久化 `Uid`，保存后由 `SaveItemResult.Uid` 回填。

## UpdateItemRequest<TDto>

修改项：

- `Uid`：目标实体主键。
- `Version`：乐观并发版本。
- `Data`：修改 DTO。

## DeleteItemRequest

删除项：

- `Uid`：目标实体主键。
- `Version`：乐观并发版本。
- `IsSoftDelete`：默认 `true`。

业务表默认软删除；库存流水等不可变记录不得靠普通删除修正。

## BatchSaveResponseBase

批量保存响应基类：

- `TotalCount`
- `Items: List<SaveItemResult>`
- `SuccessCount`
- `FailedCount`
- `AllSucceeded`
- `AddSuccessCount`
- `UpdateSuccessCount`
- `DeleteSuccessCount`
- `GetAllAffectedIdList()`

## SaveItemResult

单行保存结果：

- `ClientKey`
- `RowIndex`
- `Operation`
- `Uid`
- `Success`
- `OldVersion`
- `NewVersion`
- `ErrorCode`
- `Message`

前端应通过 `ClientKey` 或 `RowIndex` 把结果回填到对应行，成功后更新 `Uid` 和 `Version`。

## PagedRequestBase

分页请求：

- `Keyword`
- `PageIndex`
- `PageSize`

## PagedResponseBase<T>

分页响应：

- `Items`
- `TotalCount`
- `PageIndex`
- `PageSize`
- `TotalPages`

## 使用边界

- Request/Response DTO 放在 Contracts。
- Entity 不直接出 WebApi 或 Refit。
- UI 状态不进入 DTO；`ClientKey`、`RowIndex` 是保存回填辅助字段，不代表业务状态。
- 业务命令不要强行塞进批量保存；确认、过账、拆解、红冲应有独立 API。
