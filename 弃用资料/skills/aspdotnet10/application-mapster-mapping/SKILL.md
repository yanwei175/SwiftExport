---
name: aspdotnet10-application-mapster-mapping
description: SwiftExport Mapster 映射技能。用于 Application Service、Contracts DTO、实体转换、前端模型转换、TypeAdapterConfig、IRegister 映射配置，以及避免在 Controller/Repository 中散落临时映射。
---

# Application Mapster Mapping Skill

## 必读

- 后端映射读 `SwiftExport.Application/Mappings/MapsterConfig.cs`。
- WPF 前端映射读 `SwiftExport.Client.Wpf/Mappings/MapsterConfig.cs`；目标 Feature 存在专用 `Mappings` 目录时同时读取。
- 涉及服务 CRUD 时读 `../application-base-service/SKILL.md`
- 涉及 DTO 契约时读 `../contracts-refit-dto/SKILL.md`

## 什么时候使用

- 新增 Entity 与 DTO 映射。
- 新增服务层 `Adapt<TEntity>()`、`Adapt<TDto>()`。
- 复杂字段名、嵌套对象、枚举或只读模型需要显式映射。
- 前端模型与 Contracts DTO 之间需要 Mapster 转换。

## 核心规则

- Mapster 是服务层和前端都可使用的映射工具。
- Application 的全局注册入口是 `MapsterConfig.RegisterMappings(IServiceCollection services)`。
- WPF 前端的统一注册入口是 `SwiftExport.Client.Wpf/Mappings/MapsterConfig.cs`，当前通过模块初始化注册到 `TypeAdapterConfig.GlobalSettings`。
- 简单同名字段可使用默认 `Adapt`。
- 复杂映射应集中成配置，不要散落在 Controller、Repository 或临时代码中。
- DTO 与前端模型之间的结构性字段复制默认使用 Mapster；不要新增大段逐字段复制，也不要创建只包装逐字段复制的 Mapping 类。
- 业务计算、状态决策、跨来源组装或少量具有明确语义的赋值保留显式代码，不要让映射配置隐藏业务规则。
- Repository 做 Dapper 映射时优先返回 Entity 或 Contracts 只读 DTO，不承担业务 DTO 组装逻辑。

## 详细参考

需要配置边界和示例时读：

- `references/Mapster映射规则.md`
