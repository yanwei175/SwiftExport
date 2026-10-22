# DTO 模型规则

## BaseDto

路径：`SwiftExport.Contracts/DTOs/Base/BaseDto.cs`

字段：

- `Uid`
- `Version`
- `IsDeleted`
- `CreatedDate`
- `CreatedBy`
- `UpdatedDate`
- `UpdatedBy`

`Version` 用于乐观并发，前端保存 Update/Delete 时必须带回。

## Auth DTO

路径：`SwiftExport.Contracts/DTOs/Auth`

常用：

- `LoginRequestDto`
- `LoginTokenDto`
- `LoginResultDto`
- `MeDto`
- `RegisterRequestDto`
- `ResetPasswordRequestDto`
- `ResetPasswordResultDto`
- `DocumentPermissionDto`

`MeDto.Roles` 用于前端权限判断，`IsAdmin` 是基于角色的便利属性。

## 单据基础 DTO

路径：`SwiftExport.Contracts/DTOs/Base`

包括：

- `SaveBillRequest<THeader,TItem>`
- `SaveBillResponse`
- `ConfirmationItemRequest`
- `ConfirmationItemResponse`
- `BatchConfirmationRequest`

确认、取消确认等业务命令应使用明确 Request/Response，不伪装为普通 Update。

## DTO 设计检查

- 是否需要前后端共用。
- 是否需要被 Dapper 直接映射。
- 是否需要 Mapster 映射。
- 字段是否为业务语言。
- 是否包含 `Uid` / `Version`。
- 是否误放了 UI 状态。

## 禁止事项

- 禁止 DTO 引用 WPF 类型。
- 禁止 DTO 引用 WebApi、Application、Infrastructure。
- 禁止把 Entity 导出给前端。
- 禁止在 DTO 中放业务流程方法。
