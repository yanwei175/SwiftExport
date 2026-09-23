# PackingList 自动入出库内部接口交接（第一阶段）

> 文档归档于 Shipping。当前入口见 [Shipping 文档索引](../README.md)，接口现状见 [API 索引](PackingList接口索引.md)，后续方案见 [第三阶段计划](PackingList第三阶段实施计划.md)。本文保留历次设计与实施记录；较早的“未接线／SQL 未执行”等描述不代表当前状态，最新验收摘要以索引为准。

## 实现边界

新增 `IInboundPostingPort`、`IOutboundPostingPort` 和 `IAutomaticPostingScope`，全部 Scoped。两类 Port 共用一个执行器与一个事务资源上下文，不创建 HTTP API，不注册前端调用，不实现箱单来源解析、分配或正式过账入口。

普通负库存仍阻断；整组纠错、受控负库存和库存差异处理属于第三阶段，当前接口没有 AllowNegative 开关。

## 第二阶段调用约定

1. 外层服务认证授权、冻结箱单及来源、完成业务数量和仓库分配。
2. 开启唯一事务并设置共享 `DbSession.Transaction`。入库、出库使用不同的稳定 `CommandId`，共用 `CorrelationId` 和 `PackingListId`。
3. 调用 `PrepareAsync`，一次声明全部子命令、采购行、入出库单据 Uid、正向行键和仓库产品键。新单据 `GroupId` 就是目标单据 Uid，新行 `LineId` 就是物理行和正向流水的 CommandItemId。已有入库反向必须声明其全部采购来源。
4. 调用入库 `CreateAndPostAsync`，再调用出库 `CreateAndPostAsync`。同事务可读取自动入库增加的余额。
5. 保存箱单过账状态、客供件计算依据和执行关联，再提交。任何异常必须结束本次操作；Port 会回滚并清除共享事务，外层不可吞错后继续提交。

无事务或未准备资源时拒绝。资源集合复制后冻结，不能在分单写入中追加新仓、新产品、新来源或新命令。未使用的已声明资源不会产生库存。

```csharp
using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
context.Session.Transaction = transaction;
try
{
    await postingScope.PrepareAsync(resources, token);
    var receipts = await inboundPort.CreateAndPostAsync(inboundRequest, token);
    var issues = await outboundPort.CreateAndPostAsync(outboundRequest, token);
    // 第二阶段：保存箱单状态及完整业务执行依据，不能在 Port 内提交箱单。
    token.ThrowIfCancellationRequested();
    transaction.Commit();
}
catch
{
    try { transaction.Rollback(); } catch { /* 内核可能已经回滚 */ }
    throw;
}
finally { context.Session.Transaction = null; }
```

上述示例不是已实现的 PackingList 编排服务。空的入库计划应跳过入库 Port，不发送空批次。

## 输入、分组与结果

- 创建使用 `AutomaticPostingRequest`；各组按仓库及出库类型组织，组身份在批次内唯一。入库 OutboundType 为空，出库仅允许 FinalCustomerShipment／CustomerSuppliedConsumption。
- 每行携带 LineId、BasisId、ProductId、Unit、正 Quantity；入库必须有 PurchaseLineId，出库不得带采购行，客供件不按采购分摊。
- 当前保留各输入物理行，不自动合并相同采购行。数量最多四位小数，服务不静默舍入。同产品不同依据不受手工页面产品唯一规则限制。
- 入库采用 PurchaseReceipt + Automatic，流水为 PackingListAutoReceiptInbound；按本批同采购行合计检查容量，并检查供应商虚拟仓、单位及采购可收资格。没有独立人工确认步骤。
- 自动单据 CommandId 使用稳定组身份，批次 CommandId 用于批次日志及库存命令；避免一个批次多单违反单头命令唯一约束。
- 返回单据身份、编号、版本、状态与 `RequestLineId → DocumentLineId → LedgerId`、BasisId 关系，并将完整结果保存到 AutomaticInventory 模块日志。同一命令同内容重放返回 AlreadyApplied。
- `OriginalLineId` 保持原入出库明细引用语义，不存 GrossDemand／需求依据。BasisId 映射通过批次结果日志保留；第二阶段必须另存客供件采用的数量计算快照，不能只有一个不透明依据 ID。
- 业务日期由外层明确传入，不从 InvoiceDate 猜测；当前用户取服务上下文，不接受客户端指定操作人。

## 锁序、反向与失败

准备顺序：所有库存子命令锁 → 采购头行 → 入库头行、出库头行 → 相关原流水及反向／替代键 → 正反向幂等键 → 仓库 → 产品 → 库存余额键。集合按稳定身份排序；不存在的余额通过现有 UPDLOCK/HOLDLOCK 范围保护。仓库业务分配顺序不等于锁序。

反向请求指定原单据、期望版本、日期和原因。Prepare 从声明的原单据发现并锁定原流水，反向明细键由子命令与原流水确定性派生。先调用出库反向，再调用入库反向；全量验证原单与流水一一对应，禁止跨箱单、重复反向、替代事实或缺失历史。普通重试在原命令结果日志存在时返回原结果。

采购净收货历史校验已补充自动入库类型 12 的完整反向证明分支，仍要求完整原／反向配对，不把异常历史当作零收货。手工工作台的专属状态 SQL 未放开。

主要错误码：TRANSACTION_REQUIRED、RESOURCES_NOT_PREPARED、UNDECLARED_RESOURCE、COMMAND_BUSY、SCHEMA_NOT_READY、VALIDATION、UNAUTHENTICATED、COMMAND_CONFLICT、PURCHASE_NOT_RECEIVABLE、PURCHASE_CAPACITY_EXCEEDED、SUPPLIER_WAREHOUSE_INVALID、PRODUCT_UNIT_INVALID、WAREHOUSE_INVALID、DOCUMENT_STATE_INVALID、VERSION_CONFLICT、LEDGER_INTEGRITY、INVENTORY_BLOCKED、LOG_WRITE_FAILED、WRITE_FAILED。业务异常可带明细身份；取消保持 OperationCanceledException。

## 数据库和验证边界

- 本阶段没有迁移、建表或共享数据库执行。只读 schema 检查验证实体所需列、重复产品明细约束；若存在涉及 DocumentType／CreationMode 的额外 CHECK，则保守拒绝，需先人工核对其是否允许自动模式，不自动删除约束。
- 行依据记录没有借用原单字段，没有修改历史基线。真实数据库是否与当前实体生成基线兼容仍需在部署环境验证。
- 单元测试使用真实库存内核和内存仓储，覆盖事务编排、重放、容量和正常反向，但不证明 SQL Server 锁行为。
- SQL Server 集成测试默认跳过。需单独准备已部署当前实体基线的专用数据库，名字必须以 `SwiftExport_AutomaticPosting_Test_` 开头，并明确设置 `SWIFTEXPORT_RUN_AUTOMATIC_SQL_TESTS=1` 与 `SWIFTEXPORT_AUTOMATIC_TEST_CONNECTION` 后运行 `AutomaticPostingSqlServerTests`。
- 集成测试不创建／删除数据库，不自动部署基线；事务回滚测试不留业务数据，并发测试保留唯一测试身份的证据数据，不清空任何表。严禁配置共享或生产库。连接信息不得写入仓库或日志。
- 未启用独立测试库时，真实事务可见性、并发及部署兼容验证标为未执行，不宣称 PackingList 已能过账。
