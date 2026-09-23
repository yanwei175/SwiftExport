# PackingList 前端契约与后端交接

> 文档归档于 Shipping。当前入口见 [Shipping 文档索引](../README.md)，接口现状见 [API 索引](PackingList接口索引.md)，后续方案见 [第三阶段计划](PackingList第三阶段实施计划.md)。本文保留历次设计与实施记录；较早的“未接线／SQL 未执行”等描述不代表当前状态，最新验收摘要以索引为准。

> 头部 ShippingMark 已移除，箱唛仅使用行 Marks。实体、头读写契约、UI 和仓储 SQL 已同步；数据库列由用户手动删除，本次不执行数据库操作。

## 2026-09-17 装箱计算与头地点更新（优先于下文旧规则）

- 出货地点 ShippingLocationSnapshot 移至 PackingListHead、HeaderDto、DraftHeaderInput 和 HeaderModel；Detail 头部编辑。Line 的地点及三个 Warehouse 字段均移除，不影响库存选仓。
- Qty、PcsPerPackage、NwPerPackage、GwPerPackage、CbmPerPackage 在草稿可编辑。箱数实时等于 Qty / PcsPerPackage，按四位小数 AwayFromZero；取消 Ceiling 和手工覆盖箱数。未知每箱数量时箱数为空；未知单箱指标时对应小计及合计为空。小计=箱数×单箱指标，四位小数。
- 写请求的 PackageCount 只保留兼容字段，后端不采信；后端复核手工包装值并重算箱数及小计，幂等指纹包含包装输入和头地点。不重新覆盖成来源默认包装，来源默认值仅用于选入时初始化。
- 序号前“拆尾箱”按钮仅对可编辑草稿、正数量、正每箱数量且有余数的行可用。105/20 拆为原行100/20/5箱，下方新行5/5/1箱；新行来源相同、Uid独立、版本为0，保留单据文本和产品快照，清空净重/毛重/体积等待填写。整除不拆。原本不足一箱时就地转成1箱，不建立零数量行。
- 拆分后重新编号，草稿整单保存仍复核同来源总量；失败保留编辑，撤销恢复服务端快照。
- 改用迁移脚本 SwiftExport.Infrastructure/Migrations/PackingList头地点与行仓库清理.sql，不再执行旧行地点增量脚本。先备份再手动部署；脚本没有执行。同头多个旧地点或头行冲突会阻断；旧仓库列删除后仅能从备份恢复。历史生成基线未改，新实体元数据已更新。
- 拼箱暂未实现：建议后续人工选行建立箱组，共享箱号，包装毛重和体积归箱组只计一次，产品行继续保留来源和数量。需单独确定箱组结构、净重口径、拆组和导出规则，不以多行各填1箱代替拼箱。

## 2026-09-17 选源规则更新（覆盖下文历史交接口径）

- 已存在并适配真实 Controller、Service、Repository；尚未连接数据库验证。本次不实现过账和反过账。
- Picker 请求仍为 POST /sources/search，新增 List<string> Keywords。多行文本按换行拆分、Trim、去空、忽略大小写去重；最多 200 项、单项 200 字符。与原 Keyword 合并 OR 匹配来源单号、SKU、关联型号、客户型号、描述和颜色；SQL LIKE 特殊字符转义，全部参数化。不是 Excel 导入或自动勾选。
- 专用查询要求活动 NetDemand，且关联未删除 DemandExecutionLink 的 AllocatedQty > ReleasedQty；与物料回退的 Target 阻断口径一致，不附加 LinkStatus、TargetType 或采购确认条件。在计数、分页之前执行 EXISTS，避免多 Link 重复来源。通用来源交付查询及草稿保存不新增该准入条件。
- 后端 RemainingQty 已扣除有效已过账 PackingList 数量（现有交付投影）；前端按 GrossDemandId 再减当前草稿有效行数量。AvailableQty <= 0 或余额不可用不展示，不扣其他草稿。服务器 TotalCount 保留，本页可能因本地扣量为空。
- 标准、非标都可选；非标以 GrossDemandId 和来源行身份识别，ProductId 可空，ImagePath 保留。
- 列表始终只读。原生 FullRow + Extended 选择支持单选、Ctrl、Shift；IsChecked 镜像选择状态，复选框只展示。选择结果 Qty 默认本地 AvailableQty，随后在草稿调整。不再存在 SelectedQty 编辑器。
- ShippingLocationSnapshot 是可选出货地点文本（200 字符），写入、读取和显示都使用该字段，不调用仓库选择器，不影响库存选仓或自动入出库。WarehouseSiteId/Code/Name 仅保留历史读取兼容，不进入新写请求。
- 发布前手动审核执行 SwiftExport.Infrastructure/Migrations/PackingList出货地点增量.sql；仅新增地点列和备注，首次增列复制旧地点文本，不删旧仓库字段。未执行该迁移时 SchemaReady 检查阻断服务，本次没有连接或修改数据库。

## 历史交付状态

2026-09-15 已接入 Manager、Detail、Picker 的 ViewModel、出货导航、页面模板、Refit 注册及选源弹窗。前端调用真实 IPackingListApi，后端 Controller、Service、Repository 尚未实现，因此尚无可连接真实数据的端到端闭环。保留原页面布局；过账和反过账按钮只显示“待开发”，不发请求、不改变状态或库存。Trace/Export/XLSX 与打印仍为独立后续范围。

遵循 CommunityToolkit MVVM 与 Refit 技能：业务属性使用 public partial 源生成器，头行沿用 BaseLineModel，集合沿用 BaseListModel；结构转换集中在 PackingListMappings，由客户端 MapsterConfig 注册。基类/Grid/实体/数据库均不修改。

新建专用 DTO 的原因：旧 PackingList 契约已清除，不能复用库存收货/出库的凭证语义。BaseDto、PagedRequestBase、PagedResponseBase、ServiceResult 为稳定平台复用。Manager 摘要不复用详情头；Manager 下表与 Detail 同一物理明细概念，复用 PackingListLineDto/Model。

## 页面与绑定交接

| 页面绑定 | Model/DTO | 后端来源或后续行为 |
|---|---|---|
| Manager Search.Keyword/PostingStatus/CustomerId/InvoiceDateFrom/InvoiceDateTo | PackingListManagerSearchModel → PackingListManagerSearchRequest | 分页查询，仅过滤未软删除单据；关键词匹配单号、客户名、物流编号、行发票号 |
| Headers.Items / SelectedHeader | PackingListManagerListModel / PackingListManagerRowModel | PackingListManagerListItemDto，头摘要+发票号汇总 |
| Manager Lines.Items / Lines.SelectedItem | PackingListLineListModel / PackingListLineModel | 选中头 GetAsync 的有效 Lines；必须防旧请求覆盖 |
| Detail Header.* | PackingListHeaderModel / PackingListHeaderDto | PackingListHead 对应同名字段 |
| Detail Header.Lines / Lines | 同一个 PackingListLineListModel 实例 | VM 暴露 Lines => Header.Lines，不创建第二份可编辑集合 |
| Lines.InvoiceNosSummary / Header.InvoiceNosSummary | 有效行 InvoiceNo 派生 | 按 LineNumber、Uid 排序，Trim、Ordinal 去重、忽略空，以逗号空格连接 |
| 行 ImagePath | PackingListLineDto.ImagePath | 来源图片仅展示，不保存 |
| 客户、身份、状态 | DTO快照和稳定Uid | 后端校验；不允许编辑来源/产品身份，客户选择由后续选源协作控制 |

Manager：SearchCommand、RefreshCommand、PageChangeCommand 为查询；AddNewCommand、EditCommand 打开详情；DeleteCommand 删除当前选中草稿。两Grid只读，无保存。新建文档使用新 Guid，系统箱单号在服务端创建成功后返回。

Detail：SaveCommand、CloseCommand、SelectSourcesCommand、DeleteLineCommand、MoveLineUpCommand、MoveLineDownCommand 仅约定，不在本轮实现。BaseListModel.Move 只修改集合顺序和 RowIndex，后续排序命令须同步更新业务 LineNumber；发票汇总按 LineNumber、Uid 排序。切换客户若已有行，必须显式清除/重新选择确认，不能静默将旧客户来源挂到新客户。已过账/冲销不可修改库存事实；InvoiceDate 与库存 PostingDate 无联动。只读汇总不放入写请求。

## API 路由与命令协议

基址 /api/shipping/packing-lists，Refit 方法使用相对路由。下列均为待实现接口。

| 方法 | 路由 | 请求 | ServiceResult 数据 |
|---|---|---|---|
| POST | /search | PackingListManagerSearchRequest | PagedResponseBase<PackingListManagerListItemDto> |
| GET | /{uid} | Guid | PackingListDetailDto |
| POST | /sources/search | PackingListSourceSearchRequest | PagedResponseBase<PackingListSourceCandidateDto> |
| POST | /drafts | CreatePackingListDraftCommand | PackingListDraftCommandResult |
| PUT | /drafts/{uid} | UpdatePackingListDraftCommand | PackingListDraftCommandResult |
| POST | /drafts/{uid}/delete | DeletePackingListDraftCommand | PackingListDraftCommandResult |

请求取消参数均为 CancellationToken；不暴露 post/reverse 占位 API。

- 创建：CommandId、头 Uid、行 Uid 均为非空稳定 Guid，由客户端生成；同意图网络重试不重建标识。新增行 ExpectedVersion=0，已有行正版本。
- 更新：整单 ExpectedVersion + 全部有效 Lines + 显式 DeletedLines。Uid 在有效与删除集合中不得重复/交叉；存量行必须在两集合恰好出现一次，遗漏返回 LINE_SET_MISMATCH，不能默认为删除；外单行拒绝。
- 头行保存同事务，后端重新读取来源/产品/单位/客户、校验草稿状态，校验数量及四位包装精度，统一重算小计和汇总。出货地点可空，缺省包装不能自动变零。允许同 GrossDemandId 多行，不要求 NetDemand/Allocation/采购存在。
- 草稿不占用来源额度、不改变库存；保存时汇总同来源本单数量进行一致性检查，未来过账另做锁后重验。
- 写入输入故意不继承 BaseDto，不提供 IsDeleted、状态、审计、PostCommandId、图片和小计字段。UI 不提交其查询 DTO。服务端也不得使用宽松反射绑定 Entity。
- 幂等日志由后端另行设计：相同 CommandId+规范化内容返回首次成功结果并 AlreadyApplied=true；内容不同返回 IDEMPOTENCY_CONFLICT，不因单有CommandId就宣称已实现幂等。
- 创建/更新成功 Detail 非空，含最新头行版本；删除成功返回 Uid、AlreadyApplied，Detail=null。删除限制 LifecycleStatus=Active 且 PostingStatus=Unposted，整单软删除，禁止已过账/已冲销；同命令重试先查原成功结果。
- 使用 ServiceResult.Error.Code/Field：VERSION_CONFLICT、IDEMPOTENCY_CONFLICT、LINE_SET_MISMATCH、INVALID_DRAFT、SOURCE_CHANGED、CUSTOMER_MISMATCH、NOT_FOUND、STATE_BLOCKED；Field 如 Header.CustomerId、Lines[行Uid].Qty。401/403 保持HTTP权限边界，不解析中文消息判断类别。
- 失败、取消、超时不得 AcceptChanges 或清空输入；保存成功以完整最新 Detail 重新 Load，协调 Header 与 Lines 的 LoadFromDtos。删除池读取 DeletedItems；未保存新行删除不提交 DeletedLines。

## 查询字段来源与空值

| 候选字段 | Join/来源 | 快照及空值规则 |
|---|---|---|
| GrossDemandId/DemandVersion/SourceVersion | GrossDemand g 的 Uid/Version/SourceVersion | 来源身份权威；版本由查询返回不伪造 |
| CustomerId/ProductId | g.CustomerId/g.ProductId | 候选只包含可证明同客户且已关联产品者；非标未关联产品暂不返回为可选 |
| SourceType/SourceNo/DemandQty | g 同名字段 | 按来源快照，首版认可 SO/CR 实物需求，不限定已拆解/已采购 |
| SkuSnapshot/LinkSkuSnapshot/CustomerSkuSnapshot/UnitSnapshot | g 同名快照 | 不用当前产品编码覆盖来源快照；未知单位为空 |
| DescriptionEnSnapshot/ColorNameEnSnapshot | SO 通过 g.SourceLineId → SalesOrderLine，且校验 SourceUid；否则明确留空 | 优先合法来源英文快照；GrossDemand.DescriptionSnapshot/ColorSnapshot 未标语言，禁止冒充英文。CR 英文来源后端需证明后补充 |
| ImagePath | g.ImagePathSnapshot | 展示空值允许，不写回实体，不取外部网络内容 |
| CustomerPoNo | SO 的 g.SourceUid → SalesOrderHeader.CustomerPoNo | 快照采用；CR 没有可证明订单归属则为空，不按SKU猜测 |
| PcsPerPackage/NwPerPackage/GwPerPackage/CbmPerPackage | ProductPackage.ProductId=g.ProductId，IsDeleted=0、IsDefault=1 | 恰好唯一才取 PcsPerPackage/Nw_KG/Gw_KG/Cbm；无或多条默认全部为空，不任意挑选；本轮不猜测客户包装覆盖 |
| DeliveredQty/RemainingQty | 后续正式最终客户出库按 GrossDemandId 聚合有效净额 | 未实现或不可证明时两值为空且提供 DeliveryUnavailableReason；不得用g.DemandQty当剩余兜底 |

来源候选可在交付投影不可用时用于准备草稿资料，但不得视为通过可交付额度校验或允许过账；后端保存无法证明额度时须明确阻断，不返回伪成功。

现存单据详情始终以 Head/Line 的同名持久化快照为准；不能再次查当前主数据刷新已保存快照。查图允许沿 GrossDemandId 读取只读图片，孤立来源返回空图不伪造资料。所有 Join 先校验基数，客户产品多条关系不随意挑选。后端需单独完成快照采用和主数据权限/有效性校验。

## DTO字段清单

### 待发货 Picker 补充

- 复用 PackingListSourceSearchRequest、PackingListSourceCandidateDto 和 IPackingListApi.SearchSourcesAsync，不新增重复来源 API。ImagePath 已在 DTO 中，贯穿 PickerRow、Selection.Source 与新行快照，仅展示不持久化。
- PackingListSourcePickerSearchModel 保存固定 CustomerId、Keyword、PageIndex/PageSize；候选仍来自 GrossDemand，不能要求先有 NetDemand 或采购。
- PackingListSourcePickerRowModel 为只读候选快照加 IsChecked、DraftQty、AvailableQty；不是持久化单据行。剩余数量由后端交付投影扣减后，前端再扣当前草稿；选中后在草稿调整数量。
- 后端正常待发货查询过滤已确认 RemainingQty<=0；不得将未实现交付投影伪造为0。详情按 GrossDemandId 汇总本单已有数量传给 Picker，与本次选择合计复核；不排除整个来源（允许拆多行）。前端校验不替代服务端保存时重验。
- PackingListSourcePickerSelection 包含完整 Source 快照和 Qty；弹窗仅确认时返回快照，取消/关闭返回 null。详情显式生成行Uid/LineNumber，赋值 Qty，按建议初始化 PackageCount；不自动填写 InvoiceNo，不在映射中创建业务身份。ImagePath 贯穿候选、选择结果和详情行，不写入保存命令。
- 当前 Picker 为“当前页选择”协议：勾选后查询或翻页被阻断，先确认加入或清空勾选，不静默丢失已选项；可以多次打开以加入不同来源或不同包装行。
- 本轮不创建 Picker View/ViewModel/Dialog Service 或联网逻辑；候选图列将绑定 ImagePath，使用既有 CachedImage。查询采用客户隔离、分页、取消及 latest-wins，跨页勾选策略留给实际弹窗实现。

### PackingListHeaderDto

继承 BaseDto 身份、版本、软删除与审计；仅查询响应使用。

| 字段 | 类型 | 含义 |
|---|---|---|
| PackingListNo | string | 装箱单业务编号。 |
| CustomerId | Guid | 最终客户逻辑标识。 |
| CustomerNameSnapshot | string | 单据采用的客户名称快照，不随主数据更新。 |
| CustomerAddressSnapshot | string | 单据采用的客户地址快照。 |
| AttentionSnapshot | string | 自由填写的备注快照，不关联联系人实体。 |
| InvoiceDate | DateTime | 用户自定义单据日期，不作为库存记账日期。 |
| ContainerNo | string | 集装箱号。 |
| SealNo | string | 封条号。 |
| Note | string | 单据备注。 |
| Etd | DateTime? | 预计离港日期。 |
| Eta | DateTime? | 预计到达日期。 |
| LifecycleStatus | PackingListLifecycleStatusEnum | 单据生命周期，与库存状态分别表达。 |
| PostingStatus | PackingListPostingStatusEnum | 库存过账状态，不表示已实现过账入口。 |
| PostingDate | DateTime? | 库存业务日期，未过账可空，与单据日期独立。 |
| PostedDate | DateTime? | 实际过账操作时间。 |
| PostedBy | string | 过账操作人。 |
| ReversedDate | DateTime? | 反向操作时间。 |
| ReversedBy | string | 反向操作人。 |
| ReverseReason | string | 反向原因。 |
| CreateCommandId | Guid | 保留建单命令身份，具体命令校验另行实现。 |
| PostCommandId | Guid? | 过账命令身份，用于后续幂等处理。 |
| ReverseCommandId | Guid? | 反向命令身份，用于后续幂等处理。 |
| InvoiceNosSummary | string | 有效行发票号去重汇总，只读。 |

### PackingListLineDto

继承 BaseDto 身份、版本、软删除与审计；仅查询响应使用。

| 字段 | 类型 | 含义 |
|---|---|---|
| PackingListHeadId | Guid | 所属装箱单头 Uid，逻辑关联。 |
| LineNumber | int | 单据内行号，同来源允许多行。 |
| GrossDemandId | Guid | 唯一交付来源锚点，不表示采购或物料分摊。 |
| ProductId | Guid | 本次交付的产品标识。 |
| SkuSnapshot | string | 产品编码快照。 |
| LinkSkuSnapshot | string | 关联型号快照。 |
| CustomerSkuSnapshot | string | 客户型号快照。 |
| DescriptionEnSnapshot | string | 英文描述快照。 |
| ColorNameEnSnapshot | string | 英文颜色名称快照。 |
| UnitSnapshot | string | 库存及交付数量的计量单位快照。 |
| Qty | decimal | 本行计划交付数量，草稿不形成正式交付事实。 |
| PcsPerPackage | decimal? | 每包数量；未知为空，已知时须大于零。 |
| PackageCount | decimal? | 实际整包装数；默认建议向上取整，允许人工指定，未知为空。 |
| NwPerPackage | decimal? | 每包净重。未知为空。 |
| GwPerPackage | decimal? | 每包毛重。未知为空。 |
| CbmPerPackage | decimal? | 每包体积。未知为空。 |
| SubTotalNw | decimal? | 实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。 |
| SubTotalGw | decimal? | 实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。 |
| SubTotalCbm | decimal? | 实际包装数乘单包指标的结果快照；未来保存入口重算，四位小数 AwayFromZero，未知为空。 |
| CustomerPoNo | string | 客户采购编号文本快照，不作为关系主键。 |
| InvoiceNo | string | 行上手工填写的发票号文本，不关联财务发票。 |
| Marks | string | 本行箱唛。 |
| Note | string | 本行备注。 |
| WarehouseSiteId | Guid? | 历史兼容仓库标识；不再作为当前单据库存操作输入。 |
| WarehouseCodeSnapshot | string | 采用的仓库编码快照。 |
| WarehouseNameSnapshot | string | 采用的仓库名称快照。 |
| ImagePath | string | 查询展示图片，不持久化。 |

### PackingListDraftHeaderInput

| 字段 | 类型 | 含义 |
|---|---|---|
| CustomerId | Guid | 最终客户逻辑标识。 |
| CustomerNameSnapshot | string | 单据采用的客户名称快照，不随主数据更新。 |
| CustomerAddressSnapshot | string | 单据采用的客户地址快照。 |
| AttentionSnapshot | string | 自由填写的备注快照，不关联联系人实体。 |
| InvoiceDate | DateTime | 用户自定义单据日期，不作为库存记账日期。 |
| ContainerNo | string | 集装箱号。 |
| SealNo | string | 封条号。 |
| Note | string | 单据备注。 |
| Etd | DateTime? | 预计离港日期。 |
| Eta | DateTime? | 预计到达日期。 |

### PackingListDraftLineInput

| 字段 | 类型 | 含义 |
|---|---|---|
| Uid | Guid | 行稳定身份；新增行也由客户端生成非空 Uid。 |
| ExpectedVersion | int | 新增为0，已有行须提交原版本。 |
| LineNumber | int | 单据内行号，同来源允许多行。 |
| GrossDemandId | Guid | 唯一交付来源锚点，不表示采购或物料分摊。 |
| ProductId | Guid | 本次交付的产品标识。 |
| SkuSnapshot | string | 产品编码快照。 |
| LinkSkuSnapshot | string | 关联型号快照。 |
| CustomerSkuSnapshot | string | 客户型号快照。 |
| DescriptionEnSnapshot | string | 英文描述快照。 |
| ColorNameEnSnapshot | string | 英文颜色名称快照。 |
| UnitSnapshot | string | 库存及交付数量的计量单位快照。 |
| Qty | decimal | 本行计划交付数量，草稿不形成正式交付事实。 |
| PcsPerPackage | decimal? | 每包数量；未知为空，已知时须大于零。 |
| PackageCount | decimal? | 实际整包装数；默认建议向上取整，允许人工指定，未知为空。 |
| NwPerPackage | decimal? | 每包净重。未知为空。 |
| GwPerPackage | decimal? | 每包毛重。未知为空。 |
| CbmPerPackage | decimal? | 每包体积。未知为空。 |
| CustomerPoNo | string | 客户采购编号文本快照，不作为关系主键。 |
| InvoiceNo | string | 行上手工填写的发票号文本，不关联财务发票。 |
| Marks | string | 本行箱唛。 |
| Note | string | 本行备注。 |
| WarehouseSiteId | Guid? | 历史兼容仓库标识；不再作为当前单据库存操作输入。 |
| WarehouseCodeSnapshot | string | 采用的仓库编码快照。 |
| WarehouseNameSnapshot | string | 采用的仓库名称快照。 |

### PackingListDeletedLineInput

| 字段 | 类型 | 含义 |
|---|---|---|
| Uid | Guid | 显式删除行身份。 |
| ExpectedVersion | int | 删除行期望版本。 |

### PackingListManagerListItemDto

继承 BaseDto 身份、版本、软删除与审计；仅查询响应使用。

| 字段 | 类型 | 含义 |
|---|---|---|
| PackingListNo | string | 装箱单业务编号。 |
| CustomerId | Guid | 最终客户逻辑标识。 |
| CustomerNameSnapshot | string | 单据采用的客户名称快照，不随主数据更新。 |
| InvoiceDate | DateTime | 用户自定义单据日期，不作为库存记账日期。 |
| ContainerNo | string | 集装箱号。 |
| SealNo | string | 封条号。 |
| Etd | DateTime? | 预计离港日期。 |
| Eta | DateTime? | 预计到达日期。 |
| LifecycleStatus | PackingListLifecycleStatusEnum | 单据生命周期，与库存状态分别表达。 |
| PostingStatus | PackingListPostingStatusEnum | 库存过账状态，不表示已实现过账入口。 |
| InvoiceNosSummary | string | 有效行发票号去重汇总，只读。 |

### PackingListManagerSearchRequest

| 字段 | 类型 | 含义 |
|---|---|---|
| CustomerId | Guid? | 客户筛选。 |
| PostingStatus | PackingListPostingStatusEnum? | 空值表示全部过账状态。 |
| InvoiceDateFrom | DateTime? | 单据日期起始，含当天。 |
| InvoiceDateTo | DateTime? | 单据日期截止，含当天。 |

### PackingListSourceSearchRequest

| 字段 | 类型 | 含义 |
|---|---|---|
| CustomerId | Guid | 必须指定最终客户。 |

### PackingListSourceCandidateDto

| 字段 | 类型 | 含义 |
|---|---|---|
| GrossDemandId | Guid | 来源需求唯一标识。 |
| DemandVersion | int | 候选来源并发版本。 |
| SourceVersion | int? | 上游事实版本，未知为空。 |
| CustomerId | Guid | 已证明的最终客户。 |
| ProductId | Guid | 已关联的标准产品。 |
| SourceType | string | 来源类型。 |
| SourceNo | string | 来源单号快照。 |
| DemandQty | decimal | 来源要求数量。 |
| DeliveredQty | decimal? | 正式交付净额；不可用时为空，不伪装零。 |
| RemainingQty | decimal? | 剩余交付量；不可用时为空。 |
| DeliveryUnavailableReason | string | 交付投影不可用原因。 |
| SkuSnapshot | string | 产品编码快照。 |
| LinkSkuSnapshot | string | 关联型号快照。 |
| CustomerSkuSnapshot | string | 客户型号快照。 |
| DescriptionEnSnapshot | string | 英文描述快照。 |
| ColorNameEnSnapshot | string | 英文颜色名称快照。 |
| UnitSnapshot | string | 库存及交付数量的计量单位快照。 |
| PcsPerPackage | decimal? | 每包数量；未知为空，已知时须大于零。 |
| NwPerPackage | decimal? | 每包净重。未知为空。 |
| GwPerPackage | decimal? | 每包毛重。未知为空。 |
| CbmPerPackage | decimal? | 每包体积。未知为空。 |
| CustomerPoNo | string | 客户采购编号文本快照，不作为关系主键。 |
| ImagePath | string | 查询展示图片，不持久化。 |

### PackingListDetailDto

| 字段 | 类型 | 含义 |
|---|---|---|
| Header | PackingListHeaderDto | 完整头快照。 |
| Lines | List<PackingListLineDto> | 有效物理来源行，保持行号顺序。 |

### CreatePackingListDraftCommand

| 字段 | 类型 | 含义 |
|---|---|---|
| CommandId | Guid | 同意图重试保持不变。 |
| Uid | Guid | 客户端生成的单据身份。 |
| Header | PackingListDraftHeaderInput | 可写文档字段。 |
| Lines | List<PackingListDraftLineInput> | 全部有效草稿行。 |

### UpdatePackingListDraftCommand

| 字段 | 类型 | 含义 |
|---|---|---|
| CommandId | Guid | 同意图重试保持不变。 |
| ExpectedVersion | int | 整单期望版本。 |
| Header | PackingListDraftHeaderInput | 可写文档字段。 |
| Lines | List<PackingListDraftLineInput> | 全部有效行快照，遗漏不代表删除。 |
| DeletedLines | List<PackingListDeletedLineInput> | 显式删除身份与版本。 |

### DeletePackingListDraftCommand

| 字段 | 类型 | 含义 |
|---|---|---|
| CommandId | Guid | 删除命令身份。 |
| ExpectedVersion | int | 整单期望版本。 |

### PackingListDraftCommandResult

| 字段 | 类型 | 含义 |
|---|---|---|
| Uid | Guid | 命令处理的单据身份。 |
| AlreadyApplied | bool | 同命令已成功应用。 |
| Detail | PackingListDetailDto | 保存成功的最新详情；删除成功为空。 |

## 已实现前端运行时责任

VM 直接依赖窄 IPackingListApi 和现有 API Runner。来源弹窗由 PackingListSourcePickerService 创建独立会话并负责释放。详情 RouteKey 为 shipping.packing-list-detail，documentKey 为 Uid.ToString("N")；新建使用客户端生成的 Uid，Create 成功后仍保留该 Uid，重复编辑激活现有标签，不覆盖脏输入。管理页 RouteKey 为 shipping.packing-list-manager。

Manager 搜索/翻页/切换明细使用 CancellationToken + 请求序号 + 页面存活检查 latest-wins；失败保留当前列表并给出一次反馈。Detail 保存/删除使用统一写Busy防重入，退出检查 Header.IsDirty 或 Lines.HasChanges，写中不关闭；订阅在Dispose/离开时释放。

导航使用已有 Module.Shipping.Access；尚无 PackingList 独立操作权限键，不硬编码角色。前端 CanExecute 按有效未过账草稿、Busy、弹窗状态限制编辑，删除只接受有效未过账草稿；后端仍须独立授权每个 API。后续细分查看/新建/编辑/删除权限时需同时适配前后端。

发票号及小计仅本地预览。PackageCount 手工值不随 Qty 变化静默重置；选源初始化时显式采用 SuggestedPackageCount。未知指标使相应小计/总计未知，不按零计；数量按单位分组，使用当前文化显示。保存前通过视图委托提交 Grid 原生编辑并检查 WPF 转换错误，再做模型验证；卸载时解除委托。来源、产品和状态只读，有物理行（含待删除旧行）时锁定客户；先撤销/重新建单，不静默改换来源客户。出货地点为可选自由文本，不再选择或校验库存仓库。

保存返回完整详情且头行身份匹配才接纳为新基线；失败或不完整响应保留编辑。相同请求内容重试复用 CommandId，内容变化生成新命令标识。当前页只在内存保留重试身份，不承诺跨进程离线恢复；超时后若改动原意图，后端必须根据稳定单据 Uid 防重复建单并返回冲突，不得再创建第二张。更新提交所有有效行及明确删除身份/版本，未列出的旧行不能默认为删除。

Manager 修改成功后本地移除已删除草稿；详情保存后 Manager 通过“刷新”重新读取，不自动用详情 DTO 替代列表摘要。写入期间禁止关闭，未保存关闭需确认；查询失败可重试，已有单据加载失败保持只读。

## 后端接手顺序及未完成项

1. 核查新表部署及旧仓库引用保护 SQL 适配；本轮未接数据库。
2. 实现查询/选源（含交付进度不可用处理）、权限、草稿事务和幂等记录，依照 Refit 签名建立 Controller。
3. 前端接线已完成；后端就绪后联调真实端到端查询、保存、删除、权限/版本冲突与网络超时重试。
4. 最后设计过账、冲销、库存补入库与客供耗用，不强制采购行、不将公共库存缺口伪装实收。
5. Trace/Export/XLSX与打印另做；本轮无输出服务。

后端交付关联口径仍为 GrossDemand 唯一锚点，采购通过 DemandExecutionLink→NetDemand→GrossDemand 去重显示关联来源进度，不做采购行出货分摊。

## 验证与限制

契约、模型、VM 运行时行为及真实 XAML 加载可离线测试；仍不声称后端业务和真实数据端到端可用。人工键盘/屏幕阅读器/高对比度及真实网络性能尚需联调验收。复用现有客户/图片缓存和 API Runner；未新增缓存、导出或库存实现。

### 本轮验证证据

- Contracts、WPF 及 SwiftExport-Frontend.slnx 构建通过，0 警告/0 错误。
- 正常 WPF 测试项目被其他模块 NetDemandPlanEditorViewModelTests 引用已移除类型的编译错误阻断，未修改或排除原项目断言。
- 忽略目录 artifacts/packinglist-tests 下的临时测试宿主直接链接 Shipping 测试和既有 WPF 夹具，24 项专项测试通过，含草稿失败/重试/Busy、显式删除版本、只读状态、latest-wins、Picker 图片/额度、路由注册与真实主题下三页 XAML 实例化；此结果不代表完整 WPF 测试通过。可复跑 dotnet test artifacts/packinglist-tests/PackingList.Tests.csproj --no-restore --filter FullyQualifiedName~PackingList；正常项目恢复后使用原项目同一筛选运行。
- 临时还原使用本地包成功，漏洞信息源不可达产生 NU1900，未完成在线依赖漏洞检查。
- XAML 已完成离线实例化及只读检查；尚未进行真实窗口交互或人工视觉验收。
