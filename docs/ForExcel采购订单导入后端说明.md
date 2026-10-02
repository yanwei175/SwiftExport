# ForExcel 采购订单导入后端说明

## 本期边界

Excel 导入创建独立、未确认的采购草稿。客户代码与客户型号定位标准产品，用户可明确将无法定位的行改为非标。创建不产生需求 Link，不修改物料需求，不自动确认或入库。本期没有改 WPF/VSTO 页面、Excel 解析与地址配置。

后续采购关联实施已修复非标确认：明确非标身份、描述及有效单位可确认，无需 ProductId。非标类型由明确创建输入和空产品身份表达，不按 SKU 前缀推断；非标编号通过现有 NonStandardSkuGenerator 生成，不创建产品主档。此调整不代表非标库存入库已实现。

现已提供创建后独立关联物料需求的后端用例：待确认或已确认采购都可分次分配剩余量，整单限同一来源单。导入创建本身仍不产生 Link；用户选择与人工匹配由后续独立页面承载。接口、规则及验收见《采购物料需求关联后端实施与验收》。

## API 与用例

| 用例 | HTTP | Contracts | 服务 |
|---|---|---|---|
| 导入只读检查 | POST /api/for-excel/purchase-orders/check | IForExcelPurchaseOrderApi / CheckPurchaseOrderImportRequest / Response | ForExcelPurchaseOrderService |
| 创建无关联草稿 | POST /api/procurement/orders | IProcurementOrderApi.CreateAsync / CreateIndependentPurchaseOrderCommand | ProcurementOrderDraftService |
| 已有采购追加关联 | POST /api/procurement/orders/{uid}/associate-demands | IProcurementOrderApi.AssociateDemandsAsync / AssociateProcurementDemandsCommand | ProcurementDemandAssociationService |
| 从物料需求生成采购 | POST /api/procurement/purchase-drafts/create | IPurchaseDraftApi | 现有需求采购服务，本次未修改 |

检查契约位于 SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders；创建命令仍位于 Procurement/PurchaseOrders/Draft。检查与写命令分别表达只读快照和正式业务输入，不能互当授权凭据，也不复用销售导入 DTO。

检查使用现有 Procurement.Order.Create 权限，Controller 需要登录。创建复用现有权限、CommandId、可串行化事务、业务日志和持久化回执。仓储共享请求 DbSession，检查不另开事务，不写库；创建中重新读取客户型号关联并保护当前关系范围。

## 检查输入与结果

请求提供 CustomerCode、SupplierId、Header 和 Lines。实际供应商使用现有供应商选择能力确定的稳定身份，不能以 Excel 供应商简称猜测 Uid。单头与每行允许手工补充。明细要求 1～500 条，各 ClientLineId 唯一且非空，只用于检查结果对齐，不作为数据库行身份。

标准行以客户代码定位唯一启用客户，然后在 CustomerId + CustomerSku 范围内批量匹配。型号只去首尾空格、忽略大小写；保留内部空格、符号和前导零。全部关联候选都会返回，停用、断链或没有采购资格的候选不能通过；不会用分页搜索第一页或 TOP(1) 偷选。

| MatchStatus | 含义 |
|---|---|
| Matched | 标准产品定位成功，Product 返回真实 ProductId、Sku、LinkSku、单位等 |
| NonStandard | 用户明确标记非标，Product 为空；仍须通过描述、单位和数值校验 |
| NotMatched | 客户型号未定位 |
| Ambiguous | 多个关联，须从 Candidates 明确选择 SelectedCustomerProductId |
| Unavailable | 选择越界，或关联、产品、单位不可用 |

产品是否可采购沿用活动主档、SupplyType=采购、MaterialType 为成品/零件/客供件的现有规则。基础单位必须唯一且启用。客供件须选择有效 DeliverySupplierId。

Excel LinkSku 非空且与系统不同，返回 LinkSkuMismatch，RequiresConfirmation=true；用户确认后再次检查须提交 AcceptLinkSkuMismatch=true。不可匹配、停用、无效数字等硬错误不能由该标记放行。

CanSave 仅说明本次检查没有未解决的问题。页面应根据 Header/Lines 的 Issues 阻止保存；修改影响身份或校验的内容后重新检查。检查不修改输入报价、描述和优惠字段，也不生成草稿。

## 创建输入与字段归属

创建复用 Manager 的“新建无关联采购”，不扩展物料需求 Preview/Create。新增字段可选，保持旧客户端请求兼容。

| Excel / 手工资料 | 创建字段 | 持久化字段 |
|---|---|---|
| 采购单号 | BoNo | BoNo；系统 OrderNo 另行生成 |
| 客户 PO 号 | CustomerPoNo | CustomerPoNo |
| 下单日期 | OrderDate | OrderDate |
| 回复交期 | ReplyDate | ReplyDate |
| 业务员 / 跟单员 | SalesPerson / PurchasePerson | SoPerson / PoPerson，属于单据快照，不控制审计身份 |
| 进水管 / 起泡器 / 托盘 | InletHoseType / AeratorType / PalletType | InletrHoseType / AeratorType / PalletType |
| 内盒 / 标贴 / 说明书 / 外箱 / 光标 | BoxType / LabelType / InstructionManualType / CartonType / LogoType | BoxType / LabelType / IMType / CartonType / LogoType |
| 特殊要求 | Remark | Remark |
| 价格浮动率 / 金额调整 / 备注 | DiscountRate / DiscountAmount / DiscountRemark | DiscountRate / DiscountAmount / ZheKouBeiZhu |
| 客户型号 / 本司型号 / 工厂型号 | CustomerSku / LinkSku / SupplierSku | 同名商业快照 |
| 描述 / 表面处理 / 配置 | DescriptionZh / FinishZh / FeaturesZh | 同名商业快照 |
| 数量 / 未税单价 / 可选含税价 | Quantity / UnitPrice / TaxPrice | QTY / UnitPrice / TaxPrice |
| 行交期 / 行备注 | PlannedDeliveryDate / SubNote | 同名字段 |

期望交期由前端复制到相应行 PlannedDeliveryDate，采购头 ExpectedDate 继续取有效行最早交期。创建币种沿用现有 CNY，不引入汇率换算。

标准创建行提交检查返回的 ProductId、CustomerSku，以及必要时 SelectedCustomerProductId 和 AcceptLinkSkuMismatch。服务在正式事务中重新匹配，拒绝身份不一致、越界选择、关联停用和未确认的 LinkSku 差异。新请求显式给出空 CustomerSku 会拒绝；旧客户端不提供客户型号时仍可按 ProductId 手工选择。

ProductId 和真实 Sku、基础单位由服务端建立；客户型号、LinkSku、工厂型号、描述、颜色和配置保留输入商业快照。null 使用当前主数据默认，空字符串表示用户明确留空。非标行必须 IsNonStandard=true、ProductId=Guid.Empty、SelectedCustomerProductId 为空；描述与启用单位必填，不允许附带客供件送达身份。本期非标不虚构物料类型、供货类型。

数量须大于零，未税价必填且非负，零价有效；含税价可空或非负。数量、单价、金额按现有物理 decimal(18,4) 校验，不依赖 SQL 舍入。UnitAmount、TaxAmount 由数量乘对应单价计算；Excel 金额不作为权威输入。数量和价格乘积超过金额范围时拒绝。

DiscountRate 使用 decimal(9,6)，允许非负记录；DiscountAmount 使用可空 decimal(18,4)，金额调整记录允许有符号。两者只存记录，不互相推导，不调整行单价，不计算优惠后整单金额。整单金额查询规则另行设计。已有商务更新接口本期没有新增数值优惠编辑协议。

创建产生 OriginKind=2，UnlinkedQty=Quantity，需求来源为空。不接收 Excel 制单、审核等文本作为系统审计人员。

## 幂等与兼容

同一 CommandId、同一输入重试返回原结果，不重建、不重取号；修改 BoNo、优惠、配置、型号选择或差异确认等任意新增输入都会参与指纹并触发内容冲突。旧请求没有新增字段时保留原业务指纹结构，使已存在的旧命令回执仍可重放。

创建返回既有 PurchaseOrderCommandResult，需用 PurchaseOrderId 读取详情。PurchaseOrderLineDetailDto 新增 IsNonStandard；为保持旧消费者兼容，原 ProductId 仍是 Guid，非标投影为 Guid.Empty。确认核验完整非标身份及单位，人工关联还要求明确接受非标匹配；空产品身份不作为库存入库资格。

## 验证及部署

本次不新增物理列，使用现有采购表字段；生产数据库须已具备当前实体基线中的这些列，不能把空库 LocalDB 测试当成生产结构已验证。

自动测试覆盖精确匹配、歧义选择、停用及断链、非标、型号差异确认、单位、客供件送达、金额精度、权限、取消、路由及序列化、旧指纹兼容。隔离 LocalDB 测试覆盖从检查到创建读取、完整快照落库、重放冲突、关联失效重验、空型号拒绝、有效非标确认，以及日志失败时头行和回执回滚。

运行采购导入 SQL 专项：

```powershell
$env:SWIFTEXPORT_RUN_SQL_TESTS = '1'
dotnet test .\SwiftExport.Application.Tests\SwiftExport.Application.Tests.csproj --filter "FullyQualifiedName~ForExcelPurchaseOrderSqlTests"
```

测试夹具仅创建并清理唯一命名的 SwiftExport_ProcurementTests_* LocalDB 数据库，不读取业务连接配置。前端 API 注册、解析、页面交互和 Excel 实机联调留到下一阶段。

### 导入初期执行结果（历史记录）

- 采购、采购主数据及 ForExcel 非 SQL 回归：117 项通过。
- 新增 ForExcelPurchaseOrderSqlTests：4 项真实 LocalDB 测试通过，包含上述落库、重放、重验和回滚场景。
- 扩展运行独立采购 SQL 回归合计 25 项：22 通过、2 失败、1 跳过。失败为 ManagerReceiptFactsUsePurchaseLineIdentityWithoutDemandLinkDependency（未确认单的 ReceiptProgress 实际为空，而断言要求 0.6）和 ReferencedProductUnitCannotBeChanged（数据库允许修改产品单位，而断言要求 SqlException）；MigrationPreservesLegacyRowsAndCanRunTwice 因当前数据库策略只支持全新空库基线而跳过。
- 没有删除或放宽失败断言；没有修改采购管理收货进度计算、产品单位数据库约束或数据库基线。上述失败仍需单独处理，不能将本次专项通过表述为采购全量通过。
- 初次 LocalDB 测试受沙箱注册表访问限制失败；在允许本机 LocalDB 访问的环境重跑后取得上述数据库结果。NuGet 漏洞源访问有 NU1900 警告，现有测试还有分析器警告。

### Reviewer 长文本保存修复

Reviewer 发现创建允许工厂型号 300 字、采购描述 2000 字，而商务保存仍限制为 200、300 字，导致导入后即使只改交期也无法保存。新增真实 LocalDB 回归先复现了“供应商型号超过长度限制”失败。

创建校验、商业快照赋值和商务保存现统一使用 ProcurementOrderPolicy 的 SupplierSkuMaximumLength=300、DescriptionZhMaximumLength=2000，与物理列一致。超出长度仍拒绝，不截断文本，不改变身份、数量或价格规则。

修复后采购 Policy 与 ForExcel SQL 专项共 12 项通过，其中包括新增最大长度“创建→读取→原样回传文本、只改交期→保存→重读”回归，以及 301 字型号、2001 字描述同时在创建与保存拒绝的边界测试。上述已有两项 SQL 失败不在本次修复范围内，没有删除或放宽断言。
