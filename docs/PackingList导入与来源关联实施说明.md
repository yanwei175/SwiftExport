# PackingList 导入与来源关联实施说明

核对日期：2026-10-03。本文记录本次源码和验证结果，业务库迁移与 Excel 实机验收尚未执行。

## 使用流程

1. VSTO Ribbon 打开「PackingList 导入」，以 WinForms＋ElementHost 非模态窗口显示。
2. 用户选择客户和 PackingList 地址配置，读取单头与明细，修正数据，点击检查。
3. 标准行通过客户产品关系取得 ProductId、真实 Sku 和单位；多候选明确选择后再次检查。匹配失败不自动转非标，用户可明确标记非标并填写描述、单位。
4. Excel 中性型号与系统 LinkSku 不同须确认；任何编辑使检查失效。检查通过才允许创建未关联草稿，保存仍由服务器独立复核。
5. .NET 10 打开保存后的 PackingList，先保存本地修改，再选中待关联普通行，点击「关联来源」。用户明确填写来源数量，可部分关联。
6. 服务端软删除原行，生成独立来源行和待关联余量。总数量守恒，拆行后的包装数值清空；箱唛、备注、描述、颜色和 Excel PO／Invoice 原文保留。
7. 全部有效行关联后，继续既有包装、拼箱和过账流程。任一待关联行存在时，预览与正式过账均阻断。草稿不占用交付额度或库存。

## 数据与接口边界

`PackingListLine.GrossDemandId` 和草稿／查询 DTO 改为 `Guid?`。空值表示待关联草稿；禁止空 Guid。正式执行证据仍要求真实来源身份，不增加分摊表，也不增加 GrossDemand.InvoiceNo。

未关联行的来源类型、单号为空文本，行号为空；关联后由服务端填写来源快照。原单关联关系不允许通过普通草稿更新直接替换。

| 接口 | 职责 |
|---|---|
| POST `/api/for-excel/packing-lists/check` | 只读批量产品检查，返回候选、匹配身份和字段问题；需要 PackingList.Create 权限 |
| POST `/api/shipping/packing-lists/drafts` | 复用创建草稿，允许未关联行，保存独立复核 |
| PUT `/api/shipping/packing-lists/drafts/{uid}` | 复用更新草稿，已有来源不能直接替换 |
| POST `/api/shipping/packing-lists/sources/search` | 按客户、产品／非标、InvoiceNumbers 和关键词筛选，计数前过滤 |
| POST `/api/shipping/packing-lists/drafts/{uid}/lines/{lineUid}/associate-sources` | 携带 CommandId、头行版本、来源版本及数量，事务内关联、拆行、审计与回执 |

来源 InvoiceNumbers 使用销售单 InvoiceNo 或售后单系统 AfterSalesNo，通过联表展示。换行、英文／中文分号和斜杠拆分去重后精确查询。PO 不参与来源查找；无结果时可清空单号手动查询。来源候选不要求已有采购执行关系，采购与库存准入仍由正式过账负责。

来源分配以正式余额减本草稿已有数量作为可选量；关联时服务器复核全单同来源合计。草稿不预占余额，其他单据正式交付后，保存／关联／过账可能因余额变化被拒绝。

未知保存或关联结果冻结原命令，只重试相同 CommandId 和内容。确定失败保留输入供修正。会话替换、页面释放或旧请求不允许回填；关联过程中更换账号后需重新加载详情核实。

## 数据库部署

- 旧库脚本：[PackingList未关联草稿迁移.sql](../SwiftExport.Infrastructure/Database/PackingList未关联草稿迁移.sql)。只改变来源列可空性并重建相同索引，保留历史来源及已过账数据；发现空 Guid 或非草稿待关联行会拒绝执行。
- 空库基线：[SwiftExport.Schema.sql](../SwiftExport.Infrastructure/Database/Generated/SwiftExport.Schema.sql)，已由实体元数据重新生成并通过 `--verify`。
- 上线前协调后端和两端客户端版本，完成脚本审核及备份，由用户安排正式迁移。旧客户端不能读取新待关联行；未完成版本升级前不启用导入入口。
- 本次验证只创建随机命名的本机 LocalDB 测试库并在结束时删除，不使用应用业务连接配置。

## 附件地址配置示例

工作表 `PL`；WorksheetName 可留空表示当前工作表。下列地址对应本次附件，不是通用模板；单头拆分地址 A11／A12 由用户整理为一个单格或横向合并格。

| 字段 | 地址 | 向下读取 |
|---|---|---|
| Header.ExcelDocumentNo | M9 | 否 |
| Header.InvoiceDate | M10 | 否 |
| Header.CustomerName | A10 | 否 |
| Header.ContainerNo / Header.SealNo | M11 / M12 | 否 |
| Header.Etd / Header.Eta | M13 / P13 | 否 |
| Line.CustomerSku / Line.OriginalLinkSku | D15 / E15 | 是 |
| Line.DescriptionEn / Line.ColorNameEn | F15 / G15 | 是 |
| Line.Qty | H15 | 是 |
| Line.PcsPerPackage / Line.NwPerPackage | I15 / J15 | 是 |
| Line.GwPerPackage / Line.CbmPerPackage | K15 / L15 | 是 |
| Line.CustomerPoNo / Line.InvoiceNo | Q15 / R15 | 是 |
| Line.Marks | B15 | 是，可省略 |

本附件可采用 `EndMode=2`、`SummaryCellAddress=H36`，明细读取 15–35 行。也可使用 `EndMode=1` 指定末行列，但须确保该列的后续汇总／备注不会误入明细。读取包含隐藏行，不限制于打印区 A:P，Q／R 必须单独配置。

单头 Excel 编号 `24th` 合入整单 Note，系统 PackingListNo 继续自动生成。跨行合并的箱唛、每箱数量、净重、毛重和体积留空并提示，不重复计量；仅 PackingList 的指定可选字段采用此容错，数量和型号仍严格读取，销售／采购规则不改变。

## 主要文件清单

逐文件列表见 [本轮实施文件清单](PackingList实施文件清单.md)。

| 范围 | 文件／目录 |
|---|---|
| 共享契约 | `SwiftExport.Contracts/DTOs/ForExcel/PackingLists/`、`Apis/ForExcel/IForExcelPackingListApi.cs`、PackingList 草稿／查询 DTO、`IPackingListApi.cs` |
| 实体和迁移 | `SwiftExport.Core/Entities/Shipping/PackingListLine.cs`、`DatabaseSchema/EntitySchemaMetadata.cs`、上述迁移和生成基线 |
| 产品检查 | `SwiftExport.Application/Services/Shipping/PackingLists/PackingListImportChecker.cs`、`Services/ForExcel/ForExcelPackingListService.cs`、对应接口与 Controller |
| 来源与草稿 | `PackingListDraftService.Association.cs`、草稿服务、查询服务、PackingList 仓储来源查询／详情／保存、过账／反向空来源边界 |
| VSTO | `Features/Shipping/PackingLists/`、ExcelAddressReader、ExcelImportRegistry、DI、Bootstrapper、Ribbon XML／回调及传统项目 XAML 声明 |
| .NET 10 | 既有 PackingList 详情、行模型、Picker 模型／ViewModel／View、来源分配服务和 Association 部分文件 |
| 自动验证 | 后端 `PackingListImportAndAssociationTests`／`PackingListDraftLocalDbTests`，WPF `PackingListAssociationViewModelTests`／`PackingListUiTests`，VSTO `PackingListImportTests`／附件数据快照／STA `PackingListImportChecks` |

## 验证结果与未完成实机项目

### Review 修复验证（2026-10-03）

- 补登记 `ForExcelPackingListController.CheckAsync` 的创建权限及 `PackingListController.AssociateSourcesAsync` 的编辑权限。真实 MVC Action／权限过滤器覆盖未认证、无权限、错误权限及精确授权，权限专项 7 项通过。
- VSTO 接纳检查结果时按 CustomerProductId 恢复显式候选选择，候选失效则清除，不选择替代关系。导入专项 5 项通过；真实 WPF 单元格模板 STA 共 13 条断言通过，覆盖候选对象替换、差异确认、修改数量及重新检查。
- 来源分配先校验每行及剩余可分配量，再累加并显示汇总；非法、负数和超大输入保留原文本并阻止确认，修正后可恢复。.NET 10 PackingList 专项 54 项通过，包含两个新增溢出边界用例。
- 后端解决方案、.NET 10 客户端及 VSTO Debug／Release 构建通过。WPF 测试默认输出目录拒绝写入，改用工作区 `.artifacts/packing-review-fix`，依赖从已有 NuGet 本地缓存恢复；VSTO 构建使用已有开发签名证书。
- 上述回归测试在修复前分别复现失败，修复后通过。本轮未重跑全部测试、完整库存 SQL 并发场景或 Excel 实机；下方保留首轮实施验证记录，不以专项结果替代联合验收。

### 首轮实施记录

- 后端 PackingList 专项：102 通过、4 跳过。跳过的是原有完整库存执行／跨单并发 SQL 测试，本次没有宣称这些场景已通过。
- 新增随机 LocalDB 验证通过：旧列迁移两次、历史来源保持、真实来源精确查询和分页、拆行数量守恒、审计失败回滚、持久化回执重试不重复。
- .NET 10 PackingList 专项：52 通过，包含真实资源下的分配数量编辑器绑定和非法输入门禁。
- VSTO 纯托管：87 通过。附件单元格快照得到 21 行、数量 195，涵盖 Q／R；这是纯数据解析验证，尚未操作真实 Excel COM。
- VSTO STA：PackingList 9、采购预览 21、地址配置 23、销售导入 43 项断言通过；地址配置进程仍输出既有异常字符串诊断，退出码为 0，应在 Excel 实机复核。
- VSTO Debug／Release 使用 Visual Studio MSBuild 和既有开发证书签名通过。后端、Contracts、.NET 10 构建通过；存在既有空引用警告及 NuGet 漏洞源网络不可用警告。
- VSTO STA 全量未通过：停在既有 `ImplicitStyleChecks`「合并顺序保留项目只读无边框覆盖」断言。本次未修改主题、删除断言或声称全量通过。
- 未验证：Excel Ribbon 打开、右侧配置窗格、Owner、真实附件 COM 读取、非模态焦点、网络期间操作 Excel、关闭释放，以及真实后端两端联合人工验收。业务库脚本未执行；当前不能据自动检查标记为可发布。
