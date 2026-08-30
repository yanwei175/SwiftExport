# SwiftExport Mapster Mapping 草稿

## 当前项目事实

SwiftExport 后端和前端都使用 Mapster。

后端配置：

- `SwiftExport.Application/Mappings/MapsterConfig.cs`
- `SwiftExport.Application/DependencyInjection.cs`

前端配置：

- `SwiftExport.View.FW/Mappings/MapsterConfig.cs`
- `SwiftExport.View.FW/DI/DependencyInjection.cs`

映射类通常实现 `IRegister`，并通过 `config.Scan(typeof(MapsterConfig).Assembly)` 注册。

## 适用范围

- Entity <-> DTO
- DTO <-> 前端 Model / LineModel
- Request / Response 和 DTO 的简单转换
- 集合与分页结果转换

不适用于：

- SQL 查询本身。
- Dapper 字段映射配置。
- Repository 中复杂业务编排。

## SwiftExport 规则

- 简单同名字段转换优先使用 `Adapt<T>()` 或 `dto.Adapt(target)`。
- 超过 10 个字段的手工复制优先改成 Mapster。
- 复杂映射放到 `Mappings/**` 下的 `IRegister` 类。
- 后端映射配置放 `SwiftExport.Application/Mappings/**`。
- 前端页面 Model 映射配置放 `SwiftExport.View.FW/Mappings/**`。
- Repository 负责 SQL 和 Dapper，不负责注册复杂 Mapster 配置。

## 审计和主键字段

以下字段不要无脑覆盖：

- `Uid`
- `Version`
- `CreatedBy`
- `CreatedDate`
- `UpdatedBy`
- `UpdatedDate`

是否 Ignore 以目标 Entity / DTO 的业务语义为准。

## Dapper 规则

如果 Dapper 查询直接返回 DTO，字段别名已经匹配 DTO 属性，则不需要再 Mapster。

如果 Dapper 返回 Entity，再由 Application Service 转 DTO。

## ViewModel 规则

ViewModel 中可以使用 Mapster 做 DTO 与前端 Model 的转换，但不要在 ViewModel 里注册 Mapster 配置，也不要堆大量手工字段复制。
