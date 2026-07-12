# SwiftExport ERP 数据模型治理源码审查报告

审查日期：2026-07-11  
审查性质：只读源码治理审查；未修改 Entity、DTO、SQL、数据库脚本、Repository 或业务代码。  
治理口径：`AGENTS.md`、`.agents/router.toml`、`planner`、数据模型治理工作流及 `aspdotnet10-data-model-governance` Skill。源码事实优先于文档。

## 0. 结论摘要

1. **主链尚未形成可执行闭环。** 产品和销售订单查询相对完整；毛需求分页/拆解、净需求分页/转采购、采购保存、入库保存与过账、PackingList 保存与过账存在大量 `NotImplementedException` 或被整体注释的实现。证据见 `SwiftExport.Application/Services/Orders/*Service.cs` 与 `SwiftExport.Infrastructure/Repositories/Orders/DependentDemandRepository.cs`。
2. **当前最大的模型风险不是“字段太多”，而是关系主来源不清。** `DependentDemand.TargetNo/TargetLineId`、`PurchaseOrderLine.SourceType/SourceNo` 与 `DemandExecutionLink` 同时表达净需求到执行单的关系，但后者尚无业务调用方；数量又分别保存在 `DependentDemand`、`DemandExecutionLink`、`PurchaseOrderLine`，没有已启用的事务性汇总规则。
3. **历史快照必须保留。** 销售行的客户料号、描述、颜色、规格、价格、包装；采购头的供应商名称和采购行的物料/供应商料号/价格；PackingList 的描述、包装、重量、体积；库存流水的单号、行号、价格、金额、变动后余额均不能因“可联表”直接删除。
4. **DTO 大量为混合模型。** `ProductDto`、`IndependentDemandDto`、`DependentDemandDto`、`PurchaseOrderHeadDto`、`PurchaseOrderLineDto`、`PackingListLineDto` 同时承担保存、列表、联表展示或命令输入，导致 Mapster 默认映射可能把查询字段写回 Entity，或遗漏 Entity 字段。
5. **数据库脚本存在阻断级冲突。** `tools/sql/20260430_create_packing_list_tables.sql` 创建 `Id INT`、`PackingListHeadId INT` 且字段语义为供应商装箱；当前 Entity 使用 `Uid Guid`、`Version`、`PackingListHeadId Guid` 且语义为客户出货。`tools/sql/20260701_core_order_entities_optimization.sql` 只在旧表上补部分列，不能完成主键和业务语义迁移。
6. **Infrastructure 基座已存在但主链能力不完整。** 所有重点接口都有 DI 注册和类文件，但“类存在”不等于“能力可用”：`IInventoryTransactionRepository` 是空接口、`PackingListHeadRepository.GetDetailByHeadIdAsync` 与 `DependentDemandRepository.GetPagedAsync` 未实现，`CustomerProductRepository.GetPagedAsync` SQL 别名/表名错误。
7. **跨层违规与 SQL/模型不一致已存在。** `StockService` 在 Application 层直接写 Dapper SQL，并向 `InventoryTransaction.Qty` 写入；当前 Entity/DTO 只有 `InQty/OutQty/ChangeQty`，该 SQL 与当前模型不一致。

## 1. 当前源码事实

| 范围 | 当前事实 | 证据 |
|---|---|---|
| 技术主键/审计 | 重点 Entity 均继承 `BaseEntity`：`Uid Guid`、`Version`、`IsDeleted`、创建/更新审计字段 | `SwiftExport.Core/Common/BaseEntity.cs` |
| Dapper 基座 | `DapperRepository<T>` 使用 `RepositoryBaseContext`、同一 `DbSession.Connection/Transaction`、顺序 Guid、Version 乐观并发和软删除 | `SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs` |
| DI | 重点业务 Repository 全部 scoped 注册；`DbSession`、`RepositoryBaseContext`、泛型仓储均 scoped | `SwiftExport.Infrastructure/DependencyInjection.cs` |
| Mapster | Application 仅有程序集扫描，没有 Product/BOM/订单等集中显式映射；多数读写依赖同名默认映射 | `SwiftExport.Application/Mappings/MapsterConfig.cs`；各 Service 的 `Adapt` 调用 |
| 产品查询 | `ProductRepository.GetPagedAsync` 联 ProductColor、ProductCategory，并通过 `OUTER APPLY` 取默认 ProductPackage | `SwiftExport.Infrastructure/Repositories/Products/ProductRepository.cs:64` |
| 产品缺陷 | `GetAllCustomerCodeListAsync` 从 Product 查询不存在的 `CustomerCode`；`ProductDto.SupplierName` 没有 SQL 来源 | 同文件 `:24`、ProductDto |
| 客户产品查询 | 单条查询 Join 正确但关联表未过滤 `IsDeleted`；分页 SQL 使用未定义别名 `cpc`，并混用 CustomerProduct/CustomerPoConfig 表 | `CustomerProductRepository.cs:24,75` |
| BOM | Entity 已有 Parent/Sub Uid+Sku、版本与行号；查询不统一过滤软删除，部分调用未传当前事务 | `Bom.cs`；`BomRepository.cs` |
| 销售订单 | 管理查询已联销售行与毛需求；保存、确认、取消确认仍有未实现路径 | `SalesOrderHeadRepository.cs`；`SalesOrderManagerService.cs:159,265`；`SalesOrderDetailService.cs:124,278` |
| 毛需求 | Repository 有来源查询与拆解标记，但批量完工数量、分页及多项服务操作未实现 | `IndependentDemandRepository.cs:44`；`IndependentDemandService.cs` |
| 净需求 | Entity 与基础查询存在；分页未实现、转采购核心代码被注释，`DemandExecutionLink` 未参与转单 | `DependentDemandRepository.cs:88`；`DependentDemandService.cs` |
| 采购 | 头/行 Repository 基础查询存在；分页、创建、更新、批量保存均未实现 | `PurchaseOrderService.cs:42,271,466,654` |
| 入库 | 头/行仓储仅基础 CRUD；待入库、保存、过账均未实现 | `ReceiveService.cs:90,375,528` |
| 出库 | Entity、空扩展接口、泛型仓储实现和 DI 存在；无 Application Service、Controller、Refit API 调用方 | Outbound Entity/Interface/Repository 与 DI |
| PackingList | 头行模型与仓储存在；详情仓储、分页、保存、过账及内部步骤大量未实现 | `PackingListHeadRepository.cs:28`；`PackingListService.cs` |
| 库存余额 | `InventoryRepository` 按仓库+库位+SKU 查询，但空参数时直接返回 null；无原子增减接口 | `InventoryRepository.cs` |
| 库存流水 | Entity 定义完整不可变流水字段；Interface 为空；实现只是继承泛型仓储；Application 又绕过仓储直接写错误列 | `IInventoryTransactionRepository.cs`；`InventoryTransactionRepository.cs`；`StockService.cs:60` |
| 数据库事实边界 | 仓库只有 3 个 SQL 文件；除 PackingList、Outbound、DemandExecutionLink 和少量列外，没有重点表的完整建表事实；未连接部署数据库验证 | `rg --files -g '*.sql'` 结果 |
| WPF | 新 .NET 10 WPF 已实用 ProductDto；其余主链 API 已注册但页面多为占位。旧 `SwiftExport.View.FW` 仍大量绑定销售、采购、入库、PackingList DTO | `SwiftExport.Client.Wpf/Bootstrapper/ApiRegistrationExtensions.cs`；`SwiftExport.View.FW/**` |

> “当前数据库事实”仅指仓库脚本可证明的事实，不代表生产库已执行或与脚本一致。

## 2. Entity 字段审计表

### 2.1 所有 BaseEntity 技术字段

| Entity/字段 | 分类 | 结论 | 原因与影响 |
|---|---|---|---|
| 全部 Entity：`Uid` | 技术主键 | **保留** | 全链路 Guid 主键；DTO、Dapper、Repository、Refit/WPF 均依赖。任何改型均为全库迁移。 |
| 全部 Entity：`Version` | 技术字段 | **保留** | 泛型仓储更新/删除依赖。库存流水虽不可更新，也应保留一致结构但禁止业务更新。 |
| 全部 Entity：`IsDeleted` | 技术字段 | **保留** | 主数据/业务单据软删除过滤依赖；库存流水应固定为 0，不能提供删除能力。 |
| 全部 Entity：`CreatedDate/CreatedBy/UpdatedDate/UpdatedBy` | 审计字段 | **保留** | `AuditStampService` 与 Dapper 写入依赖；库存流水的 Updated 字段只为结构兼容，不应更新。 |

### 2.2 产品、分类、BOM

| Entity | 字段（逐字段覆盖） | 分类 | 结论 | 影响链 |
|---|---|---|---|---|
| Product | `Sku` | 自身业务键 | **保留** | ProductDto、BOM、销售/采购/库存全链依赖；应为未删除唯一键。 |
| Product | `MasterSku` | 逻辑关系（仅编码） | **待业务确认** | 需确认是产品族、父 SKU 还是旧系统字段；若为关系，目标为 `MasterProductUid + MasterSku`。影响 ProductDto/WPF/导入。 |
| Product | `DescriptionEn/DescriptionZh/FeaturesEn/FeaturesZh/MaterialType/SupplyType/Note` | 自身业务事实 | **保留** | BOM 拆解、销售选择、旧 WPF、ProductDto 使用。Material/Supply 应改为共享枚举是后续契约任务，不删列。 |
| Product | `ColorId/CategoryId` | 逻辑外键 Uid | **改名**为 `ColorUid/CategoryUid`（兼容迁移） | 当前 Join 以 Uid 关联；改名影响 SQL、ProductDto、WPF ProductLineModel、导入。 |
| Product | `BomVersion/BomNo` | BOM 快速入口 | **保留**，但不作 BOM 存在的唯一事实 | 必须与当前有效 BOM 版本由 Application 校验；影响 BomService/拆解。 |
| Product | `LinkSku` | 旧编码/逻辑关系 | **待业务确认** | 旧 WPF、客户产品、销售行使用；需定义目标实体与唯一性，不能直接删。 |
| Product | `MainImage` | 当前主数据展示资源 | **保留** | 产品列表、销售选择与旧 WPF 使用；订单/箱单若需历史图片应另行快照。 |
| Product | `CNYPrice/USDPrice` | 当前默认价格 | **保留** | 产品与销售候选查询；正式订单价格必须使用订单行快照，不回刷。 |
| Product | `UnitCBM/UnitWeight` | 当前物流主数据 | **保留** | ProductDto 当前遗漏，避免通过默认 Mapster 保存时误判；与 ProductPackage 的“每包装”字段语义不同。 |
| Product | `DefaultCustomerCode/DefaultCustomerSku/DefaultSupplierCode/DefaultSupplierSku` | 默认关系+编码 | **待业务确认** | 当前销售候选直接读取；应确认是否迁移到 CustomerProduct/SupplierProduct 的默认标记。若迁移，先补 Query DTO 与兼容读，再删除候选。 |
| Product | `IsNonStandard`（getter） | 派生字段 | **保留**为非持久化领域便利属性 | 不在 Dapper 写入属性中；若 API 需要，Query DTO 可显式返回。 |
| ProductCategory | `CategoryName/CategoryCode/SortNo` | 自身事实/业务键 | **保留** | `CategoryCode` 建议未删除唯一；WPF 分类树依赖。 |
| ProductCategory | `ParentId` | 自关联 Uid | **改名**为 `ParentUid` | 影响 Repository、DTO、WPF 树；需防环校验与 `(ParentUid,SortNo)` 索引评估。 |
| ProductCategory | `FakeName` | 命名歧义 | **改名**或**待业务确认** | 明确“报关名/内部别名/占位名”后命名；Product 查询当前返回它。 |
| ProductColor | `ColorCodeUnique/ColorCode` | 两个编码 | **待业务确认** | 需明确哪个是业务唯一键、哪个是展示码；两者不能无规则重复维护。 |
| ProductColor | `ColorNameEn/ColorNameZh` | 自身事实 | **保留** | Product/BOM/Sales Query DTO 联表使用。 |
| Bom | `BomNo/BomVersion/LineNumber` | 业务键/版本/行号 | **保留** | 建议 `(BomNo,BomVersion,LineNumber)` 未删除唯一；当前查询多处仅按 BomNo，需补版本条件。 |
| Bom | `ParentSkuId/ParentSku` | 父产品 Uid+编码 | **改名** `ParentProductUid`；`ParentSku` **保留** | 当前 BOM 为版本化主数据；若发布版本不可变，ParentSku 明确为版本快照；否则为可读冗余并校验一致。 |
| Bom | `SubSkuId/SubSku` | 子产品 Uid+编码 | **改名** `SubProductUid`；`SubSku` **保留** | 非标/历史缺失允许 SubUid 空时，SubSku 仍是必要快照；Application 校验标准件 Uid/编码一致。 |
| Bom | `Numerator/Denominator` | 自身业务事实 | **保留** | 增加 `>0` 检查；拆解数量依赖。 |
| Bom | `Note` | 自身事实 | **保留** | DTO/旧 WPF 使用。 |

### 2.3 销售订单、毛需求、净需求与执行链接

| Entity | 字段（逐字段覆盖） | 分类 | 结论 | 原因/影响 |
|---|---|---|---|---|
| SalesOrderHeader | `OrderNo` | 业务唯一键 | **保留** | 未删除唯一；所有来源追溯依赖。 |
| SalesOrderHeader | `OrderStatus` | 汇总展示状态 | **改名** `ExecutionStatus` 或明确仅汇总 | 已有 `ConfirmationStatus/ApprovalStatus`；禁止作为所有流程唯一判断。影响枚举、DTO、SQL、WPF。 |
| SalesOrderHeader | `ConfirmationStatus/ApprovalStatus` | 独立状态 | **保留** | 命令边界需明确；当前确认/取消确认实现未完成。 |
| SalesOrderHeader | `InvoiceNo/CustomerPoNo/ShippedBy` | 单据自身/外部编号 | **保留** | 人工查账和外部交换字段。 |
| SalesOrderHeader | `CustomerId/CustomerCode` | Customer Uid+编码快照 | `CustomerId` **改名** `CustomerUid`；`CustomerCode` **明确为历史快照** | 客户编码变更或软删除后仍要解释历史订单。 |
| SalesOrderHeader | `Currency/ExchangeRate` | 财务快照 | **明确为历史快照** | 订单成立后不得随汇率主数据更新。 |
| SalesOrderHeader | `TotalAmountOriginalCurrency/TotalAmountLocalCurrency` | 金额汇总快照 | **保留** | 保存/确认时由 Application 从行重算并校验，不能由页面任意写入。 |
| SalesOrderHeader | `ShipToName/ShipToAddress/ShipToPhone` | 收货信息快照 | **明确为历史快照** | 客户地址变化不得回刷历史单。 |
| SalesOrderHeader | `Incoterm/IncotermLocation/IncotermSortRemark/DepositRate/LeadTimeDays/PaymentMethod` | 合同条款快照 | **明确为历史快照** | 对外合同语义。`IncotermSortRemark` 命名待确认后改名。 |
| SalesOrderHeader | `SoFolder/Remark` | 单据事实 | **保留** | WPF 与管理 DTO 使用。 |
| SalesOrderHeader | `OrderDate/DeliveryDate/ReplyDate/ETA/ETD` | 单据日期事实 | **保留** | 不从当前主数据派生。 |
| SalesOrderLine | `LineNumber/SalesOrderId/OrderNo` | 行号+头关系 Uid/单号快照 | `SalesOrderId` **改名** `SalesOrderUid`；其余**保留** | 建议 `(SalesOrderUid,LineNumber)` 唯一；OrderNo 用于历史追溯。 |
| SalesOrderLine | `Sku/CustomerSku/LinkSku/DescriptionEn/ColorNameEn/FeaturesEn/IsNonStandard` | 产品/客户产品快照 | **明确为历史快照** | 当前没有 ProductUid；后续可新增 `ProductUid`，但这些快照仍不得删除。 |
| SalesOrderLine | `CnyPrice/UsdPrice/CnyAmount/UsdAmount/Qty` | 交易快照 | **明确为历史快照** | 金额由价格×数量重算并校验；确认后冻结。 |
| SalesOrderLine | `PcsOfCarton/NwOfCarton/GwOfCarton/CbmOfCarton/Ctns/NwSubTotal/GwSubTotal/CbmSubTotal` | 包装/计算快照 | **明确为历史快照** | 影响销售单与 PackingList；可计算但正式单据需保留当时值。 |
| SalesOrderLine | `SubNote/ExpectedDeliveryDate` | 行自身事实 | **保留** | 行级备注与交期。 |
| SalesOrderLine | `LastShippedDate` | 下游汇总缓存 | **移至 Query DTO** | 当前可由毛需求/出库流水聚合；多个写入源风险。迁移前检查 SalesOrderLineDto/WPF。 |
| SalesOrderLine | `MainImage` | 展示字段 | **移至 Query DTO** | 从 Product 实时联表即可；历史单据通常无需冻结图片。 |
| IndependentDemand | `SourceType/SourceUid/SourceNo/SourceLineId` | 多态来源 Uid+编号快照 | **保留** | 一对一来源应有未删除唯一 `(SourceType,SourceLineId)`；校验 Uid/No/Line 一致。 |
| IndependentDemand | `OrderStatus` | 执行状态 | **改名** `ExecutionStatus` | 与 ConfirmationStatus 分层。 |
| IndependentDemand | `ConfirmationStatus` | 确认状态 | **保留** | 不与拆解/齐套/发货混用。 |
| IndependentDemand | `SourceSku/Sku/Description/ColorName/Features/IsNonStandard` | 需求成立时物料快照 | **明确为历史快照** | 非标件无法可靠回联 Product；标准件后续可补 ProductUid。 |
| IndependentDemand | `OriginalQty/FilledQty/ShippedQty` | 原始量+执行汇总 | **保留** | OriginalQty 快照；Filled/Shipped 为事务性缓存，必须与执行链接/库存单据同事务维护。 |
| IndependentDemand | `IsShipped` | 与 ShippedQty 重复 | **删除候选** | 以 `ShippedQty >= OriginalQty` 或明确“曾发货”语义计算；影响 DTO（DTO 当前没有该字段）和 SQL。 |
| IndependentDemand | `KittingRate` | 派生值 | **移至 Query DTO** | 由齐套数量/OriginalQty 计算；当前 DTO 还混有 `IsKitted`。 |
| IndependentDemand | `LastShippedDate` | 执行汇总 | **保留**或 Query DTO，**待业务确认** | 若需高频审计缓存则保留并只由出库过账写；否则实时聚合。 |
| IndependentDemand | `HopefulDate/Remark` | 需求事实 | **保留** | 需求计划输入。 |
| IndependentDemand | `IsDecomposed/DecomposedTime` | 幂等执行事实 | **保留** | Repository 已有并发标记方法。 |
| DependentDemand | `SourceType/SourceUid/SourceNo/SourceLineId/IndependentDemandId` | 来源链 | **保留** | `IndependentDemandId` 改名 `IndependentDemandUid`；校验四元组一致，索引来源和毛需求 Uid。 |
| DependentDemand | `TargetNo/TargetLineId` | 快速入口缓存 | **保留**但非关系权威 | 正式多对多以 DemandExecutionLink 为准；回退/重建必须同步清空。 |
| DependentDemand | `OrderStatus/ConfirmationStatus` | 分层状态 | `OrderStatus` **改名** `ExecutionStatus`；Confirmation 保留 | 当前 SearchRequest 使用不存在的 `StatusFlag`，需契约统一。 |
| DependentDemand | `SourceSku/Sku/Description/ColorName/Features/ParentSku/BomLevel/SupplyType/IsNonStandard` | BOM 拆解快照 | **明确为历史快照** | 拆解结果必须可复现；建议另补当前物料/父物料 Uid，但不删除快照。 |
| DependentDemand | `GrossQty/NetQty` | 需求计算快照 | **明确为历史快照** | 记录拆解/净算当时结果。 |
| DependentDemand | `ExecutedQty/ReceivedQty/ReadyQty` | 执行链接聚合缓存 | **保留** | 权威明细为 DemandExecutionLink；Application 同事务汇总，禁止独立页面写。 |
| DependentDemand | `IsReady` | 与 ReadyQty/NetQty 重复 | **删除候选** | 由 `ReadyQty >= NetQty` 计算；影响 Entity、默认 Mapster、旧 WPF。 |
| DependentDemand | `ShipToLocation` | 编码/地址歧义 | **改名**或**待业务确认** | 拆为 `DestinationType + WarehouseUid/LocationCode + AddressSnapshot`，否则无法校验。 |
| DependentDemand | `RequiredDate/Remark` | 需求事实 | **保留** | 排程与人工说明。 |
| DependentDemand | `SupplierCode/SupplierName` | 手工供应商编码+名称快照 | `SupplierCode` **保留**；`SupplierName` **明确为历史快照** | 后续建议补 SupplierUid；UpdateSupplier 必须校验 Uid/Code/未删除。 |
| DemandExecutionLink | `DependentDemandId` | 净需求逻辑外键 | **改名** `DependentDemandUid` | 必须索引并校验目标存在、未删。 |
| DemandExecutionLink | `TargetType/TargetHeadId/TargetNo/TargetLineId` | 多态目标 Uid+编号快照 | **保留** | Target Uid 为关系主源，TargetNo 为历史/人工追溯；按 TargetType 校验实际表。 |
| DemandExecutionLink | `ExecutedQty/ReceivedQty/ReadyQty` | 分配事实 | **保留** | 是多净需求聚合到执行行时的权威数量明细。 |
| DemandExecutionLink | `Remark` | 自身事实 | **保留** | 人工追溯。 |

### 2.4 采购、入库、出库、PackingList、库存

| Entity | 字段（逐字段覆盖） | 分类 | 结论 | 原因/影响 |
|---|---|---|---|---|
| PurchaseOrderHead | `OrderNo` | 业务唯一键 | **保留** | 未删除唯一。 |
| PurchaseOrderHead | `OrderStatus/ConfirmationStatus` | 分层状态 | `OrderStatus` **改名** `ExecutionStatus`；Confirmation 保留 | DTO 目前把 OrderStatus 定义为 int 且遗漏 ConfirmationStatus。 |
| PurchaseOrderHead | `BoNo` | 外部/自定义编号 | **保留** | 允许重复的人工查账字段，不作键。 |
| PurchaseOrderHead | `SalesOrderNo/CustomerPoNo/CustomerCode` | 来源快照 | **明确为历史快照** | 一个采购单可能聚合多来源时头字段可能不唯一，需待业务确认是否移到关联/行。 |
| PurchaseOrderHead | `Currency/SupplierCode/SupplierName` | 交易主数据快照 | **明确为历史快照** | 建议补 SupplierUid；供应商名称不得回刷。 |
| PurchaseOrderHead | `SoPerson/PoPerson/InletrHoseType/AeratorType/PalletType/BoxType/LabelType/IMType/CartonType/LogoType` | 特殊业务字段 | `InletrHoseType` **改名** `InletHoseType`；其余**待业务确认** | 若只用于打印模板可保留为单据快照；若属于产品/包装行，迁移到行或专用扩展表。 |
| PurchaseOrderHead | `ReviewedBy/PoFolder/ZheKouBeiZhu/Remark/IsLocked` | 单据事实 | **保留**；中文拼音字段后续兼容改名 | DTO 另有 ApprovalStatus/ApprovedBy 但 Entity 缺失，需统一审批模型。 |
| PurchaseOrderHead | `LineCount/TotalQty/TotalShippedQty/TotalUnshippedQty/TotalReadyQty` | 汇总/派生 | **移至 Query DTO** | 由行/入库执行聚合；当前多写入源且保存未实现。 |
| PurchaseOrderHead | `TotalAmountLocalCurrency/ZhiJieYouHuiJinE/ZheKou` | 金额/折扣快照 | **明确为历史快照** | 确认前重算并冻结；字段改名需 DTO/旧 WPF 兼容。 |
| PurchaseOrderHead | `OrderDate/ExpectedDate/ReplyDate` | 单据日期事实 | **保留** | 采购合同快照。 |
| PurchaseOrderLine | `LineNumber/PurchaseOrderId/OrderNo` | 行号+头关系 | `PurchaseOrderId` **改名** `PurchaseOrderUid`；其余保留 | `(PurchaseOrderUid,LineNumber)` 未删除唯一。 |
| PurchaseOrderLine | `Sku/CustomerSku/LinkSku/SupplierSku/DescriptionZh/FinishZh/FeaturesZh/MaterialType/SupplyType` | 采购物料快照 | **明确为历史快照** | 后续可补 ProductUid/SupplierProductUid，但不能删除文本快照。 |
| PurchaseOrderLine | `UnitPrice/TaxPrice/QTY/UnitAmount/TaxAmount` | 交易快照 | `QTY` **改名** `Qty`；其余**明确为历史快照** | 增加金额一致性校验；影响 DTO、SQL、WPF Binding。 |
| PurchaseOrderLine | `SubNote/DeliveryTo` | 行事实 | **保留**；DeliveryTo 待结构化 | 客供件流向依赖。 |
| PurchaseOrderLine | `SourceType/SourceNo` | 来源可读快照 | **保留**但非关系权威 | 聚合多净需求时不能作为唯一关系；DemandExecutionLink 承担权威映射。 |
| PurchaseOrderLine | `ReadyStatus` | 派生状态 | **移至 Query DTO**或改明确枚举 | 与 ReadyQty/QTY 可推导，当前 int 语义不明。 |
| PurchaseOrderLine | `ReadyQty/RecQty/LastRecDate` | 执行汇总缓存 | **保留** | 仅由收货/供应商反馈命令更新，与执行链接同事务。 |
| PurchaseOrderLine | `FollowUpRemarks/FollowUpTime` | 当前跟进摘要 | **保留** | `FollowUpLog` 是历史明细；这里可作最后一次摘要缓存。 |
| PurchaseOrderLine | `FollowExpectedDate` | 派生/人工日期混淆 | **待业务确认** | 应拆成供应商承诺日期与系统预测日期；预测值进 Query DTO。 |
| ReceiveHead | `ReceiveNo/SupplierCode/ReceiveDate/Handler/Remark` | 单据键和快照 | **保留** | 建议补 SupplierUid；SupplierCode 为快照。 |
| ReceiveHead | `IsCompleted` | 过账状态歧义 | **改名**为 `PostStatus` 或 `IsPosted` | DTO 使用 IsPosted；Controller/Service 注释又称过账，必须统一。 |
| ReceiveHead | `RelatedPoNos` | 逗号拼接派生字段 | **移至 Query DTO**，列为删除候选 | 应从 ReceiveLine.PoNo 聚合；字符串无法约束和可靠查询。 |
| ReceiveLine | `LineNumber/ReceiveId/ReceiveNo` | 行号+头关系 | `ReceiveId` **改名** `ReceiveUid`；其余保留 | `(ReceiveUid,LineNumber)` 唯一；DTO 当前遗漏 LineNumber/ReceiveUid。 |
| ReceiveLine | `WarehouseCode/LocationCode` | 仓库/库位逻辑关系 | **保留** | 建议 WarehouseUid+WhseCode；LocationCode 不能既泛指库位又默认供应商编码而无类型。 |
| ReceiveLine | `Sku/Qty/Unit` | 入库事实/快照 | **保留** | 建议补 ProductUid；数量必须 >0。 |
| ReceiveLine | `PoLineId/PoNo` | 采购行 Uid+单号快照 | `PoLineId` **改名** `PurchaseOrderLineUid`；PoNo 明确为快照 | 入库过账回写依据。 |
| OutboundHead | `OutboundNo/OutboundDate/BizType/SourceType/SourceNo/WarehouseCode/LocationCode/Handler/Remark` | 正式出库单事实 | **保留** | SourceNo 为快照；建议补 SourceUid、WarehouseUid。 |
| OutboundHead | `IsPosted/PostedDate/PostedBy` | 过账事实 | **保留**，目标改为 PostStatus | 需支持反过账/红冲，bool 不足以表达完整生命周期。 |
| OutboundLine | `LineNumber/OutboundId/OutboundNo` | 行号+头关系 | `OutboundId` **改名** `OutboundUid`；其余保留 | `(OutboundUid,LineNumber)` 唯一。 |
| OutboundLine | `Sku/Qty/Unit/WarehouseCode/LocationCode` | 出库事实/快照 | **保留** | 建议 ProductUid/WarehouseUid；与头仓库不同时需业务校验。 |
| OutboundLine | `SourceType/SourceNo/SourceLineId/RefNo` | 多级追溯 | **保留** | 定义 Source 三元组为直接来源，RefNo 仅人工参考。 |
| OutboundLine | `Remark` | 行事实 | **保留** | — |
| PackingListHead | `PackingListNo/CustomerCode` | 业务键+客户快照 | **保留** | 建议补 CustomerUid；CustomerCode 历史快照。 |
| PackingListHead | `InvoiceNo` | 特殊人工编号 | **保留**但禁止业务关系 | 源码注释已明确。 |
| PackingListHead | `ShipToName/ShipToAddress/ShipToPhone/Attention` | 出货地址快照 | **明确为历史快照** | 装箱单打印/追溯。 |
| PackingListHead | `ContainerNo/SealNo/ETD/ETA/PackingDate/Remark` | 单据事实 | **保留** | — |
| PackingListHead | `IsPosted/PostedDate/PostedBy` | 过账事实 | **保留**，目标改为 PostStatus | 需反过账/红冲语义。 |
| PackingListLine | `LineNumber/PackingListNo/PackingListHeadId` | 行号+头关系 | `PackingListHeadId` **改名** `PackingListHeadUid`；其余保留 | 当前旧 WPF 模型仍为 int，需兼容迁移。 |
| PackingListLine | `SourceType/SourceNo/SourceLineId/DependentDemandId/PurchaseOrderLineId/SalesOrderNo` | 多级追溯 | Uid 字段改名 `...Uid`；其余**保留** | 必须定义直接来源与辅助快照优先级；DTO 当前遗漏大部分字段并另有不存在的 SoLineId。 |
| PackingListLine | `CustomerSku/Sku/DescriptionEn/FinishEn/Unit` | 出货物料快照 | **明确为历史快照** | 打印与客户追溯。 |
| PackingListLine | `Qty/PcsPerPackage/Nw_KG/Gw_KG/Cbm_m3/TotalCtns/TotalNw_KG/TotalGw_KG/TotalCbm_m3` | 数量/包装计算快照 | **明确为历史快照** | 可计算但装箱单成立后需冻结。 |
| PackingListLine | `ContainerNo/SealNo/InvoiceNo` | 头行重复 | **待业务确认** | 若允许一张箱单多柜/多封条则保留为行/分柜快照；否则删除候选并只存头。 |
| PackingListLine | `Remark` | 行事实 | **保留** | — |
| Inventory | `WhseCode/LocationCode/Sku` | 余额维度键 | **保留** | 建议补 WarehouseUid/ProductUid；未删除唯一 `(WhseCode,LocationCode,Sku)`。 |
| Inventory | `Unit` | 当前单位 | **保留** | 同一余额键不得出现单位冲突；Application 校验换算。 |
| Inventory | `OnhandQty` | 当前余额 | **保留** | 必须与库存流水同事务原子更新，禁止普通页面 CRUD。 |
| InventoryTransaction | `WhseCode/LocationCode/Sku/BizType/BizNo/BizLineId/RefNo` | 不可变历史快照/逻辑来源 | **明确为历史快照** | 不随仓库、SKU、来源单据改名更新；BizLineId 按 BizType 解释。 |
| InventoryTransaction | `InQty/OutQty/ChangeQty/PostQty` | 不可变数量事实 | **明确为历史快照** | 检查 `ChangeQty=InQty-OutQty` 且单次仅一个方向；PostQty 是过账后余额快照。 |
| InventoryTransaction | `UnitPrice/Amount` | 成本快照 | **明确为历史快照** | `Amount=ChangeQty*UnitPrice` 的符号和币种规则待确认。 |

## 3. 建议修改的 Entity 清单（不在本次实施）

| 优先级 | Entity | 建议 |
|---|---|---|
| P0 | InventoryTransaction / Inventory | 封闭普通 Update/Delete；补专用原子过账接口；统一流水列模型，移除 Application 直接 SQL。 |
| P0 | PackingListHead/Line | 先解决数据库 INT 主键脚本与 Guid Entity 的根本冲突，再谈业务实现。 |
| P0 | DependentDemand / DemandExecutionLink | 明确 Link 为权威多对多关系，DependentDemand 数量为聚合缓存，Target 字段仅快速入口。 |
| P0 | ReceiveHead/Line | 统一 IsCompleted/IsPosted/PostStatus；补齐 DTO 的 LineNumber、ReceiveUid、仓库/库位字段。 |
| P1 | PurchaseOrderHead/Line | 清理头汇总派生字段、统一 Qty 命名、补 Supplier/Product Uid 与快照语义。 |
| P1 | SalesOrderLine | 补 ProductUid；将 MainImage、LastShippedDate 移至 Query DTO；冻结交易/包装快照。 |
| P1 | Product/Bom | Uid 命名治理、默认客户/供应商关系归属、BOM 版本唯一性与发布语义。 |
| P2 | Category/Color | ParentUid、防环、颜色双编码语义与唯一键。 |

## 4. 必须保留的历史快照字段

- 销售头：`CustomerCode`、币种/汇率、ShipTo、Incoterm、付款条款、各业务日期。
- 销售行：`OrderNo`、`Sku/CustomerSku/LinkSku`、描述/颜色/规格、价格/金额、包装/重量/体积。
- 毛/净需求：来源单号、来源 SKU、当前/父 SKU、描述/颜色/规格、Gross/Net/OriginalQty、SupplyType、RequiredDate。
- 采购头：`OrderNo/BoNo`、SupplierCode/SupplierName、Currency、客户/销售来源号、折扣和日期。
- 采购行：OrderNo、SKU/客户/关联/供应商 SKU、描述/饰面/规格、价格/税价/金额、来源号、交期。
- 入出库：Receive/Outbound No、SKU、仓库/库位编码、上游单号与行 Uid、单位和数量。
- PackingList：客户、ShipTo、柜号/封条/发票号、物料描述、包装、重量、体积及总计。
- 库存流水：全部业务、数量、成本与 `PostQty` 字段；流水只能追加，错误通过反向/红字流水修正。

## 5. 实体关系与一致性风险矩阵

| 来源 → 目标 | 当前字段/基数 | 关系语义 | Application 一致性校验 | 风险/索引/强 FK |
|---|---|---|---|---|
| Product → ProductCategory | `CategoryId -> Uid`，多对一 | 当前 Uid | 类别存在、未删 | 索引 Product(CategoryId)；跨主数据不强 FK。 |
| Product → ProductColor | `ColorId -> Uid`，多对一 | 当前 Uid | 颜色存在、未删 | 索引 Product(ColorId)。 |
| Product → Product（Master） | `MasterSku -> Sku`，多对一待定 | 仅编码 | 编码存在、不得自环 | 孤立/改码风险；确认语义后补 Uid。 |
| Bom → Product（父） | `ParentSkuId+ParentSku`，多对一 | Uid+版本可读快照 | Uid/Sku 同记录、未删、发布版本规则 | `(BomNo,BomVersion,LineNumber)` 唯一；不强 FK以允许历史解释。 |
| Bom → Product（子） | `SubSkuId+SubSku`，多对一 | 标准件 Uid+快照，非标可空 | 同上；防 BOM 环、分母>0 | SubUid/Sku 索引。 |
| SalesOrderHeader → Customer | `CustomerId+CustomerCode`，多对一 | Uid+编码快照 | 创建时一致且客户有效；历史不回刷 | CustomerId 索引；跨模块不强 FK。 |
| SalesOrderHeader → SalesOrderLine | `line.SalesOrderId`，一对多 | 强父子 | 行必须属于头，LineNumber 唯一 | 建议强 FK（收益：防孤行；副作用：软删/导入需先行后头，不能物理删有历史的头）。 |
| SalesOrderLine → IndependentDemand | `SourceType+SourceLineId`，一对一 | 逻辑来源 | 确认销售行后幂等创建；数量/快照一致 | 过滤唯一 `(SourceType,SourceLineId)`；不强 FK（多态来源）。 |
| IndependentDemand → DependentDemand | `IndependentDemandId`，一对多 | 拆解结果 | 仅确认且未重复拆解；BOM 版本/数量守恒 | 索引 IndependentDemandId；可不强 FK以支持多来源历史。 |
| DependentDemand → PurchaseOrderLine | `TargetLineId`，多对一 | 快速入口缓存 | 不作为权威；从 Link 汇总校验 | 孤立与回退不同步风险；索引 TargetLineId。 |
| DependentDemand → DemandExecutionLink | `link.DependentDemandId`，一对多 | 权威执行分配 | Link 数量总和不得超过 NetQty；状态同事务汇总 | 现有索引；可评估强 FK，副作用是历史导入/软删顺序。优先逻辑校验。 |
| DemandExecutionLink → 执行头/行 | `TargetType+TargetHeadId+TargetLineId+TargetNo`，多态多对一 | Uid 主关系+No 快照 | 按 TargetType 检查目标存在、头行一致、未删、数量有效 | 现有 `(TargetType,TargetLineId)`；建议唯一 `(DependentDemandId,TargetType,TargetLineId)` 过滤索引；不强 FK。 |
| PurchaseOrderHead → Supplier | `SupplierCode+SupplierName`，多对一 | 编码+名称快照，缺 Uid | 供应商有效；名称冻结 | SupplierCode 索引；建议后补 SupplierUid。 |
| PurchaseOrderHead → PurchaseOrderLine | `PurchaseOrderId`，一对多 | 强父子 | 头行单号一致、LineNumber 唯一 | 建议强 FK，副作用同销售头行。 |
| PurchaseOrderLine → Product | `Sku`，多对一 | 仅编码快照 | 创建时 SKU 有效/物料类型一致；历史不依赖当前 Product | 编码变更风险；后补 ProductUid。 |
| PurchaseOrderLine → DependentDemand | DemandExecutionLink，多对多 | 正式追溯 | 分配数量总和=采购行来源量；供应商/单位兼容 | 当前转单未写 Link，阻断追溯。 |
| ReceiveHead → ReceiveLine | `ReceiveId+ReceiveNo`，一对多 | 强父子 Uid+单号快照 | 头行一致、LineNumber 唯一 | 建议强 FK；当前 DTO 遗漏 ReceiveId。 |
| ReceiveLine → PurchaseOrderLine | `PoLineId+PoNo`，多对一 | Uid+单号快照 | PO 行存在、未删、供应商/数量/状态允许 | 索引 PoLineId；跨单据不强 FK。 |
| ReceiveLine → Warehouse/Product | `WarehouseCode/LocationCode/Sku` | 编码逻辑关系 | 仓库有效，单位兼容，位置类型明确 | 组合查询索引；改码风险。 |
| OutboundHead → OutboundLine | `OutboundId+OutboundNo`，一对多 | 强父子 | 头行一致、LineNumber 唯一 | 建议强 FK；需先定义红冲/反过账删除规则。 |
| PackingListHead → PackingListLine | `PackingListHeadId+PackingListNo`，一对多 | 强父子 | 头行一致、LineNumber 唯一 | 目标可强 FK；当前脚本类型冲突，迁移前不能加。 |
| PackingListLine → 来源行 | Source 三元组、DependentDemandId、PurchaseOrderLineId | 多态追溯 | 定义直接来源唯一主线；各 Uid/No 必须一致 | 当前字段重叠；索引 Source 与两个 Uid。 |
| Inventory → Warehouse/Product | `(WhseCode,LocationCode,Sku)` | 当前余额维度 | 仓库/SKU 有效、单位一致 | 过滤唯一组合索引；不强 FK以保留历史编码。 |
| InventoryTransaction → Inventory/业务行 | 同维度+`BizType/BizNo/BizLineId` | 不可变流水 | 余额与流水同事务、幂等键、防重复过账 | 建议 `(BizType,BizLineId,反向标识)` 幂等索引，具体需业务确认；不设多态强 FK。 |

## 6. DTO 分类表

| DTO | 当前分类 | 审查结论 |
|---|---|---|
| ProductDto | Entity DTO + Query DTO 混合 | CategoryName/Finish/包装/SupplierName 为 Query 字段；保存 DTO 应拆为 ProductEntityDto/命令，避免查询字段写回。 |
| ProductCategoryDto、ProductColorDto | Entity DTO | 基本合理。 |
| BomDto | Entity DTO + Query DTO 混合 | Description/Finish/Features/MainImage 为 Query 字段；且遗漏 Parent、Version、LineNumber，不能完整表达当前 Entity。 |
| SalesProductDto | Query DTO | 合理，但继承 ProductDto 使其携带无来源字段；建议独立定义。 |
| SalesOrderHeaderDto、SalesOrderLineDto | Entity DTO | 订单行 DTO 额外含 ProductId/CustomerProductId/MaterialType；应明确命令字段与快照映射。 |
| SalesOrderHeaderManagerDto、SalesOrderLineManagerDto | Query DTO | 合理；聚合 SQL 需补关联软删过滤，避免重复累计。 |
| SalesOrderHeaderManagerUpdateRequestDto | Command DTO | 明确的批量编辑命令，合理。 |
| IndependentDemandDto | Entity DTO + Query DTO 混合 | `IsKitted/MainImage` 为 Query 字段；遗漏 Entity.IsShipped。建议拆 Entity/Query。 |
| DependentDemandDto | Entity DTO + Query DTO 混合 | `ChangeTime` 无 Entity 来源；遗漏 SourceUid、ConfirmationStatus、IsReady/IsNonStandard；TargetLineId 空值性不一致。 |
| DecomposeRequest/DecomposeLineRequest、IndependentDemandDecomposeRequestDto | Command DTO | 业务意图明确；需统一重复请求类型。 |
| PurchaseOrderHeadDto | Entity DTO + Query DTO 混合 | 含实体不存在的 ApprovalStatus/ApprovedBy/ChangeTime；汇总字段应 Query-only；OrderStatus 类型不一致并遗漏 ConfirmationStatus。 |
| PurchaseOrderLineDto | Entity DTO + Query/Command 混合 | MainImage 是 Query，DemandIdList 是 Command；必须拆分。 |
| PurchaseOrderDetailDto | Command/Query 聚合混合 | `DeletedLineIds` 只属于保存命令，不应出现在读取详情 DTO。 |
| ReceiveHeadDto | Entity DTO | `IsPosted` 与 Entity.IsCompleted 不一致。 |
| ReceiveLineDto | 不完整 Entity DTO | 遗漏 LineNumber、ReceiveId、WarehouseCode、LocationCode，默认映射会丢失关键关系。 |
| AwatingInboundDto | Query DTO | 合理，但 Repository/Service 查询未实现；Unit 无可靠来源。 |
| PurchaseReceiveExecuteRequestDto/LineDto | Command DTO | 意图清楚；需与 ReceiveLine 的仓库/位置和来源关系对齐。 |
| PackingListHeadDto | Entity DTO | 基本同 Entity。 |
| PackingListLineDto | 不完整 Entity DTO + 旧字段 | 遗漏行号和正式追溯字段，含 Entity 不存在的 `SoLineId`；与旧 WPF `PackingListHeadId int` 冲突。 |
| PackingListDetailDto | Query/Command 聚合混合 | 不应继承 BaseDto；读写 DTO 应拆分。 |
| PackingListStockShortageDto | Query DTO | 合理，需明确库存 Join 与单位。 |
| PackingListPostRequestDto | Command DTO | 意图明确；Operator 应优先由当前用户取得，避免伪造。 |
| InventoryTransactionDto | Entity/Query DTO | 只读合理，但 API 不应暴露普通 Update/Delete。 |
| 各 `*SearchRequest` | Query 条件 DTO | `DependentDemandSearchRequest.StatusFlag/IsKitted` 与 Entity 不一致；PackingList Refit 把 GET 与 Body 组合，需修契约。 |
| 各 `BatchSaveRequest` | 通用 CRUD Command | 正式单据不应仅靠通用 CRUD 保存/过账；应使用明确命令。 |

## 7. 联表 Query DTO 字段来源与完整 Join 映射

### 7.1 ProductDto（当前已实现）

主查询：`Product p LEFT JOIN ProductColor pc ON p.ColorId=pc.Uid AND pc.IsDeleted=0 LEFT JOIN ProductCategory pat ON p.CategoryId=pat.Uid AND pat.IsDeleted=0 OUTER APPLY ProductPackage pkg`。基数均为产品多对一；pkg 通过 TOP 1 降为一对一。

| DTO字段 | 来源 | Join/条件 | 过滤/语义/空值 | 更新责任/Repository |
|---|---|---|---|---|
| BaseDto、Sku、描述、特性、MaterialType、SupplyType、Bom、价格、默认关系、MainImage | `p.同名字段` | 主表 | `p.IsDeleted=0`；实时当前值 | Product；ProductRepository |
| CategoryName | `pat.CategoryName` | LEFT `pat`；`p.CategoryId=pat.Uid` | pat 未删；可空 | ProductCategory；ProductRepository |
| Finish | `pc.ColorNameZh` | LEFT `pc`；`p.ColorId=pc.Uid` | pc 未删；可空 | ProductColor；ProductRepository |
| ColorCodeUnique/ColorCode/ColorNameEn（DTO 当前未声明全部） | `pc.*` | 同上 | 实时；Dapper 只映射存在属性 | ProductColor |
| PcsOfCarton/NwOfCarton/GwOfCarton/CbmOfCarton | `pkg.PcsPerPackage/Nw_KG/Gw_KG/Cbm_m3` | OUTER APPLY TOP 1；`pp.ProductId=p.Uid` | pp 未删；按 IsDefault、Pcs 降序；可空 | ProductPackage；ProductRepository |
| SupplierName | **无当前 SQL 来源** | 无 Join | 始终默认空 | 应删除该字段或定义 DefaultSupplierCode→Supplier.SupplierCode LEFT JOIN；SupplierRepository/产品查询仓储 |

### 7.2 BomDto（当前已实现但不完整）

| DTO字段 | 来源 | Join/条件 | 过滤/基数/语义 | 更新责任 |
|---|---|---|---|---|
| BaseDto、BomNo、SubSkuId、SubSku、Numerator、Denominator、Note | `t1 Bom` | 主表 | 当前 SQL未过滤 t1.IsDeleted；Bom行多对一子产品 | Bom |
| Description | `t2.DescriptionZh AS Description`（当前 SQL 实际未显式别名，DTO 属性叫 Description，存在映射失败） | LEFT Product t2；`t1.SubSkuId=t2.Uid` | 未过滤 t2.IsDeleted；实时 | Product；BomRepository |
| Finish | `t3.ColorNameZh AS Finish` | LEFT ProductColor t3；`t2.ColorId=t3.Uid` | 未过滤 t3.IsDeleted；实时 | ProductColor |
| Features | `t2.FeaturesZh`（未别名为 Features，存在映射失败） | 同 Product Join | 实时/可空 | Product |
| MainImage | `t2.MainImage` | 同 Product Join | 实时/可空 | Product |

### 7.3 CustomerProductDto（当前单条 Join）

| DTO字段组 | 来源 | Join/条件 | 过滤/基数/语义 | 更新责任 |
|---|---|---|---|---|
| CustomerProduct 自身字段 | `cp.*` 显式列 | 主表 | cp 未删；实体值 | CustomerProduct |
| CustomerCode | `c.CustomerCode` | LEFT Customer c；`cp.CustomerId=c.Uid` | 当前 SQL漏 c.IsDeleted；多对一；实时 | Customer |
| DescriptionEn/MainImage | `p.DescriptionEn/MainImage` | LEFT Product p；`cp.SkuId=p.Uid` | 当前 SQL漏 p.IsDeleted；多对一；实时 | Product |
| CategoryName | `pc.CategoryName` | LEFT ProductCategory pc；`p.CategoryId=pc.Uid` | 当前 SQL漏 pc.IsDeleted；多对一；实时 | ProductCategory |

分页实现不是上述 Join：它使用未定义 `cpc` 别名并从 CustomerPoConfig 取数据，当前不可作为有效血缘。

### 7.4 SalesProductDto（当前已实现）

| DTO字段组 | 来源 | Join/条件 | 过滤/基数 | 空值/责任 |
|---|---|---|---|---|
| CustomerProductId、CustomerCode/Sku、CustomerSku、LinkSku、价格、Currency、MOQ、Note、客户描述/特性 | 有客户时 `cp`；无客户时 Product 默认字段/NULL | 有客户：CustomerProduct cp LEFT Product p ON cp.SkuId=p.Uid | cp 未删且 Active；**漏 p.IsDeleted**；cp→p 多对一 | 无客户时客户专属字段 NULL；CustomerProduct/Product |
| ProductId/Sku/MaterialType/MainImage | `p.Uid/Sku/MaterialType/MainImage`（有客户时部分从 cp） | 同上 | 实时 | Product |
| ColorNameEn | `pc.ColorNameEn` | LEFT ProductColor pc ON p.ColorId=pc.Uid | 当前 SQL漏 pc.IsDeleted；多对一 | ProductColor |
| 包装四字段 | `pkg` | OUTER APPLY ProductPackage pp ON pp.ProductId=p.Uid | pp.IsDeleted=0，TOP1 默认包装 | ProductPackage；注意 SQL 用 `pp.Cbm`，Entity 为 `Cbm_m3`，需核实真实列 |

### 7.5 SalesOrderLineManagerDto（当前已实现）

| DTO字段组 | 来源 | Join/条件 | 过滤/基数/语义 | 空值/责任 |
|---|---|---|---|---|
| 行自身字段 | `l SalesOrderLine` | 主表，`l.SalesOrderId=@OrderId` | l 未删；一头多行；历史快照 | SalesOrderLine |
| ShippedQty | `SUM(d.ShippedQty)` | OUTER APPLY IndependentDemand d；`d.SourceType=@销售订单 AND d.SourceLineId=l.Uid` | 当前漏 d.IsDeleted；逻辑一对一但 SUM 防重复；实时汇总 | IndependentDemand |
| UnshippedQty | `l.Qty-ISNULL(SUM(d.ShippedQty),0)` | 同上 | 不小于 0 的策略当前未限制 | Query 计算 |

### 7.6 SalesOrderHeaderManagerDto（当前已实现）

| DTO字段组 | 来源 | Join/条件 | 过滤/基数/语义 | 风险/责任 |
|---|---|---|---|---|
| 头自身字段 | `h SalesOrderHeader` | 主表 | h 未删；历史/当前头事实 | SalesOrderHeader |
| TotalQty | `SUM(l.Qty)` | LEFT SalesOrderLine l ON l.SalesOrderId=h.Uid | 当前漏 l.IsDeleted | SalesOrderLine 汇总 |
| TotalShippedQty | `SUM(d.ShippedQty)` | LEFT IndependentDemand d ON d.SourceType=@销售订单 AND d.SourceLineId=l.Uid | 当前漏 d.IsDeleted；若同一行多条需求会使 `SUM(l.Qty)` 被 Join 放大 | IndependentDemand |
| TotalUnshippedQty | 上两者相减 | 同上 | 实时计算 | 应先分别聚合行和需求再 Join，避免基数放大 |

### 7.7 PurchaseOrderLineDto / SupplierDto（当前已实现）

| DTO字段 | 来源 | Join/条件 | 过滤/基数/语义 | 更新责任 |
|---|---|---|---|---|
| PurchaseOrderLineDto 行字段 | `a PurchaseOrderLine.*` | 主表，`a.PurchaseOrderId=@id` | a 未删；Entity DTO/历史 | PurchaseOrderLine |
| MainImage | `b.Product.MainImage` | LEFT Product b ON `a.Sku=b.Sku` | 当前漏 b.IsDeleted；编码 Join；实时/可空 | Product；PurchaseOrderLineRepository |
| SupplierDto 全字段 | `s.*` | Line l LEFT Head h ON l.PurchaseOrderId=h.Uid LEFT Supplier s ON h.SupplierCode=s.SupplierCode | l/h 未删，`l.QTY>l.RecQty`；多行去重 DISTINCT；当前漏 s.IsDeleted | Supplier；PurchaseOrderLineRepository |

### 7.8 尚未实现但契约已暴露的 Query DTO 目标血缘

| DTO | 建议主查询与字段血缘 | 当前阻断 |
|---|---|---|
| AwatingInboundDto | `PurchaseOrderLine l INNER JOIN PurchaseOrderHead h ON l.PurchaseOrderId=h.Uid`; PoNo=`h.OrderNo`, PoLineId=`l.Uid`, Sku=`l.Sku`, PoQty=`l.QTY`, RecQty=`l.RecQty`, ReadyQty=`l.ReadyQty`, CanReceiveQty=`MAX(MIN(l.ReadyQty,l.QTY)-l.RecQty,0)`, MaterialType=`l.MaterialType`, SupplierCode=`h.SupplierCode`; l/h 均过滤未删 | `Unit` 在 PurchaseOrderLine 无来源；公式需业务确认；ReceiveService 当前抛异常。 |
| DependentDemandDto 分页 | 主表 `DependentDemand d`；若 SupplierName 改为实时则 LEFT Supplier s ON d.SupplierCode=s.SupplierCode 且 s 未删；但历史 SupplierName 应优先 d.SupplierName | Request 使用不存在的 StatusFlag/IsKitted；Repository 未实现。 |
| PurchaseOrderHeadDto 分页 | `PurchaseOrderHead h LEFT JOIN` 预聚合 PurchaseOrderLine l（LineCount/TotalQty/Ready/Received/Amount），按 h.Uid；所有关联过滤未删 | DTO 混入实体不存在审批字段；Service 未实现。 |
| PackingListDetailDto | 头 `h` + 行 `l ON l.PackingListHeadId=h.Uid`，各自未删、按 LineNumber；若需图片另 LEFT Product p ON l.Sku=p.Sku | Repository 未实现；DTO 追溯字段缺失；数据库主键类型冲突。 |
| PackingListStockShortageDto | PackingListLine/拆解 BOM 需求聚合 LEFT Inventory i ON `(WhseCode,LocationCode,Sku)`；RequiredQty=分解量，Onhand=ISNULL(i.OnhandQty,0)，Shortage=MAX(Required-Onhand,0) | 供应商位置来源、单位换算和 BOM 版本未定义。 |

## 8. 数据库设计建议（事实与目标分开）

### 8.1 当前可证明事实

- `DemandExecutionLink`、`OutboundHead/Line` 的 2026-07-01 脚本使用 Guid Uid、Version、IsDeleted，并有基础索引。
- 同脚本只给 Bom、PackingListLine、PurchaseReceiveLine 补部分列，没有创建或校正其主键。
- 2026-04-30 PackingList 脚本仍以 `Id INT IDENTITY` 为主键，字段集合与当前 Entity 不同。
- 没有 Product、BOM、SalesOrder、Demand、Purchase、Receive、Inventory、InventoryTransaction 的完整建表脚本，无法断言生产库约束。

### 8.2 目标建议

| 表 | 主键/业务键 | 逻辑关系/快照 | 唯一约束与索引 | 默认/迁移影响 |
|---|---|---|---|---|
| Product | PK Uid；Sku 未删除唯一 | Category/Color Uid；默认关系待治理 | Sku unique filtered；CategoryUid、ColorUid、MasterSku 索引 | Version=1、IsDeleted=0；先核对 Default* 数据。 |
| ProductCategory | PK Uid；CategoryCode 未删除唯一 | ParentUid 自关联 | ParentUid/SortNo；防环由 Application | 旧 ParentId 兼容迁移。 |
| Bom | PK Uid；业务键 `(BomNo,BomVersion,LineNumber)` | Parent/Sub Uid+Sku 快照 | 上述过滤唯一；Parent/SubUid、BomNo/Version 索引 | 为重复行号/空 Uid 清洗；发布版本不可覆盖。 |
| SalesOrderHeader | PK Uid；OrderNo 未删除唯一 | CustomerUid+Code、合同快照 | 状态+OrderDate分页；CustomerUid | 状态拆分需回填映射。 |
| SalesOrderLine | PK Uid；`(SalesOrderUid,LineNumber)` | ProductUid+快照 | 头行过滤唯一；Sku、CustomerSku（按真实查询） | MainImage/LastShippedDate 迁移到查询后再删列。 |
| IndependentDemand | PK Uid；`(SourceType,SourceLineId)` 过滤唯一 | 来源三元组+快照 | SourceUid、SourceNo、拆解状态索引 | 清理重复来源；KittingRate/IsShipped 迁移。 |
| DependentDemand | PK Uid | IndependentDemandUid、来源、Target 快速入口 | IndependentDemandUid；SourceType+SourceUid/Line；TargetLineUid；状态+RequiredDate | StatusFlag→ExecutionStatus 数据映射。 |
| DemandExecutionLink | PK Uid | 多态 Target Uid+No 快照 | 建议过滤唯一 `(DependentDemandUid,TargetType,TargetLineUid)`；Target、Demand 索引 | 先回填历史 TargetLineId 关系并核对分配量。 |
| PurchaseOrderHead | PK Uid；OrderNo 未删除唯一 | SupplierUid+快照 | SupplierUid/Code、OrderDate+状态 | 头汇总字段移 DTO 前先比对历史。 |
| PurchaseOrderLine | PK Uid；`(PurchaseOrderUid,LineNumber)` | ProductUid+快照 | 头行唯一；Sku、Source、状态/交期 | QTY→Qty 兼容列/双读迁移。 |
| PurchaseReceiveHead | PK Uid；ReceiveNo 未删除唯一 | SupplierUid+Code快照 | Supplier、ReceiveDate/PostStatus | IsCompleted→PostStatus 回填。 |
| PurchaseReceiveLine | PK Uid；`(ReceiveUid,LineNumber)` | PO行Uid+No、仓库/产品 | 头行唯一；PoLineUid；库存维度 | DTO 先补字段，再迁移关系。 |
| OutboundHead/Line | PK Uid；OutboundNo unique；头行 unique | 来源快照 | 脚本已有 Source 索引；补头行唯一（当前只是普通索引） | bool 状态升级需回填。 |
| PackingListHead/Line | PK Guid Uid；No unique；头行 unique | Customer/来源/包装快照 | HeadNo unique；HeadUid+Line unique；Source/PO/Demand索引 | **必须编写专门 INT→Guid 和字段语义迁移**；不能靠 20260701 脚本。 |
| Inventory | PK Uid；余额维度过滤唯一 | Warehouse/Product Uid+编码 | `(WhseCode,LocationCode,Sku)` unique filtered | 合并重复余额前与流水对账。 |
| InventoryTransaction | PK Uid；不可变 | 全部历史快照 | 业务幂等索引、维度+CreatedDate 查询索引 | 不提供 Update/Delete；将旧 Qty 拆成 In/Out/Change 需规则。 |

不建议当前为跨模块关系批量加数据库 FK。只建议在确认历史数据可清洗后，对 Sales/Purchase/Receive/Outbound/Packing 的头行强父子关系逐模块评估 FK；收益是防孤行，副作用是软删除、导入、改单和历史迁移必须严格按顺序，且不能物理删除已被引用头。

## 9. Repository / Infrastructure 缺口与实现顺序

| Interface | 实现/方法完整性 | DI | Application 调用方 | Context/事务 | 缺口与优先级 |
|---|---|---|---|---|---|
| IProductRepository | 有；分页可用但 CustomerCode 查询错误 | 有 | ProductService、BOM/需求/采购/Packing | Context/Transaction 基本正确 | P1 修 SQL、SupplierName 血缘、软删过滤。 |
| IProductCategoryRepository/IProductColorRepository | 有 | 有 | 产品服务 | Context/Transaction | Service 批量/分页未实现；P2。 |
| IBomRepository | 有；方法齐但过滤/事务不一致 | 有 | BomService、PackingList | Context；部分查询未传 Transaction | P1 版本条件、软删、事务、禁止物理删已发布 BOM。 |
| ISalesOrderHeadRepository | 有；查询/删除实现 | 有 | 三个销售 Service | Context/Transaction | 聚合基数和软删过滤；P1。 |
| ISalesOrderLineRepository | 有；查询实现 | 有 | 销售 Service | Context/Transaction | ProductPackage 列名、关联软删；P1。 |
| IIndependentDemandRepository | 有；`批量更新完工数量Async` 未实现 | 有 | 销售/毛需求/入库/Packing | Context；大部分传 Transaction | 分页/拆解服务大量未实现；P0/P1。 |
| IDependentDemandRepository | 有；Paged 未实现；Reset 写不存在的 StatusFlag；DeleteBySource 物理删 | 有 | 销售/净需求/跟进/入库 | Context/Transaction | P0：分页、状态字段、软删/安全回退。 |
| IDemandExecutionLinkRepository | 仅泛型继承，无专用方法 | 有 | **无调用方** | Context/Transaction | P0：按 Demand/Target 查询、批量新增、数量汇总、幂等。 |
| IPurchaseOrderHeadRepository | 有；按 BoNo/OrderNo | 有 | 净需求/采购/入库 | Context/Transaction | Service 保存/分页未实现；P0。 |
| IPurchaseOrderLineRepository | 有；两个 Query | 有 | 净需求/采购/入库/Packing | Context/Transaction | 只读行 Join 漏 Product 软删；待入库 DTO 未实现；P0。 |
| IReceiveHeadRepository/IReceiveLineRepository | 有；仅头按号扩展 | 有 | Receive/Packing | Context/Transaction | 缺详情、分页、按头行查询、专用过账写入；P0。 |
| IOutboundHeadRepository/IOutboundLineRepository | 有；仅泛型 CRUD | 有 | **无调用方** | Context/Transaction | 缺 Application/API、按来源查询、过账/红冲；P0（库存闭环）。 |
| IPackingListHeadRepository | 有；详情方法未实现 | 有 | PackingListService | Context/Transaction | P0：数据库迁移后实现分页/详情/锁定。 |
| IPackingListLineRepository | 有；按 No 查询 | 有 | PackingListService | Context/Transaction | 缺按头 Uid、来源/需求查询；P0。 |
| IInventoryRepository | 有；按维度查询 | 有 | Stock/Receive/Packing | Context/Transaction | 缺带 UPDLOCK 的原子增减/Upsert；P0。 |
| IInventoryTransactionRepository | **空接口**；实现虽继承 DapperRepository，但接口不可调用 Add | 有 | Receive 注入但有效代码未调用 | Context/Transaction | P0：只新增接口、批量追加、按业务幂等查询；禁止泛型 Update/Delete。 |
| IWarehouseRepository | 有 | 有 | 当前主链无明确调用 | Context/Transaction | P1：过账前仓库/虚拟仓校验。 |

基座附加问题：

- `DapperRepository.ExistsIdAsync/GetExistingIdListAsync/GetSingleEntityAsync` 未统一过滤 `IsDeleted`，Application 的“存在性”校验可能接受软删除记录。
- `StockService` 直接依赖 Dapper/DbSession SQL，违反 Application/Infrastructure 边界，且写入不存在的 `InventoryTransaction.Qty`。
- 大量 Service 的事务代码被注释；当前只有少数如销售管理局部更新/FollowUp 使用事务。主链保存/过账没有可运行的共享事务。
- `CustomerProductRepository.GetAllCustomerIdListAsync` 返回 `int`，实际 CustomerId 为 Guid；运行时映射风险。

## 10. Entity 变更影响链

| 变更 | 直接影响 | 兼容/迁移要求 | 风险 |
|---|---|---|---|
| `*Id` 改 `*Uid` | Entity、DTO、Dapper SQL、Mapster、JSON、Refit、WPF Binding、脚本 | 先新增/别名兼容，双读，迁移数据，再删除旧名 | 高 |
| `QTY`→`Qty` | Purchase DTO、旧 WPF、SQL、金额计算、入库查询 | JSON/SQL 别名兼容；数据库列迁移 | 高 |
| `OrderStatus` 状态拆分 | 枚举、Service 判断、SQL筛选、WPF状态显示、历史单据 | 定义旧值到 Document/Confirm/Execution/Post 映射 | 高 |
| 删除 SalesLine.MainImage/LastShippedDate | DTO、旧 WPF、管理查询 | 先补 Query DTO Join/聚合并改绑定 | 中 |
| 删除 Independent.IsShipped/KittingRate、Dependent.IsReady | Service 判断、搜索请求、旧 WPF | 提供计算字段 Query DTO；历史回归 | 高 |
| 删除 Receive.RelatedPoNos | 列表搜索、旧 WPF | Repository 用 `EXISTS ReceiveLine` 查询，DTO 仍可返回聚合字符串 | 中 |
| 采购头汇总移 DTO | Purchase DTO/旧 WPF/报表 | 新建 Query DTO 和预聚合 SQL；确认无外部导出依赖 | 高 |
| Packing INT→Guid | 两份脚本、Entity、DTO、旧 WPF Model、全部历史箱单 | 专项迁移表/映射、双键过渡、逐单核对 | 阻断级 |
| DemandExecutionLink 启用 | Dependent/Purchase/Receive/Packing/库存追溯 | 从旧 TargetLineId 回填 Link，校验数量守恒，再切换权威来源 | 阻断级 |

## 11. 可直接交给 backend Agent 的分阶段任务

### 阶段 0：只读数据核对与迁移决策

1. 导出生产库实际表结构、索引、约束和重点表行数；与 Entity 和两份 SQL 对比。
2. 核对 PackingList 实际使用的是旧“供应商装箱/INT”还是新“客户出货/Guid”语义；冻结继续写入窗口。
3. 检查重复业务键、孤立 Uid、Uid/编码不一致、软删引用、余额与流水差异。
4. 产出数据修复清单，不改业务代码。

验收：所有目标迁移都有可回滚脚本、影响行数和抽样单号。

### 阶段 1：Contracts 与关系权威模型

1. 拆分 Entity DTO、Query DTO、Command DTO；优先 Dependent/Purchase/Receive/Packing/Inventory。
2. 补齐 Uid/编码/名称快照语义；统一 Guid 类型、空值性和状态枚举。
3. 对齐 Controller 与 Refit：Purchase 的 `/page`、Guid id、Guid 返回；Receive 返回类型/缺失端点；Packing `/page`、Guid 返回；Independent batch request；Dependent 泛型 DTO。
4. 建立集中 Mapster 配置，禁止 Query-only 字段写入 Entity。

验收：Contracts 在 netstandard2.0 编译；WebApi 与 WPF 同时编译；契约测试逐路由对齐。

### 阶段 2：Infrastructure 基座与查询修复

1. 修复软删除存在性查询；修复 Product/CustomerProduct/BOM/Sales Query SQL。
2. 实现 DependentDemand 分页、Packing 详情、Receive 详情与待入库查询。
3. 建立 `IInventoryTransactionRepository.Add/AddRange/ExistsBizKey`，移除 Update/Delete 暴露。
4. 建立 Inventory 带锁原子 Upsert/Change 方法，所有 SQL 使用同一 DbSession Transaction。

验收：每个接口有实现、DI、调用方测试；Query DTO 字段与 SQL 别名逐字段匹配。

### 阶段 3：净需求→采购与 DemandExecutionLink

1. 实现转采购命令：校验状态、SupplyType、SupplierUid/Code、单位、数量。
2. 同事务创建采购头行、DemandExecutionLink，并汇总 DependentDemand 执行量/状态。
3. 禁止只回写 TargetLineId；Target 字段仅作 Link 投影缓存。
4. 支持取消未过账采购时回退 Link 和数量，禁止硬删已执行记录。

验收：多需求聚合一采购行、部分转单、重复请求、并发、取消转单数量守恒。

### 阶段 4：入库、出库、库存闭环

1. 实现 Receive 保存/过账：正式头行、采购行实收、Link ReceivedQty、Inventory、InventoryTransaction 同事务。
2. 实现 Outbound 正式单据与过账；库存扣减不再由 StockService 直接写 SQL。
3. 增加幂等业务键、防负库存锁策略、反过账/红冲命令。
4. 校验 `余额期末 = 期初 + ΣChangeQty`，流水只追加。

验收：成功、库存不足、重复过账、并发过账、异常回滚、红冲均保持单据/余额/流水一致。

### 阶段 5：PackingList 智能过账

1. 完成 Guid 数据迁移后实现保存/详情/分页。
2. 根据采购/需求完成情况生成正式自动入库单、正式出库单和流水。
3. 客供件按供应商虚拟仓/BOM 版本扣减；来源写 DemandExecutionLink/单据行追溯。
4. 实现 PostStatus、反过账/红冲，不以删除回退。

验收：已手工入库、未入库自动入库、客供件不足、允许/禁止负库存、多柜多行、重复过账、红冲全覆盖。

## 12. 尚需业务确认的问题

1. Product.MasterSku 的真实含义：产品族、父产品还是旧系统检索码？
2. Product 的 DefaultCustomer*/DefaultSupplier* 是否继续作为产品自身默认关系，还是迁移到 CustomerProduct/SupplierProduct 并增加 IsDefault？
3. ProductColor.ColorCodeUnique 与 ColorCode 的业务区别和唯一规则。
4. BOM 发布后是否不可变；Parent/Sub Sku 是否正式版本快照；是否允许非标 SubSkuId 为空。
5. 销售订单行是否必须新增 ProductUid；非标件如何表示没有 Product 的情况。
6. `IsShipped` 是“曾发生发货”还是“已全部发完”；`IsReady` 是“曾完工”还是“全部完工”。
7. DependentDemand.ShipToLocation 是仓库码、供应商码还是自由地址；客供件的目的供应商仓如何结构化。
8. PurchaseOrderHead 的 SalesOrderNo/CustomerPoNo/CustomerCode 在聚合多个来源时如何表达；是否移到行/关联表。
9. PurchaseOrderLine.FollowExpectedDate 是人工承诺日期还是系统预测日期。
10. ReceiveHead.IsCompleted 是否等价于已过账；是否需要草稿/已过账/已反过账/已红冲四态。
11. PackingList 是否允许一单多柜、多封条、多发票；若允许，Container/Seal/Invoice 应进入分柜实体还是行快照。
12. 生产库现存 PackingList 到底使用 INT 旧表还是 Guid 新模型；历史数据量与外部引用是什么。
13. InventoryTransaction 金额币种、成本算法和负号规则；反向流水使用负 In/Out 还是相反方向正数。
14. 业务编码软删除后是否允许复用；这决定普通唯一索引还是过滤唯一索引。
15. 哪些头行关系允许数据库强 FK；导入、历史清洗和物理删除是否仍有需求。

## 13. 审查风险提示

- `_tools/DbSeed/Program.cs` 存在硬编码数据库账号口令。报告不展示口令；应立即改为安全配置并轮换凭据。
- `ISalesOrderApi.cs` 的中文注释当前出现乱码，提示文件编码或历史转换风险。
- 本次未运行构建/测试：任务是只读模型治理，构建会生成 bin/obj 且无法验证未实现业务链；结论来自逐文件源码与脚本审查。
- 审查开始前 `git status --short` 为空；本次只新增本报告文件。
