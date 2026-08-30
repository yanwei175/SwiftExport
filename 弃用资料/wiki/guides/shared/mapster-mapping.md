# Mapster Mapping Guide

## 适用场景

用于 SwiftExport 中 Entity、DTO、前端 Model / LineModel、Request、Response 之间的对象映射。

## SwiftExport 事实

后端 Mapster 配置入口：

- `SwiftExport.Application/Mappings/MapsterConfig.cs`
- `SwiftExport.Application/DependencyInjection.cs`

前端 Mapster 配置入口：

- `SwiftExport.View.FW/Mappings/MapsterConfig.cs`
- `SwiftExport.View.FW/DI/DependencyInjection.cs`

映射类通常实现 `Mapster.IRegister`，并通过 `config.Scan(typeof(MapsterConfig).Assembly)` 注册。

## SwiftExport 规则

- 简单同名字段转换优先用 `Adapt<T>()` 或 `source.Adapt(target)`。
- 超过 10 个字段的手工复制，优先改成 Mapster 或已有 Mapping 类。
- 后端复杂映射放在 `SwiftExport.Application/Mappings/**`。
- 前端 DTO 与 UI Model / LineModel 映射放在 `SwiftExport.View.FW/Mappings/**`。
- Repository 负责 SQL 与 Dapper，不负责注册复杂 Mapster 配置。
- Dapper 查询直接返回 DTO 且字段别名匹配时，不需要再 Mapster。

## 受控字段

以下字段不要无脑双向覆盖：

- `Uid`
- `Version`
- `CreatedBy`
- `CreatedDate`
- `UpdatedBy`
- `UpdatedDate`

是否 Ignore 以目标 Entity / DTO 的业务语义和已有 Mapping 为准。

## 修改前检查

- 当前映射发生在后端 Application 还是前端 View.FW。
- 是否已有对应 `IRegister` Mapping 类。
- 是否影响 DTO、前端 Model、Entity 或保存请求。
- Dapper SQL 字段别名是否已经直接匹配目标 DTO。

## 禁止事项

- 禁止在 ViewModel 中注册 Mapster 全局配置。
- 禁止在 Repository 中堆复杂对象映射规则。
- 禁止为单次转换创建 `MapsterHelper` / `MapsterUtil` 这类无边界工具。
- 禁止用 Mapster 覆盖审计字段和并发字段而不显式说明。
