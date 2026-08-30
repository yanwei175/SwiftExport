# 单据状态机与轻量改单机制

本文定义 SwiftExport ERP 的单据状态、业务命令、改单边界和过账边界。它补充 [ERP 业务总纲](erp-business-blueprint.md)，用于后续实现销售订单、毛需求、净需求、采购、入库、出库和 PackingList 链路时判断“能不能改、能不能删、能不能回退”。

本项目是六七人使用的内部 ERP，不采用复杂审批式改单机制。默认策略是：库存流水之前允许安全回退并重跑流程；库存流水之后必须反过账或红冲。

## 1. 核心原则

1. 不把所有流程塞进 `OrderStatus`。
2. `OrderStatus` 只做列表展示和进度汇总，不作为复杂业务命令的唯一判断依据。
3. 确认、拆解、转采购、入库过账、出库过账、PackingList 过账、反过账、红冲都是业务命令。
4. 未确认、未过账、无库存流水、无后续引用的下游，可以删除或回退。
5. 已确认但未产生库存流水的下游，可以先取消确认，再删除或回退。
6. 已产生库存流水的下游，不能删除重跑，只能反过账或红冲。
7. 库存流水只新增，不修改、不删除。
8. `DemandExecutionLink` 应成为净需求到采购、生产、外协、入库、出库的主追溯表。

一句话规则：

```text
库存流水之前允许回退重跑；库存流水之后必须反过账或红冲。
```

## 2. 状态分层

### 2.1 单据生命周期状态

建议新增 `DocumentLifecycleStatus`，回答“单据是否还有效”。

| 状态 | 含义 | 典型单据 |
| --- | --- | --- |
| 草稿 | 可编辑，尚未进入正式业务链路 | 销售订单、采购单、入库单、出库单、PackingList |
| 生效 | 已确认或已被下游读取 | 销售订单、毛需求、采购单 |
| 关闭 | 正常完成或人工终止 | 销售订单、采购单、需求 |
| 作废 | 单据无效，不参与后续执行 | 所有单据 |

生命周期状态不表达是否入库、是否出库、是否过账。

### 2.2 确认状态

当前已有 `ConfirmationStatusEnum`，继续保留。

| 状态 | 含义 |
| --- | --- |
| 待确认 | 未成为正式业务输入 |
| 已确认 | 允许下游读取和执行 |

确认不应自动进入所有下游。销售订单确认自动生成毛需求，是销售订单的特殊规则，不推广到所有单据。

### 2.3 执行进度状态

建议保留或扩展 `OrderStatus` 做展示，也可新增 `ExecutionProgressStatus`。

| 状态 | 含义 |
| --- | --- |
| 未开始 | 未生成下游 |
| 已生成需求 | 销售订单已生成毛需求 |
| 已拆解 | 毛需求已生成净需求 |
| 已转执行 | 净需求已转采购/生产/外协 |
| 部分入库 | 已部分入库 |
| 已入库 | 已全部入库 |
| 部分出库 | 已部分出库 |
| 已出库 | 已全部出库 |
| 已完成 | 需求和库存闭环完成 |

执行进度可由 `DemandExecutionLink`、采购行、入库行、出库行、PackingList 行和库存流水汇总得到。

### 2.4 过账状态

建议新增 `PostingStatus`，用于入库单、出库单、PackingList。

| 状态 | 含义 |
| --- | --- |
| 未过账 | 未影响库存 |
| 已过账 | 已生成库存流水 |
| 已反过账 | 已用反向流水撤回 |
| 已红冲 | 已通过红字单据抵消 |

当前 `ReceiveHead.IsCompleted`、`PackingListHead.IsPosted`、`OutboundHead.IsPosted` 可以保留兼容，但后续业务判断应逐步迁移到 `PostingStatus`。

## 3. 命令触发规则

| 命令 | 是否进入下游 | 规则 |
| --- | --- | --- |
| 保存 | 否 | 只保存草稿，不生成需求，不影响库存 |
| 确认销售订单 | 是 | 自动按销售订单行一对一生成毛需求 |
| 取消确认销售订单 | 视下游而定 | 只有下游未执行或可回退时允许 |
| 确认毛需求 | 否 | 只表示允许拆解 |
| 拆解 | 是 | 显式生成净需求 |
| 转采购 | 是 | 显式生成采购单、采购行和执行追溯 |
| 入库过账 | 是 | 生成入库单过账结果、库存流水，回写采购和需求 |
| 出库过账 | 是 | 生成出库单过账结果、库存流水，回写需求 |
| PackingList 过账 | 是 | 自动入库/出库/客供件扣减，但必须生成正式单据和库存流水 |
| 改单 | 否 | 先检查下游，再决定回退重跑、反过账或红冲 |
| 反过账 | 是 | 生成反向库存流水，回退相关数量 |
| 红冲 | 是 | 保留原单，生成红字单据和红字流水 |
| 关闭 | 否 | 停止后续执行 |
| 作废 | 否 | 仅无不可逆下游时允许 |

## 4. 销售订单改单主流程

销售订单是主链路上游，改单时优先按下游深度判断。

```text
销售订单需要改单
  -> 检查下游进度
  -> 没有库存流水：
       按顺序回退下游
       删除或取消未过账下游
       取消确认销售订单
       修改销售订单
       重新确认、拆解、转采购、入库或出库
  -> 已有库存流水：
       禁止直接改单
       能撤回则反过账
       不能撤回则红冲或补差异单
```

## 5. 下游深度与改单策略

| 下游进度 | 允许操作 | 处理方式 |
| --- | --- | --- |
| 只有销售订单草稿 | 直接改 | 保存即可 |
| 已确认并生成毛需求，毛需求未确认 | 允许回退 | 删除毛需求，取消确认销售订单，修改后重新确认 |
| 毛需求已确认但未拆解 | 允许回退 | 取消毛需求确认，删除毛需求，回到销售订单 |
| 已拆解为净需求，净需求未转采购 | 允许回退 | 删除净需求，回退毛需求拆解标记，再改销售订单 |
| 已转采购，采购单未确认 | 允许回退 | 删除采购单/行，释放 `DemandExecutionLink` 和净需求执行数量 |
| 采购单已确认但未入库 | 允许谨慎回退 | 先取消确认采购单，再删除采购行/单，释放净需求 |
| 已生成入库单但未过账 | 允许回退 | 删除入库单/行，再回退采购和需求 |
| 入库已过账但未被出库消耗 | 先反过账 | 生成反向库存流水，再允许回退上游 |
| 已出库或 PackingList 已过账 | 不允许直接回退 | 走红冲、补差异单或补货流程 |

## 6. 取消确认边界

### 6.1 销售订单

允许取消确认：

- 毛需求不存在。
- 毛需求存在但未确认、未拆解。
- 毛需求和净需求均可完整删除，且没有采购、入库、出库、PackingList、库存流水。

拒绝取消确认：

- 毛需求已拆解且净需求已转采购。
- 采购单已确认且不能取消。
- 任意入库/出库/PackingList 已过账。
- 任意相关库存流水存在。

### 6.2 毛需求

允许取消确认：

- 未拆解。
- 无净需求。
- 无执行追溯。

拒绝取消确认：

- 已拆解。
- 已生成净需求。
- 已生成采购或其他执行单据。

### 6.3 采购单

允许取消确认：

- 未入库。
- 无入库单行引用。
- 无库存流水。

拒绝取消确认：

- 已入库过账。
- 已被出库或 PackingList 引用。
- 已有关联库存流水。

## 7. 删除、反过账、红冲边界

### 7.1 可以删除的对象

只允许删除未确认、未过账、无库存流水、无后续引用的数据。

| 对象 | 可删除条件 |
| --- | --- |
| 毛需求 | 未确认或已取消确认，未拆解 |
| 净需求 | 未转采购，无执行链接 |
| 采购单/行 | 未确认，或已取消确认，未入库 |
| 入库单/行 | 未过账 |
| 出库单/行 | 未过账 |
| PackingList | 未过账 |

### 7.2 必须反过账的对象

已过账但仍可撤回的业务，走反过账。

适用场景：

- 入库已过账，但库存未被出库消耗。
- 出库已过账，但未被 PackingList 或后续对账锁定。
- PackingList 已过账，但业务允许整体撤回且没有对外锁定。

反过账必须：

- 生成反向库存流水。
- 回退入库、出库、采购、需求中的累计数量。
- 标记原单 `PostingStatus = 已反过账`。
- 保留原单和原库存流水。

### 7.3 必须红冲的对象

原业务不能撤回但需要修正，走红冲。

适用场景：

- 已跨期。
- 已对账。
- 已对客户出货。
- 已被后续单据引用，无法整体反过账。
- 业务上需要保留原始历史。

红冲必须：

- 保留原单。
- 生成红字单据或反向业务单据。
- 生成红字库存流水。
- 记录原单与红冲单关系。

## 8. 实体字段建议

### 8.1 单据头通用字段

建议逐步给核心单据头补充：

```csharp
DocumentLifecycleStatus LifecycleStatus
ExecutionProgressStatus ProgressStatus
string? CloseReason
string? VoidReason
DateTime? ClosedDate
DateTime? VoidedDate
string? ClosedBy
string? VoidedBy
```

### 8.2 过账单据字段

入库单、出库单、PackingList 建议补充：

```csharp
PostingStatus PostingStatus
string? PostingBatchNo
DateTime? PostedDate
string? PostedBy
DateTime? ReversedDate
string? ReversedBy
string? ReverseReason
string? ReversalBatchNo
Guid? ReversedFromUid
bool IsSystemGenerated
```

### 8.3 `DemandExecutionLink`

建议作为主追溯表增强：

```csharp
Guid IndependentDemandId
Guid SourceHeadId
Guid SourceLineId
string SourceType
string SourceNo
string TargetType
Guid? TargetHeadId
Guid? TargetLineId
string? TargetNo
decimal RequiredQty
decimal ExecutedQty
decimal PostedInQty
decimal PostedOutQty
ExecutionProgressStatus ProgressStatus
PostingStatus PostingStatus
bool IsClosed
```

`DependentDemand.TargetNo` 和 `TargetLineId` 可以保留为快速入口，但权威关系应逐步迁移到 `DemandExecutionLink`。

### 8.4 `InventoryTransaction`

建议补充：

```csharp
string PostingBatchNo
string SourceDocType
Guid? SourceHeadId
Guid? SourceLineId
string SourceNo
string Direction
Guid? ReverseOfTransactionId
bool IsReversal
bool IsRed
string? Reason
```

## 9. Application Service 建议

不需要复杂审批式改单服务，但需要三个轻量服务边界。

### 9.1 `DocumentRollbackPolicyService`

负责判断某单据是否可以取消确认、删除下游、回退到上游。

返回：

```text
CanRollback
BlockingReason
BlockingDocuments
SuggestedAction
```

### 9.2 `SalesOrderChangeCheckService`

负责销售订单改单前检查下游。

返回：

```text
CanEditDirectly
NeedRollbackDocuments
HasPostedInventory
BlockingReason
SuggestedAction
```

### 9.3 `PostingService`

负责所有会影响库存的过账动作：

- 入库过账
- 出库过账
- PackingList 过账
- 反过账
- 红冲
- 写库存流水
- 回写需求、采购和销售进度

`ReceiveService`、`PackingListService` 可以保留为入口，但库存变动规则应逐步收敛到 `PostingService`。

## 10. API / DTO 建议

### 10.1 改单检查

```text
POST /api/sales-orders/{id}/change-check
```

返回：

```text
CanEditDirectly
NeedRollbackDocuments[]
HasPostedInventory
BlockingReason
SuggestedAction
```

### 10.2 回退下游

```text
POST /api/sales-orders/{id}/rollback-downstream
POST /api/purchase-orders/{id}/cancel-confirm
POST /api/receives/{id}/reverse
POST /api/outbounds/{id}/reverse
POST /api/packing-lists/{id}/reverse
```

### 10.3 过账

```text
POST /api/receives/{id}/post
POST /api/outbounds/{id}/post
POST /api/packing-lists/{id}/post
```

### 10.4 链路查询

```text
GET /api/document-flow/by-sales-order/{salesOrderId}
GET /api/demand-links/by-dependent-demand/{dependentDemandId}
```

用于前端显示改单前的阻塞点。

## 11. 测试场景

### 11.1 销售订单确认

- 未确认销售订单确认后生成一对一毛需求。
- 重复确认被拒绝。
- 无明细行确认被拒绝。

### 11.2 销售订单取消确认

- 只有未确认毛需求时，允许取消确认并删除毛需求。
- 毛需求已拆解但未转采购时，必须先回退拆解。
- 已转采购时，必须先删除或取消采购下游。
- 已入库过账时，禁止直接取消确认。

### 11.3 改单重跑

- 销售订单数量增加，未产生库存流水时，允许回退后重跑。
- 销售订单数量减少，采购未入库时，允许回退采购后重跑。
- 销售订单 SKU 改变，未过账时按旧行删除、新行新增处理。

### 11.4 入库与库存流水

- 未过账入库单允许修改和删除。
- 入库过账后生成库存流水。
- 入库过账后禁止普通保存修改。
- 入库未被出库消耗时允许反过账。

### 11.5 PackingList

- 未过账 PackingList 允许修改。
- PackingList 过账后禁止直接修改。
- PackingList 过账必须生成正式入库/出库单据和库存流水。
- PackingList 已过账后改单只能走反过账、红冲或补差异。

## 12. 当前知识体系还缺什么

当前已经有：

- 业务总纲：`wiki/project/erp-business-blueprint.md`
- 架构总纲：`wiki/project/project-map.md`
- 单据状态与改单机制：本文
- 后端、前端、Contracts、Dapper、WPF 等开发 guide / skill

建议后续补齐的总纲级文档：

1. 数据模型总纲：说明核心表、主外键、业务唯一键、软删除、审计字段、库存流水不可变原则。
2. 领域模块总纲：按产品、客户、供应商、销售、需求、采购、仓库、PackingList、投诉补货拆模块边界。
3. API 契约总纲：统一 Controller / Refit / DTO 命名、路由、命令接口和查询接口风格。
4. 前端页面总纲：定义 WPF 模块导航、工作台页面形态、列表/编辑/弹窗/批量保存模式。
5. 测试与验收总纲：定义每个业务闭环最少要跑哪些手工验收场景。
6. 数据迁移与种子总纲：定义 SQL 脚本、迁移、基础枚举、测试数据和本地数据库初始化方式。
