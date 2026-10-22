# DTO 分类与字段来源

## DTO 分类

### Entity DTO

- 表达实体可持久化业务数据，用于标准读取和保存传输。
- 可以包含 `Uid`、`Version` 和必要审计字段。
- 不继承 Core Entity，不包含 WPF UI 状态。
- 不因页面方便而复制大量联表展示字段。

### Query DTO

- 用于列表、详情、Lookup、统计和联表查询。
- 可以扁平化多个表，并允许 Infrastructure 使用 Dapper 直接映射。
- SQL 必须显式选择字段，别名与 DTO 属性一致。
- 实时联表字段、计算字段和展示字段不得反向写回 Entity。

### Command DTO

- 表达确认、取消确认、拆解、转采购、过账、反过账、红冲、关闭、作废等明确业务意图。
- 只携带命令需要的参数，不伪装为普通 Update DTO。
- 需要并发控制时携带 `Uid` 和 `Version`。
- 批量命令必须能返回逐项执行结果。

## 联表字段血缘

每个非本表字段必须记录：

- DTO 字段。
- 来源表和字段。
- Join 类型、表别名和 Join 条件。
- 一对一、一对多或多对一的关系基数。
- `IsDeleted` 等必须应用的关联过滤条件。
- 实时查询或历史快照语义。
- 空值策略。
- 更新责任和使用场景。

简单且稳定的来源可以写 XML 注释：

```csharp
/// <summary>默认供应商名称。</summary>
/// <remarks>
/// 来源：Supplier.Name。
/// 关联：Product.DefaultSupplierUid = Supplier.Uid。
/// </remarks>
public string SupplierName { get; set; }
```

复杂、多表或被多个查询复用的 DTO 必须生成映射表：

| DTO字段 | 来源表.字段 | Join类型/别名 | Join条件 | 关系基数 | 关联过滤 | 实时/快照 | 空值策略 | 更新责任 |
|---|---|---|---|---|---|---|---|---|
| SupplierName | Supplier.Name | LEFT JOIN Supplier s | Product.DefaultSupplierUid = s.Uid | 多对一 | s.IsDeleted = 0 | 实时 | 可空或空字符串 | Supplier |

## 检查规则

- 不把 Entity 直接返回前端。
- 不让 Command DTO 混入大量页面展示字段。
- 不让 Query DTO 被 Repository 当作写入模型。
- 不让 Mapster 隐藏联表字段来源；联表事实以 SQL 和映射表为准。
- DTO 字段改名必须检查 Dapper 别名、Mapster、JSON、Refit 和 WPF Binding。
- Contracts 必须保持 `netstandard2.0` 和当前 C# 语法兼容。
