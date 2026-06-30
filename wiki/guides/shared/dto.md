# DTO Guide

## 适用场景

用于跨层传输、API 输入输出、Application Service 请求响应、Entity / LineModel 映射和批量保存数据结构。

## SwiftExport 规则

- DTO 只承载传输数据，不承载 UI 状态和控件行为。
- Entity、DTO、LineModel、Request、Response 的职责必须分离。
- DTO 放在 `SwiftExport.Contracts/DTOs/**`，Refit API 和 Controller 返回优先使用 DTO。
- 映射优先使用项目既有 Mapster 配置或已有 Mapping 类。
- Mapster 详细规则见 [Mapster Mapping](mapster-mapping.md)。
- Dapper 查询可以在明确 API/前端传输场景直接返回 DTO，但不要让 DTO 反向承担持久化规则。
- 批量保存时用明确变更集表达新增、修改、删除，不靠 UI 状态污染 DTO。

## 修改前检查

- DTO 是 API Request、API Response、Application DTO 还是 UI Model 转换目标。
- 字段变更是否影响 API、Refit、前端绑定或数据库。
- 受控字段由哪一层赋值。
- 是否需要兼容旧字段。

## 禁止事项

- 禁止 DTO 包含 `IsSelected`、`IsDirty`、`RowIndex`、控件对象等 UI 状态。
- 禁止直接把 Entity 暴露为 API Response。
- 禁止在 Repository 中配置 UI 模型映射。
- 禁止随意改变公开 DTO 字段名和类型。
