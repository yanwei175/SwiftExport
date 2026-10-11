# 采购 BONO 唯一限制与升级说明

核对日期：2026-10-11。此变更同时完善 VSTO 采购导入交互和后端创建占号规则。.NET 10 采购详情仅作布局参考，未修改；复用原检查、配置查询与保存 API。

## 生效范围

- `bo_no` 非空且订单 `is_deleted=false` 时，在所有采购单之间唯一。不按客户、供应商、确认或活动状态缩小范围；关闭、作废、已确认订单仍占号。
- 首尾普通空格通过 `btrim(bo_no, ' ')` 修剪，比较沿用 `public.erp_dictionary_ci`。null、空字符串及仅普通空格不占号。
- 检查接口返回 `BoNoAlreadyExists`／`BoNo`；正式独立创建、需求转采购新建均在事务中复核。仅 BONO 索引的 `23505` 冲突转换成明确校验拒绝；其他数据库故障不冒充 BONO 重复。
- 两个采购头插入仓储返回 `RepositoryWriteResult`；调用方遇失败停止写行和贡献并回滚，不发布未提交单号。幂等重放返回原结果；追加既有草稿不再取号或按新号查重。
- 不扩展普通草稿更新的 BONO 编辑白名单，也不自动合并同号订单。

## 数据库升级

增量脚本：[PostgreSQL采购BONO唯一限制.sql](../SwiftExport.Infrastructure/Database/PostgreSQL采购BONO唯一限制.sql)。本轮仅交付脚本，没有执行。

1. 管理员确认目标业务库和维护时段，先单独运行脚本开头的只读重复查询，人工决定保留或处理哪些旧单。脚本不自动改号、软删除或清理。
2. 历史重复处理完成后执行事务段。表级 SHARE 锁保护预检到建索引之间的占号范围；发现重复会抛错并阻止建索引，需要显式结束失败事务后重新处理。
3. 检查 `pg_indexes` 的 `ux_purchase_order_head_bo_no` 定义：普通空格修剪、CI collation，以及未删除／非空部分过滤必须一致。`IF NOT EXISTS` 不会修复同名但定义错误的旧索引，发现偏差应另行安排升级。
4. 完成隔离库唯一及并发验收后再部署应用并开放创建。不把生成基线当作业务库已升级或并发已验收。

实体定义位于 `SwiftExport.Core/DatabaseSchema/EntitySchemaMetadata.cs`；生成器特化表达式位于 `SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlSchemaGenerator.cs`。生成基线已同步到 `SwiftExport.Infrastructure/Database/Generated/SwiftExport.PostgreSql.Schema.preview.sql`。可以用 `dotnet run --project _tools/SchemaGenerator --no-build -- --verify` 核对文件与实体元数据一致性；此命令不连接数据库。

## 前端默认资料与读取边界

采购单头采用常驻横向标签／输入组；使用缓存客户和供应商。有效客户是 SKU 匹配的前提，独立本行／全部匹配不要求供应商或完整单头，保存仍需要当前整单检验通过。换客户保留明细的数量、价格、描述等，清旧产品身份、候选和型号差异确认。

双方选齐后查询 `ICustomerPoConfigApi.GetByPartiesAsync`，需 `Customers.PoConfig.View`。只补空值，保留非空 Excel／手工内容；`IMType → InstructionManualType`、`Purchaser → PurchasePerson`、`Note → Remark`。不改 BONO、客户 PO、日期、整单优惠金额或明细；固定 CNY，配置币种不同仅提示。查询期间保留编辑，暂停整单检验和保存；旧双方、旧账号、已关闭页面结果不得回写。“更多”菜单可重试，不建设共享配置缓存。

读取单头只覆盖实际读取的字段，未配置字段保留，配置单格为空明确清空。客户代码须唯一定位到缓存候选，否则保留原文、提示选择并禁用匹配。未知保存结果仍冻结原请求，以相同 CommandId 显式重试。

## 本轮验证记录

- 后端及 Contracts 构建通过；VSTO Visual Studio MSBuild Debug／Release 通过，保持 net48、C# 7.3。
- 采购后端范围测试：120 通过、43 因各自集成环境未启用而跳过。覆盖只读检查、独立创建、新建需求采购、头写入冲突回滚、幂等重放和显式追加。
- VSTO 采购纯托管专项 20 项通过。采购真实 WPF STA 页面 58 条断言通过，覆盖常驻表头绑定、数量颜色、行内弹层、实际排序、红绿圆点、非标复选命令、图片绑定、多选及大量提示不挤占明细。未重跑全量 STA，已有主题／DataGrid 验收记录不因此被改为通过。
- 扩大回归未全绿：VSTO 127 通过、1 失败，`SalesDirectoryComesFromModelAttributes` 仍要求已移除的 `Header.Currency` 等特性；后端扩大范围 183 通过、65 跳过、1 失败，`Common_and_procurement_error_code_boundaries_are_separate` 仍要求旧 13 个通用错误码，当前为 22 个。对应生产与断言文件不在本轮修改范围，未删除或放宽断言。
- NuGet 漏洞源不可达产生 NU1900；不表示依赖漏洞审计完成。

新增 `SwiftExport.Application.Tests/Database/采购BONO唯一约束PG测试.cs` 已编译，**未运行**。它复用明确的测试环境门禁：192.168.3.12:5432 的 `dv_next_pg_failure_tests_20261004`，需显式启用 `SWIFTEXPORT_RUN_PG_FAILURE_TESTS` 和 `SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION`，不得换成业务库。运行前须重新确认允许写入范围：两个测试只创建会话临时表；并发测试仅创建随机 `purchase_bono_test_<GUID>` schema 和单表，最终只删除自身表与 schema。测试不会初始化／清理整库，也不操作已有采购表。本轮未使用已有环境变量作为新的数据库写入授权，未执行实库唯一索引及并发创建验收。

Excel 实机仍需验证：缓存客户选择、双方配置权限与手工保留、读取后回显／换客户保留明细、本行和全部匹配、候选及客供件、图片加载、非模态输入与 DPI、关闭与账号切换释放、正式草稿保存及 BONO 重复提示。实库和实机完成前不标记可发布。

## 本轮文件清单

以下文件包含本次实现、验证和说明更新，未提交 Git：

### VSTO 页面、模型和接线

- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/Models/PurchaseOrderImportLine.cs`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/Services/PurchaseOrderImportRequests.cs`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/ViewModels/PurchaseOrderImportViewModel.Addresses.cs`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/ViewModels/PurchaseOrderImportViewModel.Submission.cs`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/ViewModels/PurchaseOrderImportViewModel.cs`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/Views/PurchaseOrderImportView.xaml`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/Views/PurchaseOrderImportView.xaml.cs`
- `SwiftExport.Client.VSTO/Ioc/ServiceRegistration.cs`
- `SwiftExport.Client.VSTO/SwiftExport.Client.VSTO.csproj`
- `SwiftExport.Client.VSTO/Features/Procurement/PurchaseOrders/ViewModels/PurchaseOrderImportViewModel.Defaults.cs`

### 后端、实体与契约

- `SwiftExport.Application/Services/ForExcel/ForExcelPurchaseOrderService.cs`
- `SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/FromDemand/PurchaseDraftCreationService.cs`
- `SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/ProcurementOrderDraftService.cs`
- `SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders/PurchaseOrderImportIssueCodes.cs`
- `SwiftExport.Core/DatabaseSchema/EntitySchemaMetadata.cs`
- `SwiftExport.Core/Interfaces/Procurement/PurchaseOrders/Draft/IProcurementOrderDocumentRepository.cs`
- `SwiftExport.Core/Interfaces/Procurement/PurchaseOrders/Draft/IPurchaseOrderDraftRepository.cs`
- `SwiftExport.Infrastructure/Database/Generated/SwiftExport.PostgreSql.Schema.preview.sql`
- `SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlSchemaGenerator.cs`
- `SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Draft/PostgreSqlProcurementOrderDocumentRepository.cs`
- `SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Draft/PostgreSqlPurchaseOrderDraftRepository.cs`
- `SwiftExport.Infrastructure/Database/PostgreSQL采购BONO唯一限制.sql`

### 测试

- `SwiftExport.Application.Tests/Database/PostgreSqlInventoryExecutionReadIntegrationTests.cs`
- `SwiftExport.Application.Tests/ForExcel/ForExcelPurchaseOrderTests.cs`
- `SwiftExport.Application.Tests/Procurement/ProcurementOrderCommandOrchestrationTests.cs`
- `SwiftExport.Client.VSTO.Tests/PurchaseImportTestFixture.cs`
- `SwiftExport.Client.VSTO.WpfTests/PurchasePreviewChecks.cs`
- `SwiftExport.Client.VSTO.WpfTests/SwiftExport.Client.VSTO.WpfTests.csproj`
- `SwiftExport.Application.Tests/Database/采购BONO唯一约束PG测试.cs`
- `SwiftExport.Application.Tests/Procurement/采购BONO仓储约束测试.cs`
- `SwiftExport.Application.Tests/Procurement/需求采购BONO创建测试.cs`
- `SwiftExport.Client.VSTO.Tests/采购导入默认资料与匹配测试.cs`

### 文档

- `.agents/references/模块能力地图/采购模块能力地图.md`
- `SwiftExport.Client.VSTO/docs/基础设施验收清单.md`
- `SwiftExport.Client.VSTO/docs/采购订单导入预览页说明.md`
- `docs/ForExcel采购订单导入后端说明.md`
- `docs/采购用例说明.md`
- `docs/采购BONO唯一限制与升级说明.md`

