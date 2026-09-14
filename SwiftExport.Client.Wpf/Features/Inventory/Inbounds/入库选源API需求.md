# 入库选源 API 交接

2026-09-14：本批已实现非过账后端与契约。Picker 既有 Refit 已有对应服务端；Manager 新增 Refit，但页面查询/保存按钮尚未接线。数据库未连接、未迁移，不能把代码构建通过视为数据库部署完成。不得复活旧 Workspace 来源语义或用假数据返回成功。

## 查询协议

- `IInboundSourceApi.SearchAsync`：`POST /api/inventory/inbound-sources/search`。
- 请求为 `InboundSourceSearchRequest`：`DocumentType` 必填；不接收仓库条件；采购类型时 `PurchaseCategory` 必填，正常采购 0、客供件采购 1，客户端默认客供件。`SupplierId` 可空，筛选采购头供应商。
- `Keyword` 搜索采购单号、BoNo、Sku、LinkSku；页码从 1 开始，客户端每页 50。后端限制页大小，不能一次返回全部采购行。
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
- 过账失败应提示用户修改草稿，不静默换来源、仓库或数量；过账后端仍不属于本批实现范围。

## Manager 非过账 API（本批后端实现）

基址为 `/api/inventory/inbounds`，Refit 为 `IInboundManagerApi`。

| 方法 | 路由 | 输入 | 返回数据 |
|---|---|---|---|
| POST | /search | InboundManagerSearchRequest：Keyword、DocumentType、PageIndex、PageSize | 分页 InboundManagerListItemDto |
| GET | /{uid} | 入库单 Uid | InboundManagerDetailDto：Header、Lines |
| POST | /drafts | CreateInboundDraftCommand：CommandId、Draft | InboundDraftCommandResult |
| PUT | /drafts/{uid} | UpdateInboundDraftCommand：CommandId、ExpectedVersion、Draft | 同上 |
| POST | /drafts/{uid}/delete | DeleteInboundDraftCommand：CommandId、ExpectedVersion、Reason | 同上，IsDeleted=true |

- 全部返回 `ServiceResult<T>`，业务阻断在 Message 明确说明；服务端验证真实用户的 `Module.Inventory.Access`，不把 UI 按钮状态作为权限。独立入库操作权限尚未新增，过账不因此获得授权。
- 每次保存一张单，头行及日志同事务；多张单由调用方逐单处理，不承诺跨单原子性。请求限 1–500 行，数量为正数，最多四位小数。同单来源不重复。
- Draft 只含 DocumentType、WarehouseSiteId、ReceiptDate、Remark 和 Lines。行输入只有 Uid、SourceLineId、Quantity。新增行 Uid 为空；已有行保持入库行 Uid，省略的已有行软删除。已有行不允许换来源，应删旧行、加新行。
- 已保存单据类型固定；头版本保护完整明细集合。保存不比较采购版本、不扣剩余额度；新增行只验证可定位的采购、产品、单位。已有行保留历史产品快照。最终采购状态和累计容量必须在未来过账重新校验。
- 仅活动、未过账、人工且非 PackingList 触发单可修改/删除；即使单头状态异常未过账，发现正式库存流水仍拒绝。删除保留头行及日志，不物理删除。
- 当前编号为 `IR-{入库Uid的32位格式}`，由服务端建立，不接收客户端编号。
- 重试复用 CommandId 和完整内容；更新/删除也复用最初 ExpectedVersion。同键不同内容、不同操作或不同用户冲突。响应不确定时不要新建 CommandId。
- 来源版本仅为查询当时版本，不入库持久化；详情里的来源单号、BoNo、LinkSku、图片和剩余量为查询投影，不是销售/需求快照。
- 正式净收货按采购来源行汇总有符号数量，包含 Posted 与 Reversed 原事实及相应反向单，排除草稿。历史枚举 11/12 或无法证明采购行的正式记录会阻断 Picker；不得把漏算历史当零收货。反向结构上线前仍须联合验收该口径。

## 部署与验证边界

- 以当前实体 `dbo.InboundDocument/InboundDocumentLine` 为目标；不恢复 `PurchaseReceiveHead/Line` 映射，不自动迁移历史身份。
- 先人工审阅并运行 `SwiftExport.Infrastructure/Migrations/031_入库非过账接口_Preflight.sql`；本批未执行任何数据库脚本。
- WebApi、WPF 项目构建通过；7 项草稿服务隔离单测通过。全量 Application 测试被需求模块两个 required 成员初始化错误阻断；未做真实 SQL Server、HTTP 或 WPF 联调。
- Manager 前端接线、数据库结构迁移、采购旧 SourceType/SourceUid 查询迁移、过账/反向、Trace、PackingList 自动入库仍待后续实施。
