# Skill: Mapster Mapping

## 适用范围

本项目统一使用：

```text
Mapster
```

进行对象映射。

适用于：

- DTO ↔ Entity
- DTO ↔ LineModel
- Request ↔ DTO
- Response ↔ DTO
- QueryResult ↔ DTO

不适用于：

- SQL查询
- Repository
- Dapper映射

------

# 基本原则

允许：

```text
Mapster负责对象转换
```

禁止：

```text
手工复制几十个字段
```

错误：

```csharp
dto.ProductCode = model.ProductCode;
dto.ProductName = model.ProductName;
dto.Price = model.Price;
dto.Remark = model.Remark;
...
```

正确：

```csharp
model.Adapt(dto);
```

或者：

```csharp
var dto = model.Adapt<ProductDto>();
```

------

# 映射方向

推荐：

```text
Entity
↕
DTO

DTO
↕
LineModel

Request
↓
DTO

DTO
↓
Response
```

------

# DTO → LineModel

允许：

```csharp
public override void LoadFromDto(ProductDto dto)
{
    dto.Adapt(this);

    AcceptChanges();
}
```

推荐。

------

# LineModel → DTO

允许：

```csharp
public override ProductDto ToDto()
{
    return this.Adapt<ProductDto>();
}
```

推荐。

------

# Entity → DTO

允许：

```csharp
var dto = entity.Adapt<ProductDto>();
```

推荐。

------

# DTO → Entity

允许：

```csharp
dto.Adapt(entity);
```

推荐。

------

# 更新实体规则

更新时优先：

```csharp
dto.Adapt(entity);
```

不要：

```csharp
entity.Name = dto.Name;
entity.Code = dto.Code;
entity.Price = dto.Price;
```

除非有特殊业务规则。

------

# 特殊业务字段

如果字段需要业务控制：

例如：

```text
CreatedDate
CreatedBy
Version
Uid
```

禁止直接映射覆盖。

推荐：

```csharp
TypeAdapterConfig<ProductDto, Product>
    .NewConfig()
    .Ignore(x => x.Uid)
    .Ignore(x => x.CreatedDate)
    .Ignore(x => x.CreatedBy)
    .Ignore(x => x.Version);
```

------

# 配置位置

统一放：

```text
Application
└─ Mapping
```

例如：

```text
Application
└─ Mapping
    ├─ ProductMapping.cs
    ├─ BomMapping.cs
    ├─ PricingMapping.cs
    └─ MappingRegister.cs
```

不要：

```text
ViewModel里配置Mapster

Repository里配置Mapster
```

------

# MappingRegister

统一注册：

```csharp
public static class MappingRegister
{
    public static void Register()
    {
        ProductMapping.Register();
        BomMapping.Register();
        PricingMapping.Register();
    }
}
```

程序启动时：

```csharp
MappingRegister.Register();
```

只执行一次。

------

# 命名规则

推荐：

```text
ProductMapping
BomMapping
PricingMapping
PurchaseMapping
SalesOrderMapping
```

不要：

```text
MapsterHelper
MapsterManager
MapsterUtil
```

------

# DTO 与 LineModel

推荐：

```csharp
public class ProductLineModel
    : BaseLineModel<ProductDto>
{
    public override void LoadFromDto(ProductDto dto)
    {
        dto.Adapt(this);
        AcceptChanges();
    }

    public override ProductDto ToDto()
    {
        return this.Adapt<ProductDto>();
    }
}
```

不要：

```csharp
ProductCode = dto.ProductCode;
ProductName = dto.ProductName;
Price = dto.Price;
Remark = dto.Remark;
...
```

大量复制。

------

# 集合映射

允许：

```csharp
var dtoList =
    entities.Adapt<List<ProductDto>>();
```

推荐。

------

# 分页映射

允许：

```csharp
var result =
    pageResult.Items
              .Adapt<List<ProductDto>>();
```

不要循环复制。

------

# Dapper 查询规则

Dapper返回DTO时：

允许：

```csharp
QueryAsync<ProductDto>()
```

不需要再Mapster。

例如：

```csharp
var result =
    await connection.QueryAsync<ProductDto>(
        sql,
        param);
```

直接返回即可。

------

# Dapper Entity规则

如果Dapper返回Entity：

允许：

```csharp
var dto =
    entity.Adapt<ProductDto>();
```

再转换。

------

# Application Service规则

允许：

```csharp
var dto =
    entity.Adapt<ProductDto>();
```

推荐。

------

# ViewModel规则

允许：

```csharp
dto.Adapt(lineModel);
```

推荐。

禁止：

```text
ViewModel中写几十行字段复制
```

------

# Repository规则

Repository负责：

```text
SQL

Dapper

数据库访问
```

Repository不负责：

```text
复杂Mapster配置
```

------

# Ignore规则

以下字段通常忽略：

```text
Uid

Version

CreatedBy

CreatedDate

UpdatedBy

UpdatedDate
```

根据项目情况决定。

------

# 双向映射规则

如果需要双向映射：

推荐：

```csharp
TypeAdapterConfig<Product, ProductDto>
    .NewConfig();

TypeAdapterConfig<ProductDto, Product>
    .NewConfig();
```

分别配置。

不要依赖隐式行为。

------

# 性能规则

高频调用：

允许：

```csharp
TypeAdapterConfig
```

提前注册。

不要：

```csharp
每次调用重新配置
```

------

# 禁止事项

禁止：

```text
MapsterHelper

MapsterManager

MapsterUtil
```

禁止：

```text
ViewModel手工复制几十个字段
```

禁止：

```text
Repository里写映射配置
```

禁止：

```text
启动过程中重复注册Mapster配置
```

------

# Codex生成规则

生成映射代码时：

优先：

```csharp
Adapt<T>()
```

其次：

```csharp
dto.Adapt(entity)
```

禁止：

```csharp
dto.Name = model.Name;
dto.Code = model.Code;
dto.Price = model.Price;
...
```

超过10个字段的手工复制。

复杂映射统一放：

```text
Application/Mapping
```

统一管理。