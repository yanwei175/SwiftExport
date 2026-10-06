# M26-P-REVIEW-01 Excel采购锁与正式创建全链审查

审查时间：2026-10-05 02:31 +08:00。执行B；事实源为主仓库 `D:/Documents/source/CSharp/SwiftExport` 当前源码、主迁移清单与已有结果。主角色为 Reviewer，采用代码审查工作流的只读调用方与测试核对方法；本轮没有待审新增代码 diff，以下是迁移边界审查，不将既有实现问题包装成新回归。授权仅本结果与本人状态页。

## 结论与实施门槛

**采购四查询不能按销售读取切片直接移植并接线。** 同一 `IForExcelPurchaseOrderRepository` 同时服务事务外预览与事务内创建；正式创建还依赖采购参考读取、采购单持久化、命令回执、审计、取号及单位引用冻结。只迁四查询，或以普通 SELECT、Serializable/SSI、现有行 FOR UPDATE 自动替代源 HOLDLOCK，都没有完成源范围保护验收。

最小下一步是主控先冻结采购主数据读写协议、快照取得时点与冲突返回边界，再放行“四方法独立 PG 仓储及独立测试、保持原接口、不改服务/DI”的实现切片。该切片只可标仓储交付；正式创建须随 M17/P04/M13 及 P07 全链验收。协议未冻结前可整理投影和比较测试清单，本轮不建议交付去掉锁语义的生产实现。

本轮未执行构建、测试、数据库语句；没有改业务代码，没有获取源库执行计划或锁观测。下文的范围是源码逻辑谓词，不能据此声称源库实际总能获得某一精确索引键范围锁。物理锁范围、锁升级、等待和死锁均待真实双连接观测。

## 真实调用链与事务

1. VSTO `PurchaseOrderImportViewModel.Submission.cs` 调用检查 API；`ForExcelPurchaseOrderService.CheckAsync` 先鉴权和结构校验，再检查客户、采购供应商、客户型号候选、单位及客供件送达供应商。检查服务不创建事务。四查询透传同 scoped Session 的可空 Transaction；普通请求没有跨四查询的显式事务。每条 HOLDLOCK 的保护不能延续到另一 HTTP 请求，也不能把整个预览当作一致数据库快照。
2. 客户端保存另调独立采购 Create API。保存请求使用固定 CommandId；结果不明时重试同 ID，编辑版本用于判断旧预览是否失效。预览与创建没有共享连接或事务，CanSave 不能充当服务端资格证明。
3. `ProcurementOrderDraftService.CreateAsync` 先由 `ProcurementOrderCommandSupport` 检查创建权限，调用 `BusinessCommandExecutor`，明确使用 Serializable。可选 `ICommandTransactionPreparation` 先取得同 CommandId 租约，随后开启事务，LockAsync/FindAsync 读取命令回执，再进入创建 action。
4. 首次执行在事务内 ValidateParties，重新读客户和供应商，校验单头；批量 MatchProducts；按提交行顺序读取当前产品、产品候选、单位、客供件送达供应商。客户和供应商重复读取仍在该事务。非标行不关联产品，但单位必须重新确认。
5. 标准行如果有 CustomerSku 或 SelectedCustomerProductId，重新解析当前客户型号候选、核对所选关系及 ProductId，LinkSku 不一致须明确确认；不能使用预览缓存代替。旧手工请求没有这两个字段时保留原路径，不新增客户型号必填规则。
6. 服务端生成单头/行 UID 和行号，独立来源 OriginKind=2，ExecutionRequestId=CommandId；计算金额和商业快照，取采购单号，插入单头/行，插入成功审计，执行器插入命令回执后提交。独立创建不写需求执行关联。
7. 已有相同回执时校验用户、命令类型、目标、指纹及结果格式，提交后回放旧结果，不再次校验主数据、不再次插入单据或成功审计。权限检查在进入执行器之前仍执行。这个语义必须保留。

**快照时点门槛：** PG Serializable 下，action 前的回执查询已可能取得事务快照。不能在 MatchProducts 才加互斥并假定随后读取会看到等待期间刚提交的数据。即使在 BeginTransaction 后安排读锁，也须证明它早于实际首次快照读取；现有命令租约仅按 CommandId 互斥，不保护不同 CommandId 共用的客户/单位/产品资料。

## 四查询的逻辑范围与未覆盖事项

源码：`SwiftExport.Infrastructure/Repositories/ForExcel/ForExcelPurchaseOrderRepository.cs:15–54`，接口位于 Core/Interfaces/ForExcel。

| 查询 | 源谓词、返回和锁对象 | 空范围/范围变化及边界 |
|---|---|---|
| FindCustomers | Customer HOLDLOCK；IsDeleted=0；UPPER(LTRIM(RTRIM(CustomerCode))) 等于 C# Trim/Upper 后参数；Uid 排序，返回全部候选及 IsActive | 无候选、新增同等价编码、旧编码改入/改出、恢复软删都会改变结果；不能只锁当前返回的 UID。仓储不自行过滤停用，服务要求唯一有效客户。 |
| FindSupplier | Supplier HOLDLOCK；Uid 与 IsDeleted=0；返回当前资格及代码、名称 | 缺失/删除身份的空范围；创建同身份或恢复/删除/停用、清空代码名称影响资格。没有锁所有供应商，也不要求供应商产品关联存在。 |
| MatchProducts | CustomerProduct HOLDLOCK 按 CustomerId、IsDeleted=0、ASCII SQL trim/upper 的 CustomerSku IN 集合；LEFT JOIN Product HOLDLOCK，p.IsDeleted=0；按 CustomerSku、cp.Uid 返回全部关系 | 空数组直接返回；非空但零命中的客户型号范围、同型号新增第二关联、移入/移出、SkuId 改绑、关联恢复/停用、缺失产品补建及产品资格变化都需核对。不能只锁已选关联，不能把 LEFT JOIN 改 INNER JOIN 丢掉不可用候选。 |
| FindUnit | SysDictData HOLDLOCK；DictCode=Unit、IsEnabled=1、IsDeleted=0、upper(SQL ASCII trim DataNameEn)=规范化参数；恰好一行才返回规范单位 | 0/1/多行均有业务含义；新增等价英文单位、改名/改 DictCode、启停、删除恢复都影响匹配，锁现有一行不能保护新增歧义。不能用 LIMIT 1 或 DISTINCT 消除歧义。 |

MatchProducts 的 IsUsable 检查关系/产品启用、采购供给类型、物料类型属于成品/零件/客供件及非空 SKU；单位可用性由后续查询确认。LinkSku 优先关系非空值再取产品值；MainImage、Unit、MaterialType 来自产品。保留原 NULL、原存储文本和排序，不额外去重、修复孤儿或加业务资格。

正式创建使用 `ProcurementReferenceReadRepository`：客户/供应商/产品按 UID HOLDLOCK，软删谓词为 ISNULL(IsDeleted,0)=0，不能与导入的 IsDeleted=0 合并为一个默认规则。产品候选还读取 ProductColor HOLDLOCK、同客户同产品的启用 CustomerProduct TOP(1)、同供应商同产品的 SupplierProduct TOP(1)，均按 Uid 选取；供应商关系没有新增 IsActive 过滤。其 LinkSku 默认来自产品，与导入匹配的关系优先规则不同。单位查询同样返回恰好一行，接纳 NULL 软删字段。**正式链锁域因此多出颜色和供应商产品关系，不止四查询的五张表。**

## 实际写入方、字段责任与现有顺序

以下是实际应用可达写入责任，不是按模块名推断隔离。顺序指显式语句/服务顺序，不保证联表执行计划的物理锁次序。泛型仓储 SQL 由实体元数据生成，不能只搜索字面 `UPDATE dbo.Customer`。

| 写入入口 | 会影响读取的责任 | 当前显式顺序/协议缺口 |
|---|---|---|
| CustomerService、SupplierService 的普通 CRUD/BatchSave | 新增、编码/名称更新、软删或请求允许的物理删除；普通更新列排除 IsActive | 批量验证后自有默认隔离事务，新增→更新→删除，各组按提交顺序；没有共同主数据范围互斥协议。不能因 IsActive 不可普通更新就漏掉其他资格字段。 |
| ExecutionEligibilityService / Repository | Product、Customer、Supplier 的 IsActive 专用写入，按 ExpectedVersion 更新、Version+1及成功审计 | 同 CommandId 审计事务前准备→Serializable→用户/角色/权限 HOLDLOCK→目标 UID UPDLOCK,HOLDLOCK→版本更新→审计。P04-E生命周期测试不等于授权/采购范围竞争验收；主控仍持有 M13 授权锁冻结。 |
| CustomerProductMaintenanceService + CustomerProductRepository/泛型仓储 | CustomerId、CustomerSku、SkuId、LinkSku、IsActive、删除及恢复，改变候选集合或所选关系资格 | 校验与重复检查后默认事务，新增→更新→删除按提交顺序；同请求规范化查重与数据库唯一键不是同一个可自动互换的比较域。 |
| ProductMaintenanceService + ProductRepository | Sku、Unit、MaterialType、SupplyType、LinkSku、颜色、描述配置、删除及新增；普通更新排除 IsActive、DefaultSupplierId | 实际为 Serializable；先分组验证，更新/删除目标产品 withUpdLock，读取 SKU、颜色/供应商/仓库、Unit 字典及受影响 BOM 事实，再新增→更新→删除。不是全局稳定 UID 顺序；更新路径产品在单位读取之前，不能另设单位→产品的新协议而忽略该次序。 |
| SysDictDataService + 字典仓储 | Unit 明细 DataNameEn、DictCode、IsEnabled、删除/恢复/新增 | 默认事务内先按提交字典编码锁 SysDictType，再锁目标明细，新增→更新→删除。现有 PG GetLockHint 是 FOR UPDATE，只保护找到的行。需包含旧/新单位等价域，新增无目标 UID 的范围不能靠明细行锁完成。 |
| SysDictTypeService | 类型目录维护，不能直接当成 Unit 明细资格字段写入者 | 先锁类型目录，再查引用明细；有明细时禁止改编码/删除，系统 Unit 类型另有保护。须与字典明细的类型→明细顺序保持一致，不把它错误描述成级联改名明细。 |
| ProductMasterMaintenanceService → SyncFromProductMasterAsync | 主型号关联产品的 MasterSku、描述/Features、DefaultSupplierId 与 Version；不写 Unit/SupplyType/MaterialType/IsActive | 按用例先主型号/产品及参考资料，再同步 Product；是产品快照与锁冲突参与者，不能按主型号模块独立处理。 |
| ProductDefaultSupplierService | SupplierProduct 身份关系、产品及供应商检查后更新 Product.DefaultSupplierId/Version | 自有事务，关系→产品→供应商→Product UPDATE；不直接改变 MatchProducts 的资格谓词，但会竞争同产品行。 |
| SupplierProductMaintenanceService、ProductColorService | 前者关联身份/SupplierSku/删除影响正式候选默认值；后者颜色名/删除影响 FinishZh | 供应商关系批量校验产品/供应商后默认事务、按输入写入；颜色普通 CRUD/删除规则。必须纳入创建快照竞争，不能只验导入产品资格。 |

ProductMaintenanceService 的 BOM 编号/有效性标记维护已计入产品写入入口；BomService 的组成保存本身不能据此描述为产品资格写入。其共享事实读取仍可能与产品行锁竞争，应覆盖与产品维护的锁顺序，但不为本迁移新增全局业务锁。

普通产品资料、关系资料和资格专用入口共同覆盖“谁能改资格”；数据库管理员、运维脚本及外部直写不在应用协议自动保障内。P06 的工具迁移须独立审计其写入范围；本轮不运行工具或自行改部署限制。

## 引用冻结、唯一性、版本及取消审计

- `EntitySchemaMetadata.cs:59–60` 将 Product.Unit 在 PurchaseOrderLine/InventoryLedgerEntry 引用后冻结。SQL Server 生成触发器在产品单位更新后检查账本及采购行，引用检查为 UPDLOCK,HOLDLOCK。PG `PostgreSqlSchemaGenerator.WriteProtection` 明确标注只检查当前可见引用、并发新增引用协议未完成。不得将评审 DDL 或已有触发器存在当成并发保证；创建采购行与改单位、过账写账本与改单位必须共同验收。
- 源元数据唯一键：未删 Customer.CustomerCode、Supplier.SupplierCode、CustomerProduct(CustomerId,CustomerSku)、PurchaseOrderHead.OrderNo、(ExecutionRequestId,SupplierId)、PurchaseOrderLine(HeadId,LineNumber)。这是代码目标结构，不是本轮验证了真实部署。单位 DataNameEn 没有这个规范化谓词的唯一约束，必须保留歧义返回。
- 单据 CommandId 回执去重与执行请求唯一键不等于客户 PO/BO 业务重复检查。四采购查询没有销售 ExistingOrders 那样的查询；本轮未发现独立创建以 CustomerPoNo/BoNo 阻止新 CommandId 创建，不添加该业务规则。
- 创建不接收主数据 ExpectedVersion 来替代重读；当前 Product/Customer/Supplier 的 Version 投影也不能自行扩大成新客户端并发契约。草稿 Save 的单头/行 ExpectedVersion 及确认状态防线是另一现有用例，原样保留。
- 查询 CommandDefinition 透传 CancellationToken；预览只读。创建在事务准备、业务读写及提交前检查取消；执行器失败尝试回滚并清空 Session.Transaction，先处置事务后释放租约。网络取消、等锁取消、BeginTransaction/提交失败与租约清理需真实链验收。
- 成功审计使用同 CommandId，必须与头/行/回执共同提交；回放不重复成功审计。序列化失败现有分类为 ServiceUnavailable、明确未提交；其他无法确认提交结果返回 OutcomeUnknown。同 ID 重试保持原指纹，不静默自动重试、不降隔离。

## PG 已有实现、验证与运行状态

1. 四查询的 `PostgreSqlForExcelPurchaseOrderRepository` 尚不存在；采购 ReferenceRead 与 DocumentRepository 也未发现 PG 对应实现，当前 DI 注册仍为 SQL Server 三个仓储。不能只换连接工厂。
2. PG 通用会话、命名映射、参数、异常分类、CommandReceipt、BusinessOperationLog、DocumentSequence，以及客户/供应商/产品/客户产品/供应商产品/颜色/字典仓储已有源码。存在实现不等于本调用链的阻塞/空范围协议已验证；通用 withUpdLock 仅 FOR UPDATE，客户产品仓储明确不新增资格锁。
3. 主清单证据：M26销售切片27离线/13真PG通过、未接线；M13候选读取31离线/12真PG通过、未接线。M13首次价格测试误预期已由主控按源/PG decimal(18,4)纠正；旧状态中的11+1不是当前最终结果。执行资格服务44离线及阶段构建由主控登记，完整资格 PG 全链仍未验收。
4. 上述既有证据不覆盖采购四查询、创建所有参考读取、空范围竞争或引用冻结。当前运行 DI 未统一到 PG，P07 待验。结果不标 M26 整体兼容或正式创建通过。

## 公共前置与最小可交付切片

| 前置责任 | 必须冻结/验证的内容 | 边界 |
|---|---|---|
| P03/B10 比较 | SQL ASCII trim、C# Unicode Trim、upper/collation、NULL、原文投影、等价候选集合与唯一索引的一致性 | 只引用既有比较证据，不改全局比较器或把所有空白删掉。 |
| P04/M13 主控 | 不同 CommandId 的主数据读写协议、缺失 UID/等价编码范围、旧/新键获取、稳定顺序、首次快照前准备、与 RBAC/审计命令租约的相容性 | 禁止新全局业务锁；锁已存在行不能替代空范围保护。若需公共接口或服务最小衔接，主控单独授权。 |
| M17采购写入 | ReferenceRead、DocumentRepository、单位引用冻结、审计/回执/取号在同 scoped 会话，完整创建快照和取消 | 不把 M13 查产品候选仓储直接当成正式 ReferenceRead；两者谓词/默认值不同。 |
| P01/P03/P06/P07 | 明确测试实例/库/夹具初始化清理范围，PG 结构验证、真实写入入口覆盖、运行接线 | 独立仓储代码可先交付，数据库测试与运行接线须主控排单。 |

建议顺序：先用这份源范围清单冻结协议与竞争期望；然后放行独立四方法 PG 仓储及独立测试，明确支持空事务预览和已有事务调用、固定映射/取消/投影不变；主控验收仓储切片后再串联创建链。若公共协议尚未完成，不注册该仓储供创建使用，也不把只读预览受测当成创建完成。

协议选择必须有真实使用场景。按逻辑键互斥若使用现有准备能力，所有相关写入者都须覆盖旧/新键及比较等价域，并证明等待在业务快照之前；未参与的普通写入者会使协议失效。仅后置 advisory lock、单方加锁或仅依赖 SSI 不满足这项证明。整表共享锁会扩大跨模块阻塞并改变死锁域，不作为默认最小方案，也不据本结果创建全局业务锁。冲突时允许什么失败和用户重提边界由主控按原业务语义冻结。

## 真实 PG 竞争验收建议

使用两个独立物理连接、显式屏障和有界超时，事务 A 为真实创建/检查服务链，B 为真实维护服务或对应同协议仓储；观察真实等待/服务器锁与最终数据。不能以 sleep 猜测、同连接两个 Task、仅静态 SQL 断言或只测最终计数代替。每组清理只限夹具登记的身份/单据；记录租约、事务和连接无残留。

| 场景 | 屏障及断言 |
|---|---|
| 预览与正式创建之间改变资料 | 预览成功后 B 提交客户/供应商停用、关系停用/改绑、产品停用/改供给类型、单位停用；A 新请求创建必须按当前事实拒绝或进入明确冲突，不凭 CanSave 插入。 |
| 已有资格在创建期间变化 | A 取得资格后停在插单前，B 修改相关客户/供应商/产品/关系/单位；验证冻结协议规定的等待/终止及完整提交结果，反向先持写锁再启动 A 同样覆盖。 |
| 客户型号空范围与新歧义 | A 查询缺失型号或单一候选，B 新增、恢复、改入同一客户等价型号；覆盖旧→新型号与跨客户移动。无命中、单命中、多命中分别验；不能只锁所选 UID。 |
| 单位0/1/多行范围 | B 新增第二条等价英文单位、改 DictCode 进 Unit、恢复/启用原不可见行，或从匹配范围移出；A 保留恰好一行规则，协议覆盖空集合。 |
| 缺失关联产品/供应商身份 | A 读到缺失或软删的目标，B 补建同 UID/恢复；验证拒绝/等待边界和 LEFT JOIN 不可用候选保留，不自动改业务结果。 |
| 创建商业默认值竞争 | B 修改 SupplierProduct.SupplierSku、ProductColor.ColorNameZh、客户产品关系及 ProductMaster 同步描述；A 输入 null 使用默认、空串明确留空分别验证快照，不能把用户商业输入回写主数据。 |
| 单位冻结对新增事实 | A 创建引用产品的采购行、B 更改 Product.Unit；再覆盖账本新增引用。双向持锁顺序及旧快照均测，最终不得存在已引用产品可任意改单位或采购单位/产品单位失配的漏保护。 |
| 多行/多供应商顺序与死锁 | 两个创建请求产品/单位/送达供应商输入逆序；并与批量产品维护、单位目录维护、默认供应商、主型号同步竞争。记录稳定顺序、死锁分类、整体回滚，不吞错自动重试。 |
| 命令回执/审计/取号 | 同 CommandId 两请求只有一份业务结果；同 ID 改内容冲突；不同 ID 共享主数据不被命令租约误判安全。头/行/审计/回执全部同成败，回放不重读资料或重复审计，失败取号边界沿用原规则。 |
| 快照前后与取消 | B 在 A 命令回执查询后、资料加锁前提交改变；证明 A 的结果符合冻结冲突协议。等待准备租约、资料锁、查询执行及提交前取消分别检查事务/租约/连接清理。 |

已有 SQL Server `ForExcelPurchaseOrderSqlTests` 以独立 LocalDB 夹具覆盖预览→创建→读取→回放、关系被停用及伪造选择/产品拒绝等功能；它们不是上述跨连接竞争证明。未运行的 PG 新场景不能登记通过。

## B10 比较证据及交付范围

引用本人状态及主控 M26 已有矩阵：源 Chinese_PRC_CI_AS 下短文本与尾 U+0020/U+202F/U+205F/U+3000 比较为真；Tab、NBSP、U+2000/2001/2002/2003/2009 为假。冻结 PG ICU 中除 Tab 外列举空白与 ASCII 空格等值，未修剪的尾空白对短文本比较另有差异。采购谓词还叠加 SQL ASCII trim 和调用方 .NET Trim，不能直接把矩阵转成统一 Trim 规则；关系查询后 Resolve 用 C# Normalize 筛选同型号，必须联合验证 SQL候选与C#选择。

本轮只新增本结果、更新本人状态；未修改任何业务源码、共享文档、旧结果或比较器；未构建、测试、执行数据库、提交、推送、部署或创建代理。已有未提交源码和其他轮次成果保留。待主控读取结果并冻结公共协议后再决定实现分派。
