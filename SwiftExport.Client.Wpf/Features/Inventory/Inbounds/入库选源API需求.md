# 入库选源 API 交接

本批只实现客户端与契约，后端未实现。不得复活旧 Workspace 来源语义或用假数据返回成功。

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
- 过账失败提示用户修改草稿，不静默换来源、仓库或数量。保存及过账后端不属于本批实现范围。
