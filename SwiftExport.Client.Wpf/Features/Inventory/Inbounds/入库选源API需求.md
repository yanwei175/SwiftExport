# 入库选源 API 交接

## 2026-09-15 人工入库整单反过账（本批）

- 仅活动、人工、已过账且非 PL 触发的采购入库；原单原行保留，反过账后仍只读，不生成负数入库单。不加入客户归属、消耗分配、库存池或预留，不检查后续出库；库存按仓库＋产品合计扣减，余额不足整单阻断。
- 新增 `POST /api/inventory/inbounds/{uid}/reverse-preview`，请求 ExpectedVersion，返回 InboundReversePreviewDto（原流水 Ledgers、汇总 StockImpacts、结构化 Blockers、CanReverse）。
- 新增 `POST /api/inventory/inbounds/{uid}/reverse`，请求 CommandId、ExpectedVersion、Reason（Trim 后 1–500 字），返回 InboundReverseResult（Uid、Version、DocumentNo、PostingStatus、反过账审计信息）。预览与提交要求 Module.Inventory.Access 和独立可分配 Inventory.Inbound.Reverse。
- 先命令重放，再按采购来源、入库头行顺序锁定；原流水与明细一一证明后调用现有库存 ReverseAsync。采购关闭或主数据停用不重新取消原事实的纠错资格；历史仓库和单位规则沿用库存内核。库存、状态、审计、日志同事务，失败全量回滚。
- Manager 启用当前单反过账预览和原因弹窗；确认冻结命令，未知响应刷新原命令重试；成功但详情失败只重读。成功只回填当前单，不丢其他编辑。真实退货不使用此入口。
- 净收货：Posted 贡献原行数量；已证明完整整单反向的 Reversed 贡献零。入库选源/详情/容量、采购管理/跟进数量查询同步适配。共享只读历史门禁拒绝旧负数单、无明细或不完整反向，不自动迁移。历史事实存在性安全检查不删除。
- 需求既有查询只累计 PostingStatus=1，不需要因本次状态归零改分摊；其旧 DemandExecutionLinkId 关联问题单独记录，不在本批臆造按 Link 分配规则。
- 专项隔离测试 100 项通过（入库、出库反过账、库存内核、入库 WPF 会话）；WebApi/WPF 构建通过。全量 Application 测试被需求旧类型缺失阻断，WPF 全量测试在获准重试后确认被旧需求 ViewModel/DTO/API 类型缺失阻断；不修改无关测试。真实 SQL 锁、事务、死锁及桌面联调未验证，未连接数据库。
- 交付 `036_人工入库反过账_Preflight.sql`，仅只读核对，未执行；保持原入库行正数约束，不新增表。旧段落与本节冲突时以本节为准。

2026-09-14：已实现采购入库正向过账、只读预览与其他草稿提醒；Manager 保留查询、分页、详情、保存和草稿软删除。未开放售后、生产、自动入库或出库检查。数据库未连接、未迁移，不能把构建通过视为部署完成，不恢复旧 Workspace。

## 查询协议

- `IInboundSourceApi.SearchAsync`：`POST /api/inventory/inbound-sources/search`。
- 请求为 `InboundSourceSearchRequest`：`DocumentType` 必填；不接收仓库条件；采购类型时 `PurchaseCategory` 必填，正常采购 0、客供件采购 1，客户端默认客供件。`SupplierId` 可空，筛选采购头供应商。
- `Keyword` 搜索采购单号、BoNo、Sku、LinkSku；页码从 1 开始，客户端每页 50。后端限制页大小，不能一次返回全部采购行。
- 可选 `CurrentInboundDocumentId` 只用于排除本单的其他草稿提醒，不作为候选范围限制。候选和详情行返回 `OtherDraftCount`、`OtherDraftQuantity`；统计同来源其他活动未过账草稿，不扣可收量、不限制保存。
- 返回 `ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>`，每项严格对应一条采购行，不按 Link 展开。未实现的生产等类型明确返回失败，不能落入采购分支。
- 供应商下拉只消费当前会话全量缓存，不建立“有效采购供应商”接口，也不按有无采购记录过滤缓存。

选源与收货仓库无关：调用方不传目标仓库，候选结果不包含仓库字段，Picker 不展示仓库列。物料推荐仓库不作为准入条件，也不自动回填 Manager。过账仅校验单头所选仓库的业务有效性，不要求匹配推荐仓库。

## 字段与准入

| 字段 | 数据来源或含义 |
|---|---|
| DocumentType | 采购入库 10，不是正常/客供件分类 |
| SourceLineId、SourceVersion | PurchaseOrderLine.Uid、Version；查询快照，不代表锁定额度 |
| PurchaseOrderId、SourceNo、LineNumber | 采购头身份、采购单号、采购行号 |
| BoNo | 采购自定义单号，不从销售或需求链提取 |
| ProductId、Sku、LinkSku、Description、Unit | 采购产品身份与确认快照；单位不擅自默认 PCS |
| ImagePath | 只读图片路径，符合 CachedImage 的本地/UNC 路径要求 |
| SupplierName | 采购头供应商显示名称，不是仓库所属供应商 |
| OriginalQty | 采购 Quantity（物理列 QTY） |
| RemainingReceivableQty | 采购数量减同采购行正式入库及反向有效净额，不扣草稿、不读需求覆盖缓存 |

分类使用采购行 MaterialType 快照区分客供件/非客供件，不以有无 Link 或供应商推测。历史身份、分类、单位无法证明时不能伪装成合法可收来源。

准入检查采购已确认且活动、未关闭/作废，有合法产品单位，正式剩余量大于零。无需求/无 Link 采购同样可收。先按采购行聚合净入库量，再关联展示资料，避免多 Link 倍增数量。历史入库 SourceUid 可能保存 Link/Allocation，后端不得直接当作采购行 Uid 使用。

## 选择与并发边界

- 选择不使用更新锁、不预占、不判断版本冲突。UI 的查询取消和过期响应保护仅避免结果串页。
- 每页确认一次。已有勾选时查询或翻页提示先确认或显式清空，不静默丢失；取消关闭不回填。
- 确认返回独立快照，Manager 不二次获取详情，不比较来源版本。重复来源跳过，不覆盖已填数量。
- 单头已有明细时不改类型，草稿仓库仍可独立选择或修改。新明细本次实收初始 0，用户填写。
- 保存核对入库单自身版本；过账在同一事务最终核对采购状态、身份、单位、目标仓和同来源合计的剩余额度，并防重复命令。采购版本不变也可能已有其他收货，版本不能替代数量校验。
- 过账失败提示具体阻断，不静默换来源、仓库或数量；正式提交重新校验，不信任预览快照。

## Manager API（已实现）

基址为 `/api/inventory/inbounds`，Refit 为 `IInboundManagerApi`。

| 方法 | 路由 | 输入 | 返回数据 |
|---|---|---|---|
| POST | /search | InboundManagerSearchRequest：Keyword、DocumentType、PostingStatus、PageIndex、PageSize | 分页 InboundManagerListItemDto |
| GET | /{uid} | 入库单 Uid | InboundManagerDetailDto：Header、Lines |
| POST | /drafts | CreateInboundDraftCommand：CommandId、Draft | InboundDraftCommandResult |
| PUT | /drafts/{uid} | UpdateInboundDraftCommand：CommandId、ExpectedVersion、Draft | 同上 |
| POST | /drafts/{uid}/delete | DeleteInboundDraftCommand：CommandId、ExpectedVersion、Reason | 同上，IsDeleted=true |
| POST | /{uid}/post-preview | InboundPostPreviewRequest：ExpectedVersion | InboundPostPreviewDto |
| POST | /{uid}/post | PostInboundCommand：CommandId、ExpectedVersion | InboundPostResult：Uid、Version、DocumentNo、PostingStatus |

- 全部返回 `ServiceResult<T>`，服务端验证真实用户的 `Module.Inventory.Access`。预览和过账还必须具备独立可分配权限 `Inventory.Inbound.Post`；普通用户不自动获得，管理员沿用现有机制。页面不通过中文消息判断错误类别。
- 每次保存一张单，头行及日志同事务；多张单由调用方逐单处理，不承诺跨单原子性。请求限 1–500 行，数量为正数，最多四位小数。同单来源不重复。
- Draft 只含 DocumentType、WarehouseSiteId、ReceiptDate、Remark 和 Lines。行输入只有 Uid、SourceLineId、Quantity。新增行 Uid 为空；已有行保持入库行 Uid，省略的已有行软删除。已有行不允许换来源，应删旧行、加新行。
- 已保存单据类型固定；头版本保护完整明细集合。保存不比较采购版本、不扣剩余额度；新增行只验证可定位的采购、产品、单位。已有行保留历史产品快照。最终采购状态和累计容量在正式过账重新校验。
- 仅活动、未过账、人工且非 PackingList 触发单可修改/删除；即使单头状态异常未过账，发现正式库存流水仍拒绝。删除保留头行及日志，不物理删除。
- 新建编号由现有 `IDocumentNoService` 生成，格式为 `IR{收货日期yyyyMM}{至少4位流水}`，如 `IR2026090001`；取号在保存事务内、幂等检查后执行。不接收客户端编号，不重编已有单号。
- 重试复用 CommandId 和完整内容；更新/删除也复用最初 ExpectedVersion。同键不同内容、不同操作或不同用户冲突。响应不确定时不要新建 CommandId。
- 来源版本仅为查询当时版本，不入库持久化；详情里的来源单号、BoNo、LinkSku、图片和剩余量为查询投影，不是销售/需求快照。
- 正式净收货按采购行汇总，仅 Posted 贡献数量；完整证明的 Reversed 归零，旧负数反向单阻断。历史枚举 11/12 或无法证明采购行的正式记录会阻断 Picker；不得把漏算历史当零收货。反向结构上线前仍须联合验收该口径。

## 正向过账与提醒

- 仅当前已保存、完整加载、无未保存修改的人工采购入库单可提交；不自动保存、不批量过账。预览展示来源、单位、本次数量、采购原数量、正式净额、剩余量及结构化 Blockers；StockImpacts 按仓库和产品展示当前、增量及预计余额。预览不写业务数据、不预占、不保证提交结果。
- 对话框明确提示“过账后暂不支持修改或撤销”，有阻断项不能确认。确认冻结 CommandId 和 ExpectedVersion；成功只接纳当前单，即使筛选未过账也暂时保留，不丢其他单编辑。
- 采购专用校验不依赖需求、Allocation 或 Link。正常、客供件、独立采购共用入口；产品/供应商停用本身不取消收货资格，仍核对产品身份、单位和目标仓库。其他入库类型明确不支持，不设空实现。
- 幂等命令检查早于单据状态；先发现来源，按稳定顺序锁采购头行，再锁入库头行，重验版本与来源集合。在来源锁内重新计算正式净额，然后调用 `IInventoryPostingService.PostAsync`；状态、版本、人员时间、库存与日志同事务提交。
- 库存流水仅关联 `InboundDocument` 及稳定入库行身份，再通过入库行追溯采购；不直接关联采购/需求，不重新取单号。入库仓储不更新库存余额、不插库存流水。正向入库不检查后续出库。
- 跨草稿重复来源只显示“另有 X 张未过账草稿，合计 Y”；预览重新读取提醒。两张采购数量 100 的草稿各填 100 均可保存，首张正式入库后第二张必须容量阻断。
- 过账同样复用当前窗口的未知结果恢复；刷新重试原命令，不能新建命令绕过。写成功但详情失败显示已过账、只重试详情。版本冲突不换版本覆盖；不提供跨重启恢复。

## 部署与验证边界

- 以当前实体 `dbo.InboundDocument/InboundDocumentLine` 为目标；不恢复 `PurchaseReceiveHead/Line` 映射，不自动迁移历史身份。
- 部署前审阅 `031_入库非过账接口_Preflight.sql` 及 `032_入库正向过账_Preflight.sql`，确认字段、数量正数约束、唯一索引、序列及历史来源；本批未执行任何数据库脚本，不调整负数约束。
- WebApi、WPF 构建通过，入库与库存内核针对性测试 63 项通过，包括可控来源锁并发替身、回滚、重放、预览变化、窗口恢复和预览弹窗 STA 绑定。另一个既有 Picker 模板测试因夹具缺少 `DataGridRowHeightMedium` 资源失败，保留原断言、不修改页面资源规避。替身不能证明真实 SQL Server 锁、事务或死锁行为；未做数据库、HTTP 或完整桌面联调。
- Manager 查询侧已接线：筛选输入不自动请求；翻页使用上次成功查询条件；未保存变更先确认，查询失败保留编辑；摘要未加载详情前只读，单据切换保留已编辑明细。
- Search 已包含 PostingStatus 并有服务端过滤；默认未过账，点击查询应用，不仅筛当前页。
- 全量 WPF 测试仍因既有 API Runner、Dialog 和采购批量确认测试编译错误阻断。本次临时隔离入口只选入库测试源，不删除或修改无关测试；NuGet 漏洞数据获取因网络受限产生警告。
- 启用前阻断：`ProcurementOrderRepository.HasDownstreamFactsAsync` 仍通过入库 `SourceUid/SourceType` 判断下游；当前入库事实已是 `SourceLineId` 加头 DocumentType，可能导致取消确认查询 SQL 失败或无法正确保护。实际采购命令已使用采购头更新锁，但下游事实查询需由采购任务独立迁移并联调。本批未修改采购模块。
- 数据库迁移、部分红冲/自动纠错、Trace、售后、生产及 PackingList 自动入库未实现，不因本批构建通过宣称部署可用。

## Manager 保存、删除与恢复（已接线）

- 保存提交当前页全部已修改单据（含仅改明细），先提交原生 Grid 编辑并校验所有待保存单，再按头集合顺序逐单发送；失败立即停止，已成功单据不回滚，其他编辑保留。
- 产品、单位和采购来源显示字段不回传。新增行映射为空 Uid，已有行保留 Uid，删除行从完整有效行集合中省略。前端不再用 RemainingReceivableQty 快照限制保存数量。
- 删除勾选单据，无勾选时取当前单。存在只读、未加载或版本冲突对象时整批先阻断。本地新增确认后移除；持久化单填写共同删除原因后逐单软删除，明确成功才从页面移除。
- 三个入库写接口按用户批准不再使用按“用户＋接口”冷却的 PreventDuplicateRequest，避免连续保存不同单据被误限流；客户端 Busy 和服务端命令锁/日志幂等仍保留，其他模块过滤器不改。
- 普通失败使用 ServiceResult.Error 的公共代码（VALIDATION、VERSION_CONFLICT、FORBIDDEN 等），保留 Message 与可选 Field。命令忙为 INBOUND_COMMAND_BUSY，结果未知为 INBOUND_OUTCOME_UNKNOWN。客户端不按中文消息判断重试类型。
- 结果未知时保留已发送的 CommandId、版本和完整请求，锁定该单；“刷新”对当前单重试原命令。首次进入未知后，后续权限或传输失败也不解除待确认状态；恢复前禁止替换列表和普通关闭。不支持程序崩溃或强制退出后的自动恢复。
- 写成功但详情失败时已经保留服务器身份，单据保持锁定；“刷新”只重试详情，不重发创建。保存和删除成功不整体重查列表，总数仍标明为上次查询快照。
- 版本冲突不换用新版本覆盖；“刷新”先确认放弃当前冲突单修改，再读取该单，其他草稿编辑不受影响。
