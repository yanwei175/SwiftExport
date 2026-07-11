# Mapster 映射规则

## 当前源码事实

`MapsterConfig` 位于 `SwiftExport.Application/Mappings/MapsterConfig.cs`：

```csharp
var config = new TypeAdapterConfig();
config.Scan(typeof(MapsterConfig).Assembly);
services.AddSingleton(config);
```

当前 Application 可通过扫描当前程序集注册 Mapster 配置。

## 使用原则

- Service 层可以使用 Mapster 在 Entity 与 Contracts DTO 之间转换。
- 前端也可以使用 Mapster 在 Contracts DTO 与页面 Model、LineModel 之间转换。
- 简单同名字段使用 `Adapt<T>()`。
- 字段名不同、枚举转换、嵌套对象、集合明细、只读联表模型应写显式配置。

## 配置位置

- Application 服务层映射配置放 `SwiftExport.Application/Mappings/**`。
- 前端页面模型映射放 `SwiftExport.Client.Wpf/Mappings/**` 或 Feature 内明确的 `Mappings` 位置。
- 不在 Controller 中写临时映射。
- 不在 Repository 中写业务 DTO 组装；Dapper 可直接映射只读 DTO，但业务含义组装仍由 Application 负责。

## 与 BaseService 的关系

`BaseService` 默认使用：

- `item.Data.Adapt<TEntity>()`
- `item.Data.Adapt(existing)`
- `entity.Adapt<TDto>()`

因此继承 BaseService 的 DTO 与 Entity 字段命名应尽量保持一致；不一致时必须补 Mapster 配置或重写具体 Service 方法。

## 禁止事项

- 禁止为了省事把 Entity 直接返回给前端。
- 禁止在 Controller 中拼装复杂 DTO。
- 禁止在 Repository 中调用 Application 映射逻辑。
- 禁止把 UI 状态字段映射进 Entity。
