# PackingList 实体与关联交付设计

> 文档归档于 Shipping。当前入口见 [Shipping 文档索引](../README.md)，接口现状见 [API 索引](PackingList接口索引.md)，后续方案见 [第三阶段计划](PackingList第三阶段实施计划.md)。本文保留历次设计与实施记录；较早的“未接线／SQL 未执行”等描述不代表当前状态，最新验收摘要以索引为准。

> 头部 ShippingMark 已移除，箱唛仅使用行 Marks。实体、头读写契约、UI 和仓储 SQL 已同步；数据库列由用户手动删除，本次不执行数据库操作。

## 2026-09-17 最新字段处置

PackingListHead 新增 ShippingLocationSnapshot（可空，200字符），头读写 DTO 同步。PackingListLine 删除 ShippingLocationSnapshot、WarehouseSiteId、WarehouseCodeSnapshot、WarehouseNameSnapshot；无库存仓库职责。

PackageCount 改为 Qty/PcsPerPackage 的四位小数结果快照，取消向上取整及人工独立指定；重量体积小计继续四位 AwayFromZero。保存入口接纳用户实际包装值并统一重算。尾箱拆分保持来源及总量，新行各单箱指标清空，用户填写后再实时计算。

手动迁移使用《PackingList头地点与行仓库清理.sql》，覆盖下文旧行地点方案；未执行。多人工作区的历史能力地图仍有旧仓库描述，以本次用户确认及源码为准，后续过账设计不得沿用行仓库字段。

## 2026-09-17 旧增量决策

新增行字段 ShippingLocationSnapshot（nvarchar(200)，可空）作为非权威出货地点文本，标准与非标都可填写，不影响自动入出库。旧 WarehouseSiteId、WarehouseCodeSnapshot、WarehouseNameSnapshot 只保留历史兼容，已从新写请求移除。迁移脚本见 SwiftExport.Infrastructure/Migrations/PackingList出货地点增量.sql，需单独审核部署，本次未执行。当前已存在前后端草稿链路；下文“仅实体”描述为历史阶段记录。最新选源口径见《PackingList前端契约与后端交接》的日期更新章节。

## 历史状态与边界

本轮仅清洗 Core 实体及契约测试。目标表 PackingListHead / PackingListLine **待迁移**；没有保存服务、DTO、API、WPF 或过账入口，不具备数据库运行时持久化能力。旧初始化和迁移脚本不改，现有 Uid 不重新生成。

## 字段字典

两实体继承 BaseEntity：Uid、Version、IsDeleted、CreatedDate/By、UpdatedDate/By；技术审计与业务日期独立。

### PackingListHead

| 字段 | 类型 | 职责 |
|---|---|---|
| PackingListNo | string | 装箱单业务编号。 |
| CustomerId | Guid | 最终客户逻辑标识。 |
| CustomerNameSnapshot | string? | 单据采用的客户名称快照，不随主数据更新。 |
| CustomerAddressSnapshot | string? | 单据采用的客户地址快照。 |
| AttentionSnapshot | string? | 自由填写的备注快照，不关联联系人实体。 |
| InvoiceDate | DateTime | 用户自定义单据日期，不作为库存记账日期。 |
| ContainerNo | string? | 集装箱号。 |
| SealNo | string? | 封条号。 |
| Note | string? | 单据备注。 |
| Etd | DateTime? | 预计离港日期。 |
| Eta | DateTime? | 预计到达日期。 |
| LifecycleStatus | PackingListLifecycleStatusEnum | 单据生命周期，与库存状态分别表达。 |
| PostingStatus | PackingListPostingStatusEnum | 库存过账状态，不表示已实现过账入口。 |
| PostingDate | DateTime? | 库存业务日期，未过账可空，与单据日期独立。 |
| PostedDate | DateTime? | 实际过账操作时间。 |
| PostedBy | string? | 过账操作人。 |
| ReversedDate | DateTime? | 反向操作时间。 |
| ReversedBy | string? | 反向操作人。 |
| ReverseReason | string? | 反向原因。 |
| CreateCommandId | Guid | 保留建单命令身份，具体命令校验另行实现。 |
| PostCommandId | Guid? | 过账命令身份，用于后续幂等处理。 |
| ReverseCommandId | Guid? | 反向命令身份，用于后续幂等处理。 |

### PackingListLine

| 字段 | 类型 | 职责 |
|---|---|---|
| PackingListHeadId | Guid | 所属装箱单头 Uid，逻辑关联。 |
| LineNumber | int | 单据内行号，同来源允许多行。 |
| GrossDemandId | Guid | 唯一交付来源锚点，不表示采购或物料分摊。 |
| ProductId | Guid | 本次交付的产品标识。 |
| SkuSnapshot | string | 产品编码快照。 |
| LinkSkuSnapshot | string? | 关联型号快照。 |
| CustomerSkuSnapshot | string? | 客户型号快照。 |
| DescriptionEnSnapshot | string? | 英文描述快照。 |
| ColorNameEnSnapshot | string? | 英文颜色名称快照。 |
| UnitSnapshot | string? | 库存及交付数量的计量单位快照。 |
| Qty | decimal | 本行计划交付数量，草稿不形成正式交付事实。 |
| PcsPerPackage | decimal? | 每包数量；未知为空，已知时须大于零。 |
| PackageCount | decimal? | 实际整包装数；默认建议向上取整，允许人工指定，未知为空。 |
| NwPerPackage | decimal? | 每包净重。未知为空。 |
| GwPerPackage | decimal? | 每包毛重。未知为空。 |
| CbmPerPackage | decimal? | 每包体积。未知为空。 |
| SubTotalNw | decimal? | 实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。 |
| SubTotalGw | decimal? | 实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。 |
| SubTotalCbm | decimal? | 实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。 |
| CustomerPoNo | string? | 客户采购编号文本快照，不作为关系主键。 |
| InvoiceNo | string? | 行上手工填写的发票号文本，不关联财务发票。 |
| Marks | string? | 本行箱唛。 |
| Note | string? | 本行备注。 |
| WarehouseSiteId | Guid? | 本次库存操作仓库，草稿允许未确定。 |
| WarehouseCodeSnapshot | string? | 采用的仓库编码快照。 |
| WarehouseNameSnapshot | string? | 采用的仓库名称快照。 |

## 旧新字段处置与历史数据

| 原字段/类型 | 处理 |
|---|---|
| PackingListDocument / PackingListContributionLine | 改为 PackingListHead / PackingListLine，目标表同名，旧物理表不改 |
| PackingListDocumentId / Quantity | 改为 PackingListHeadId / Qty，迁移保留头行 Uid |
| DeliveryAddressSnapshot | 改为 CustomerAddressSnapshot |
| DescriptionSnapshot | 目标为 DescriptionEnSnapshot；历史文本必须证明英文语义后才回填 |
| CustomerCodeSnapshot | 新模型不保存；旧数据保留，不执行删列 |
| PackingDate、ShipmentDate | 新模型删除；InvoiceDate / PostingDate 不可未经确认直接替换历史日期 |
| 头 InvoiceNo | 退役，不能猜测分摊给每一行 |
| CreateRequestFingerprint、PostPreviewToken、ReversePreviewToken | 删除模型字段，后续命令校验重新设计 |
| NetWeight、GrossWeight、Volume | 语义不明确，不自动认作单包值或小计 |
| PackageCount | 保留；历史小数包装不可静默取整 |
| NetDemandId、NetDemandAllocationId、DemandExecutionLinkId | 不再保存；通过 GrossDemandId 查询，历史关系先保留 |
| SalesOrderId、SalesOrderLineId、SalesOrderNoSnapshot、SalesOrderLineNumberSnapshot | 新模型删除，通过来源需求追溯；旧快照数据不删除 |
| SourceSupplierId、SourceSupplierNameSnapshot、SupplyType、SnapshotRequiredDate | 不属于本轮交付行权威，删除模型字段 |
| SourceDemandVersionSnapshot、SourceVersionSnapshot、CommandItemId | 旧协议退役，后续提交重验方案另行设计 |
| 其他同名字段 | 保留；WarehouseSiteId 改为可空，包装量使用 decimal? |

## 单据计算与快照

InvoiceNo 由行手工填写，与销售或财务发票无关联。未来 InvoiceNosSummary 仅进入 Query/Export DTO：排除软删除行，按 LineNumber（相同时 Uid）排序，Trim 后跳过空值，按区分大小写的文本首次出现去重，以 `, ` 连接。不得独立编辑或持久化头汇总。

Qty 为交付单位数量；PcsPerPackage 为同单位每包数量，已知须大于零。PackageCount 使用 decimal?，已知为非负整数；默认建议 Ceiling(Qty/PcsPerPackage)，允许填写实际包装数。三个小计 = PackageCount × 对应每包指标，四位小数 AwayFromZero。任一因子未知，小计为空；未知不当零。重量和体积单位在后续保存/输出入口统一确认，不在本轮加入隐式单位换算。

未来保存服务统一重算小计，本轮不在实体 setter 中增加计算或验证。快照采用时固定，不随主数据更新；InvoiceDate 是可修改的单据日期，不改变 PostingDate 或库存历史。单据状态修改准入留给后续命令。

## 关系、基数与数量口径

| 关系 | 基数与责任 |
|---|---|
| Head → Customer | 多对一；未来 Application 校验所有来源属于同一最终客户 |
| Head → Line | 一对多；有效头行行号唯一，逻辑外键 |
| Line → GrossDemand | 多对一，同一来源可按包装和仓库拆行；唯一交付来源锚点 |
| Line → Product / WarehouseSite | 多对一；产品与来源一致，仓库草稿可空，正式动作须确认 |
| NetDemand → GrossDemand → Line | 查询来源交付，不直接减少物料需求数量 |
| DemandExecutionLink → NetDemand → GrossDemand | 采购跟踪去重来源后查询交付，不按 Link 重复汇总 |

未来已交付权威是按 GrossDemandId 汇总的有效正式最终客户出库净额；草稿、客供耗用不计入，反向恢复。不能用 PackingList 行数量替代已过账事实。

采购与物料展示“关联来源已交付／待交付”，不显示为采购自身已出货量。不同产品、单位不得合并；同一来源在不同采购均可展示关联进度，但跨采购汇总必须先去重 GrossDemandId。

## 当前偏差与后续任务

PL-ENTITY-01：实体目标与旧数据库表列不一致，禁止直接启用泛型仓储写入。当前 WarehouseSiteRepository 的历史引用保护仍查询 PackingListContributionLine，不提前改表名。

PL-QUERY-01：工作区中旧采购 Workspace 和 SourceDeliveryRepository/Sql 已被其他改动移除；本轮不恢复、不改变采购业务口径。后续新采购跟踪需要实现上述关联来源查询，不能认为现在已有实时出货投影。

旧 PL-001～005 的源码入口已清理，不再是可运行事实；未实现的库存、修订、输出能力仍未完成。本设计覆盖本轮实体和首版关联口径，与旧地图矛盾处以本设计为准。

DTO 分类：本轮无 DTO；以后分 Draft Command、Search/Detail Query、Trace/Export Query，InvoiceNosSummary 为派生 Query 字段，不向实体增加 UI 状态。

Repository/DI/API：本轮均不新建。实施顺序为只读数据库核查与单独授权迁移 → 草稿持久化 → 来源交付查询/采购投影 → 预览和正式过账 → 输出工作台。

## 数据库迁移清单（不执行）

1. 只读确认真实表、列、Uid、行数及历史 PackingListHead/Line 同名表是否存在；同名冲突禁止覆盖。
2. 建立旧新字段映射及无法证明的日期、英文、包装、发票快照清单；未知保持待核实，不猜值。
3. 保持 Uid 及库存单据中来源标识，迁移前后核对孤立头行、GrossDemand、Product、Warehouse 关系。
4. 建议活动单号唯一、有效头行行号唯一、GrossDemandId 非唯一索引及客户/日期查询索引；不增加来源唯一约束，不创建强外键。
5. 数量建议 decimal(18,4)，整数包装约束、字符串长度、历史异常及状态默认值需按数据 preflight 冻结后生成新脚本；不修改已发布脚本。
6. 同步仓库引用保护及未来读写 SQL 后再开放持久化。历史快照列的归档/删除必须另行授权。

## 自动库存边界（待实现）

使用本行仓库、产品和 GrossDemandId 生成正式单据，不强制采购行。非采购关联入库不得计入采购收货；不能因账面不足虚构实收。公共库存使用、实际收货确认、补入库数量、幂等、固定锁序、反向与原子事务留到过账阶段。客供件自动耗用不在本轮。

## 验收案例

- 同一来源 100 件拆成不同仓库/包装的 60、40 两行，不能因此重复来源额度。
- 两条 Link 指向同一来源，已交付 50/100：采购展示一次来源 50/50，不显示两份采购自身已出货 50。
- 10 套成品关联 40 个组件：组件只展示来源交付进度，不把成品 10 直接从组件 40 扣减。
- 发票行号顺序为 A、空白、A、B，汇总 A, B；删除 B 行后仅 A。
- Qty=25、PcsPerPackage=12，建议 PackageCount=3；人工指定 4、每包净重 2.5，小计 10.0000；未知单包指标仍为空。
- 草稿零交付影响；修改 InvoiceDate 不改 PostingDate；目标实体映射不代表数据库迁移完成。
