# M18-Q02-IMPL01 采购管理查询实施结果

执行B：01a10790-0ef8-7663-ab9b-aeee9f7f1c0d；工作目录 `C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport`。开始：2026-10-05 06:06:35 +08:00。Backend / 后端API工作流，复用仓储、DbSession、质量门禁技能。仅新增专属仓储、两测试、本结果及本人状态；M19/M17等旧交付停写，公共来源/采购锁归主控。

## 交付与行为

新增完整 `IPurchaseOrderManagerQueryRepository` 的 PG 实现（SearchAsync、GetLinesAsync），**未接DI**。修改了新业务仓储代码，未修改原业务代码、Core、Contracts/DTO、Application服务、DI、共享Guard/映射/参数或A进度文件。

- 两入口借同一 Session 可空事务、透传 CancellationToken，先 ValidateGlobalAsync，再独立全局正式采购入库孤儿 EXISTS。独立谓词保留未删 Posted/Reversed 采购入库头行、SourceLineId NULL或采购行不存在；存在但已软删采购行仍可定位，不能收窄到本单。命中抛原 InvalidOperationException 与“存在无法定位采购行的正式采购入库历史，请先完成数据核实与迁移。”；Guard 的未知历史异常及技术异常不吞掉。
- Search：两门禁之后只一条真实SQL，经 ReceiptFacts→LineFacts→HeadFacts→projected 完成所有数量、日期、商业CASE、头/最终状态筛选和源排序，完整缓冲后 C# 仅 Count/原 Skip/Take。不分别重算count/页，不复读头，不C#文化排序或计算商业量，无临时表/额外事务/业务锁。
- 收货按 SourceLineId 聚合，Posted计量、Reversed零，不展开 Link。最新反馈按整采购行 Sequence DESC、SQL Server uniqueidentifier 字节优先级 DESC；同序合法并列受唯一索引禁止，未绕索引造样本。
- 保留 InvalidBase/Ready/Receipt 独立条件、Unknown/Zero/Full计数、Ready/Receipt CASE优先级、decimal(18,6) ReceiptProgress、ReviewReason优先、UnlinkedQty资格。单位MIN/MAX保留SQL计算、NULL三值；已知ASCII尾空格等值仅在比较处rtrim，DTO原单位不改写。B10 Unicode/ICU与源SQL比较等价仍开放，不扩大公共规范化方案。
- 北京时间业务日每次一次；逾期 `<today`、临期 `today<=date<today+8`，保留活动已确认门槛和未知Ready风险。OrderDate自然日半开上界，MaxValue.Date不AddDays溢出；计划交期和预计完成保留原DateTime含等号上界。
- 局部显式Npgsql参数固定nullable timestamp/uuid/bool/int[]；多选Distinct、空集合[-1]+limit=false；关键词保留Trim及~%_[字面转义，CustomerCode原等值使用既有字典/SQL尾ASCII空格比较。全部DTO字段显式quoted aliases，采购物理数量为quantity。
- GetLines不使用整单遮罩：正式Received SUM，Unreceived=max(Qty-Received,0)，未知Ready为NULL；当前产品图片可NULL、缺ProductId返回Guid.Empty，送达名只客供件，行字段/日期/价格NULL与源排序保留。

## 验证结果

执行命令（每次先清启用开关，未输出任何连接串）：

```powershell
Remove-Item Env:SWIFTEXPORT_RUN_PG_TESTS,Env:SWIFTEXPORT_RUN_PG_FAILURE_TESTS,Env:SWIFTEXPORT_RUN_PG_COMMAND_TESTS,Env:SWIFTEXPORT_RUN_PG_RBAC_TESTS,Env:SWIFTEXPORT_SQL_SOURCE_READ -ErrorAction SilentlyContinue
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlPurchaseOrderManagerMigrationTests.Offline_' --verbosity minimal
```

最终 **25通过、0失败、0跳过**；必要依赖编译无错误。前四次阶段验证均24通过，随后新增冻结/完整别名检查后两次均25通过。中间新增 MixedReadyPriority 的日期断言产生一条 MSTEST0017，改用明确 expectedDate 后已消除；最终完整测试编译仅7条既有分析器警告，无本轮新增警告，末次基础构建缓存无警告输出。未放宽有效断言或修改其他测试。

离线用真实Dapper与本例ADO脚本验证：两个入口/可空父事务、门禁优先及原异常、完整头DTO与行DTO映射、各字段quoted aliases、去重数组/类型/时间/MaxValue/字面关键词、缓冲工作集分页、reader关闭后数据源变化不改变计数/页、三阶段令牌取消及原技术异常、连接/事务/reader所有权。脚本不实现SQL商业聚合，不能把这些结果当作PG算法或服务器等待证明。

**40项真实PG用例仅编写并编译，未运行。** 使用现有固定 failure 库门禁、DoNotParallelize、随机业务键/UID，单条ReadCommitted父事务内新增/读取，finally回滚，无初始化/提交/清库。覆盖：

- 两全局门禁与无关坏历史；已删但存在采购行、无活动行头；独立采购无Link。
- 六种货好状态及Unknown优先/全零/全满/混合；七种正式收货状态/数量/单位/NULL单位/完整反向证明；跨行单位不同/NULL/空白/尾ASCII空格遮罩。
- 最新整行报告、忽略软删更高序号、多Link不倍增；当前图片变更/产品软删/Guid.Empty/客供件送达名/价格日期NULL。
- 五个Due窗口边界、活动/确认/生命周期和待关联资格；OrderDate自然日、计划/完成精确闭边界及字面搜索；分页工作集和读取后真实同事务变化保留旧结果。
- 可空事务、预取消及连接后续可用；0采购量现有CHECK拒绝证据，保存点只恢复本测试中止事务，不禁用约束。成对历史流水的Sequence采用独立随机正值满足唯一索引，不插重复0，不消耗数据库序列。

真实PG范围待主控执行；没有数据库SQL/DDL执行、服务器等待取消、物理独立连接竞争、全量测试、Git命令、提交、推送、部署或代理/聊天消息。此前主控通过数不混算本轮。大列表完整缓冲的内存/延迟代价尚未性能验收；两门禁与主查询不是新串行事务，保持源普通读取入口，未宣称全请求跨语句隔离。读取后变化用例与ADO屏障不是物理跨连接并发证据。

## 固定交付SHA256

以下三源/测试及本结果位于工作目录；绝对路径如下：

| 文件 | SHA256 |
| --- | --- |
| C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Query/PostgreSqlPurchaseOrderManagerQueryRepository.cs | 6D0D43E98F2BD0DAC7DD2B00CC4A3BD450FEBD7E73079A8CDEB87077A0C2D674 |
| C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Application.Tests/Database/PostgreSqlPurchaseOrderManagerMigrationTests.cs | 5BAA3D8BC98092C06F2460D72BB35BEF32BCA20E126E55A84CDB65938CF43DD3 |
| C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Application.Tests/Database/PostgreSqlPurchaseOrderManagerMigrationTests.Integration.cs | 415B93D1B2251978D07E6F25E01ADD1FA9A717134B35D6C5AC70751FF32C8FDE |

结果路径：`C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/docs/postgresql-migration/任务结果/M18-采购Manager-实施结果.md`。本人状态：`C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/docs/postgresql-migration/窗口状态/执行B.md`。新三文件已检查0尾空格、0替换字符，中文职责注释齐备；保留既有未提交工作，本轮未使用Git查询。

## 只读依赖快照

主源 `D:/Documents/source/CSharp/SwiftExport/SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Query/PurchaseOrderManagerQueryRepository.cs`：CBBC704FB15B85455B21DF5F911FEAA67B81FE321C9FBB461AC80A403AE272A7，与分派一致。实际服务验证分页/状态/文本/日期，仓储不额外增加业务验证，完整接口与两个DTO已读取。

本工作目录公共依赖未编辑；已读时间与结束核对：

| 路径（相对工作目录） | SHA256 |
| --- | --- |
| SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/PostgreSqlInboundReceiptHistoryGuard.cs | 7F5964503D8FB7BA35DFE2BA2C7941C3D7EEE16F3ADA94D93D589D03AB96925F |
| SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs | B5583A2F128A0A8AAABB275E3E812D4197F242A4628FE632110A47BADC55F675 |
| SwiftExport.Infrastructure/Repositories/Base/PostgreSqlEntityMetadata.cs | 8D0E928B456E2C7261CDC7A7626414D3A398D28B92BF6DB68681D03CFF3B2A2F |
| SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlNames.cs | E2E52C228EFF31906DB1B0667988BA3317FD304A24471431CF7B80DC583D4F1D |
| SwiftExport.Infrastructure/DependencyInjection.cs | 165E6229E64DE1A5F4FA7DBE38729AA17C059AA87259A0D7C4206833176C7380 |

Guard在实施前/结束SHA均与主控固定值一致；其余公共文件均只读，不声称锁定其他窗口或全仓库版本。M15/FactLock未作为本普通查询前置，也未因此实施反向仓储。

