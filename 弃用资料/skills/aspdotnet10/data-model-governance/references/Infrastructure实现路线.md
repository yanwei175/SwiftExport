# Infrastructure 实现路线

## 盘点矩阵

对每个 Core Repository Interface 检查：

- 是否存在 Infrastructure 实现。
- Interface 方法是否全部实现。
- 是否已经注册到 DI，注册生命周期是否正确。
- 是否已有 Application Service 调用方。
- 是否使用 `RepositoryBaseContext`。
- 是否共享 `DbSession` 的 Connection 和 Transaction。
- 是否存在重复 SQL、重复接口或职责重叠。

统一输出：

| Repository Interface | 当前实现 | DI注册 | 调用方 | 事务要求 | 缺口 | 实现优先级 |
|---|---|---|---|---|---|---|

## 实现顺序

先根据源码排除已经完整实现的能力，再按依赖顺序安排缺口：

1. `DbSession`、连接工厂、`RepositoryBaseContext`、审计和 DI 基座。
2. 泛型单实体 CRUD 的真实缺口。
3. 产品、BOM、客户、供应商、仓库等主数据查询。
4. 销售订单和毛需求来源。
5. BOM 拆解和净需求。
6. 净需求转采购和 `DemandExecutionLink`。
7. 入库、出库、库存余额和不可变库存流水。
8. PackingList 智能入库、出库和全链路追溯。
9. 有证据支持的查询性能、批量处理和专项索引优化。

## Repository 边界

- 通用仓储只保留稳定的单实体 CRUD。
- 复杂联表、分页、批量和业务查询放具体业务 Repository。
- Repository Interface 放 Core，Dapper 实现放 Infrastructure。
- Application 控制业务事务，Repository 使用当前 `DbSession.Transaction`。
- Repository 不自行创建连接或开启业务事务。
- API 可见的只读联表查询可以直接返回 Contracts Query DTO。
- Infrastructure 不引用 Application，不重复实现 Application 的业务校验。

## 优先级

优先处理：

- 已被 Application 调用但缺少实现或 DI 注册的接口。
- 阻断销售订单到库存主流程的接口。
- 涉及正式单据、库存余额和库存流水一致性的接口。
- 多处复制 SQL 且已经存在一致性风险的查询。
- 有查询计划、日志或明确耗时证据的性能问题。

延后处理：

- 尚无调用方的预留接口。
- 可由现有仓储稳定完成的重复接口。
- 当前阶段未启用的生产或外协能力。
