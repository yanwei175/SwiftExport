# SwiftExport PackingList 工作台冻结版工程任务书

## 1. Codex 新任务启动提示词

> 请先完整读取本任务书和同目录《库存履约下游专项总索引》《供应商虚拟仓与库存冻结版工程任务书》《入库工作台冻结版工程任务书》《出库工作台冻结版工程任务书》，再读取净需求、采购、日志任务书、根目录 `AGENTS.md`、路由、workflow/skill 和真实源码。旧 `PackingList` C# Entity、DTO、API、Service、Repository 已清理，不得从旧项目直接恢复；新专用模型优先映射现有 `PackingListHead/Line` 物理表，旧源码只可用于核对展示字段。先只读审计真实表结构和销售/需求链路，只为缺失事实增加字段。默认不执行共享数据库迁移。

## 2. 业务目标

PackingList 是最终客户装柜/发货计划和库存履约入口。它不是简单打印表：

- 草稿阶段组织本次向同一最终客户发出的产品、销售来源和数量。
- 过账预览计算哪些成品需要自动补入库、哪些成品要最终客户出库、哪些客供件要耗用。
- “提交并过账”在一个事务中生成正式入库、正式出库、库存流水和业务操作日志。
- 过账完成后，最终客户已发货数量以正式出库事实为准。

## 3. 已冻结业务规则

- 一个 PackingList 只对应一个最终客户，但允许包含多个销售订单、多个毛需求来源和多个供应商执行仓。
- 来源必须精确到销售订单行/毛需求/净需求 Allocation 贡献，不能只保存 SKU 和销售单号文本。
- 保存/编辑草稿不改变库存，不增加“已发货”。
- 用户执行“提交并过账”前必须先 Preview；修改行、贡献、数量、日期、客户或装箱模式后 Preview 失效。
- 对本次成品发货贡献，已手工入库的部分直接出库；尚未入库但本次确定装柜的缺口由系统自动生成正式入库，再全量出库。
- 自动入库只补该执行来源的未入库缺口，不依据同仓同 SKU 的全局余额判断，也不能挪用无关订单来源。
- 客供件耗用按毛需求拆解进入净需求时保存的历史快照比例计算，不能读取当前 BOM 改写历史。
- 客供件从父件执行供应商虚拟仓扣减。
- 库存不足时默认阻断。确需不耽误出货，用户在失败结果上发起独立负库存异常授权，生成 90 类型流水。
- PackingList 一旦过账，普通字段和数量不可编辑；纠错必须整体反过账后另建/修改草稿。
- `InvoiceNo`、柜号、箱唛等是展示/物流资料，不是幂等键或来源关系。
- 按销售订单号必须能找到 PackingList、采购单、入库、出库及逐来源数量。

## 4. 状态机

使用分层状态：

- `LifecycleStatus`：`Active=0, Closed=8, Voided=9`。
- `PostingStatus`：`Unposted=0, Posted=1, Reversed=8`。
- 草稿 = Active + Unposted。
- 已发货 = Active/Closed + Posted。

允许流转：

```text
草稿 -> 提交并过账 -> 已过账 -> 反过账 -> 已反向
草稿 -> 作废
```

禁止：已过账直接编辑、已反向重新过账同一单据、删除任何已过账/已反向单据。

## 5. 来源选择与数量

- 来源选择只显示仍可发货的 GrossDemand/NetDemand/Allocation 贡献。
- 本次 PackingQty 必须满足 `0 < Qty <= ShippableRemainingQty`。
- `ShippableRemainingQty` 以该需求贡献的需求量减去已过账最终客户出库净额计算；采购已备好数量仅提示，不构成硬上限。
- 同一销售来源可以分多次 PackingList；同一个需求贡献在一个草稿中只能出现一次，修改数量而不是重复加行。
- 同 SKU 不同销售行在 `PackingListLine` 中保持为不同物理行。UI/打印可以按产品视觉汇总，但持久化关系不得丢失。
- 计划交期、采购交期不决定是否允许装柜；若早于需求交期或跨不同交期合并，可显示提示但不阻断。

## 6. 过账预览算法

PreviewBuilder 必须是无副作用纯计算，输入为服务端重新读取的规范化事实：

1. 锁前读取草稿头行和全部 DemandContribution。
2. 校验客户、来源、数量、Allocation、执行供应商和虚拟仓。
3. 按贡献计算历史已入库净额与最终客户已发货净额。
4. `AutoInboundQty = max(PackingQty - SourceAvailableInboundQty, 0)`；这里的 SourceAvailableInboundQty 只取同一执行来源尚未被发货消耗的入库净额。
5. `FinalOutboundQty = PackingQty`。
6. 根据拆解快照计算每个 B 客供件本次理论耗用，扣除同一来源历史耗用并处理最后一批尾差。
7. 按供应商虚拟仓、产品形成库存变动预览，列出 Before、AutoInbound、Outbound/Consumption、After。
8. 输出 Blockers、Warnings、必须确认的警告代码和 PreviewToken。

PreviewToken 是规范化头/行 Version、贡献、来源剩余量、仓库、库存 Version、自动入库、出库和耗用结果的 SHA-256 摘要。Execute 仍必须加锁重读并重新计算。

## 7. 提交并过账事务

固定锁序：

1. PackingListDocument 头行。
2. PackingListLine（每行即一个需求贡献），按 Uid 排序。
3. GrossDemand、NetDemand、Allocation、DemandExecutionLink，按 Uid 排序。
4. 历史 Inbound/Outbound 来源贡献。
5. WarehouseSite。
6. InventoryBalance，按 WarehouseId/ProductId 排序。
7. Command/CommandItem 幂等范围。

同一事务内：

1. 校验 PreviewToken、Version、剩余量和已确认警告。
2. 为缺口创建 `PackingListAutoReceipt` 入库头行并调用 PostingService 增库存。
3. 创建 `FinalCustomerShipment` 出库头行并扣成品。
4. 创建 `CustomerSuppliedConsumption` 出库头行并扣客供件。
5. 更新 PackingList PostingStatus、PostedDate/By。
6. 写 BusinessOperationLog。
7. 一次提交。

任一步失败必须全部回滚。Application 内部直接调用入库/出库领域端口，禁止通过 HTTP 自调用。

## 8. 目标数据模型与现有表映射

- `PackingListDocument` 映射 `dbo.PackingListHead`。
- 每个 `PackingListContributionLine` 映射一条 `dbo.PackingListLine`；一条物理行同时承载产品展示快照和一个精确需求贡献。
- 复用现有 PackingListNo、客户/地址、InvoiceNo、ContainerNo、SealNo、ETD/ETA、PackingDate、IsPosted、SKU、Qty、客户 SKU、英文描述、包装、重量、体积、SalesOrderNo、SourceType/No/LineId 等列。
- 只追加现有表缺失的 Uid、分层状态、Allocation/Link、Warehouse、Command 和反向字段，不新建同义头行表或 `PackingListDemandContribution` 物理表。
- 旧初始脚本曾出现整型 HeadId，而后续 C# 使用 Uid；实施前必须以真实数据库 preflight 为准，不能凭旧脚本直接修改类型。

### 8.1 PackingListDocument

| 字段 | 说明 |
|---|---|
| Uid / Version / 审计字段 | 稳定身份、并发、审计 |
| PackingListNo | 系统/人工可读编号，唯一 |
| CustomerId / CustomerCode/NameSnapshot | 单一最终客户 |
| ShipmentDate | 计划/实际业务发货日期 |
| InvoiceNo | 展示字段，可空，不作业务键 |
| ContainerNo / SealNo / ShippingMark | 物流展示字段，可空 |
| LifecycleStatus / PostingStatus | 分层状态 |
| PostedDate / PostedBy | 过账事实 |
| ReversedDate / ReversedBy / ReverseReason | 反过账事实 |
| CommandId | 创建/过账幂等范围 |

### 8.2 PackingListContributionLine（物理表 PackingListLine）

物理行既是页面/打印的基础产品行，也是一个精确来源贡献：

| 字段 | 说明 |
|---|---|
| Uid / PackingListId / LineNumber | 稳定行身份 |
| ProductId / Sku/Description/UnitSnapshot | 产品关系与快照 |
| Quantity | 该单一需求贡献的本次数量 |
| PackageCount / NetWeight / GrossWeight / Volume | 物流展示数据，可空 |
| Marks / Note | 展示备注 |
| Version / 审计字段 | 并发与审计 |

### 8.3 同一 PackingListLine 需要补齐的来源字段

| 字段 | 说明 |
|---|---|
| Uid / PackingListHeadId | 行身份和所属头 |
| GrossDemandId / NetDemandId / AllocationId | 精确需求来源 |
| DemandExecutionLinkId | 已存在采购执行时关联 |
| SalesOrderId / SalesOrderLineId | 可证明时保存逻辑关系 |
| SalesOrderNo/LineNumberSnapshot | 人工检索快照 |
| SourceSupplierId / WarehouseSiteId | 成品执行供应商与仓 |
| Quantity | 本次贡献数量 |
| SnapshotRequiredDate / SupplyType | 历史快照 |
| CommandItemId | 唯一幂等键 |
| Version / 审计字段 | 并发与审计 |

每条 PackingListLine 只能有一个 Allocation 来源且必须可证明。页面产品汇总数量由相同 ProductId 的物理行求和，不能出现只有展示数据没有需求来源的业务行。

### 8.4 客供件耗用快照

优先使用现有同一 PlanRevision 下 A/B `NetDemandAllocation` 的数量和关联关系还原拆解比例，不新建快照表。如果真实字段仍不足，只在 Allocation 增加父基准量、单位耗用比例或舍入规则等最小快照字段。不得在 PackingList 时读取当前 BOM 代替。

## 9. 反过账

- Reverse Preview 展示自动入库、最终出库、客供件耗用和预计库存结果。
- 反向执行固定顺序：反向所有出库流水 -> 反向自动入库流水 -> PackingList 标记 Reversed。
- 若自动入库形成的库存已经被其他后续出库使用，反向后会产生负库存则阻断；首期不允许用 90 类型强行反过账。
- 反过账必须携带 CommandId、ExpectedVersion 和原因，并在同一事务写业务日志。
- 反向后需求可发货剩余量自然由有效出库净额恢复，不在需求表写冗余 ShippedQty。

## 10. Repository 与查询

建议：

- `IPackingListDocumentRepository`：现有 PackingList 头行读写、ForUpdate、过账/反向状态。
- `IPackingListPlanningRepository`：来源搜索、锁需求事实、读取入库/出库/耗用净额和拆解快照。
- `IPackingListQueryRepository`：Search、Detail、Trace、打印/导出模型。

所有查询显式列清单。遗留 `PackingListHead/Line` 行只读；同一物理表中的新行由新专用仓储写入，并依靠新增状态/来源字段区分。只有无法补字段且有明确技术证据时才允许另提新表方案，必须重新取得用户确认。

遗留 PackingList 如果只有 SKU/单号而没有可证明需求来源，只读展示并标记“遗留不可过账/反向”。

## 11. HTTP/Refit 契约

基址 `/api/shipping/packing-lists`：

- `POST /search`
- `GET /{uid}`
- `POST /sources/search`
- `POST /drafts`
- `PUT /drafts/{uid}`
- `POST /{uid}/post-preview`
- `POST /{uid}/post`
- `POST /{uid}/reverse-preview`
- `POST /{uid}/reverse`
- `GET /{uid}/trace`
- `GET /{uid}/export`

Create/Update DTO 必须提交逐 Contribution 的 Uid、来源 Uid、ExpectedRemainingQty 和 ExpectedVersion。Post/Reverse 使用 CommandId、ExpectedVersion、PreviewToken 和 ConfirmedWarningCodes。

## 12. WPF 工作台

Shell 一级导航“出货”下新增“PackingList 工作台”，替换旧占位页面：

- 服务端分页；按 PackingListNo、InvoiceNo、客户、销售订单号、供应商、SKU、状态和日期筛选。
- 草稿编辑采用头部客户/物流资料 + EnterpriseGrid 产品行 + 可展开来源贡献。
- “选择来源”弹窗从需求贡献加入，支持多销售订单但强制同一客户。
- 顶部命令：新建、编辑草稿、过账预览、提交并过账、负库存异常处理、反过账、Trace、导出/打印。
- Preview 分区展示自动入库、最终出库、客供件耗用、库存不足和强警告；Blocker 存在时禁用提交。
- 修改任何影响结果的字段后清空 PreviewToken 并要求重新预览。
- 提交 Busy 防重入；逐来源结果明确 Success、AlreadyApplied、VersionConflict 或业务阻断。
- Trace 以销售订单 -> 毛需求 -> 净需求/Allocation -> 采购 -> 入库 -> PackingList -> 出库展开。

## 13. 分阶段实施

### Phase 0：只读审计与原型计算

- 审计旧 PackingList 展示字段、当前需求 Trace、入/出库内核和拆解快照是否足够。
- 先完成纯计算 PreviewBuilder 测试，不写数据库。

### Phase 1：Contracts、现有表扩展与草稿

- 新增专用实体、DTO、API、草稿仓储、来源搜索，并映射/最小扩展 `PackingListHead/Line`。

### Phase 2：过账编排

- 自动入库、最终出库、客供件耗用、幂等、并发和全事务回滚。

### Phase 3：反过账与 WPF

- 完成 Reverse、工作台、Trace、XLSX/打印模型。

## 14. 测试与验收

至少覆盖：

- 单/多销售订单同客户成功，不同客户阻断。
- 同 SKU 多销售行保持独立物理行；页面/导出汇总正确且来源不丢失。
- 草稿不改变库存/已发货；过账后正式事实准确。
- 已入库成品直接出库；未入库缺口自动入库后出库。
- 多供应商仓、多 SKU 和客供件耗用在同一事务全成全败。
- 按历史拆解快照计算，修改当前 BOM 不影响旧订单。
- 部分发货、分批发货、最后一批舍入尾差和累计数量守恒。
- Preview 过期、Version 并发、重复提交幂等和 Command 内容冲突。
- 正常负库存阻断、特殊授权放行及 90 类型流水。
- 反过账恢复库存和可发货剩余量，存在后续使用时阻断。
- 按销售订单号完整 Trace 和 XLSX 导出。

## 15. 明确不做

- 船公司订舱、报关、提单、物流轨迹和财务发票。
- 自动集装箱装载优化；采购已备好数量只为未来计划提供数据。
- 批次/序列号、扫码和移动端装柜。
- 用 PackingList 草稿或展示字段直接作为库存/已发货权威。
