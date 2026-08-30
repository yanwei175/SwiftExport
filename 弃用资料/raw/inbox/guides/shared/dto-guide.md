# DTO Guide

## 适用场景

适用于跨层传输、API 输入输出、Application Service 请求响应、Entity/LineModel 映射和批量保存数据结构。

## 推荐做法

- DTO 只承载传输数据，不承载 UI 状态和控件行为。
- Entity、DTO、LineModel、Request、Response 的职责应清晰分离。
- 映射优先使用项目既有映射工具或集中配置。
- 受控字段如主键、版本、创建人、创建时间、更新时间，应由应用或基础设施明确管理。
- Dapper 查询可以在简单场景直接返回 DTO，但不要让 DTO 反向承担持久化规则。
- 批量保存时用明确变更集表达 Added、Modified、Deleted。

## 禁止事项

- 禁止 DTO 包含 `IsSelected`、`IsDirty`、`IsDeleted`、`RowIndex` 等 UI 状态。
- 禁止在 Repository 中配置 UI 模型映射。
- 禁止创建万能 `MapsterHelper`、`MappingManager` 等大工具类。
- 禁止让请求 DTO 暗含业务流程多个阶段的混合语义。
- 禁止随意改变公开 DTO 字段名和类型。

## Codex 修改前检查清单

- DTO 是应用内部 DTO、API Request、API Response 还是 UI LineModel 转换目标。
- 现有映射配置位置和命名规则是否已确认。
- 字段变更是否影响 API、Refit、前端绑定或数据库。
- 受控字段由哪一层赋值。
- 是否需要兼容旧字段。

## 常见错误

- 直接把 Entity 暴露为 API Response。
- DTO 和 LineModel 合并，导致应用层污染 UI 状态。
- 大段手写重复字段赋值，后续字段遗漏。
- 映射配置散落在 ViewModel 或 Repository。
- 更新实体时覆盖了版本、审计字段或主键。
