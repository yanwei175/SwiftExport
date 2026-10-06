# M18-Q03-IMPL01 采购 Trace 实施结果

- 执行 A：01a10790-07d0-7a61-91e3-ed83ca6e34d4；目录 C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport。
- 开始：2026-10-05 06:31:05 +08:00；交付：2026-10-05 06:42:32 +08:00。
- 主 Agent / Workflow：Backend / 后端 API 工作流。对象是 PG 只读查询仓储，复用已读 Dapper、会话、业务命令及质量技能；按 MAIN《采购Trace实施分派》实施。
- 结果：新增完整 IPurchaseOrderTraceRepository.GetAsync；40 离线通过，64 真 PG 用例编译成功、未执行。不接运行 DI，不声称 PG 已验收。
- 授权：仅下列三个新源码文件、本新结果及本人状态。前轮全部交付、公共 Guard/元数据/参数、源/Core/DTO/服务只读。

## 交付文件与固定 SHA-256

- SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Trace/PostgreSqlPurchaseOrderTraceRepository.cs
  - SHA-256：627917C47D2D2B94166C834CDA407B6E2ABF0167A65C3091CAC82573B9104B28。
- SwiftExport.Application.Tests/Database/PostgreSqlPurchaseOrderTraceMigrationTests.cs
  - SHA-256：579C2C8C22F12C3026F1342042266111245B00D0E3102EE4ADB6C3F1D1685D62。
- SwiftExport.Application.Tests/Database/PostgreSqlPurchaseOrderTraceMigrationTests.Integration.cs
  - SHA-256：C90156355013AB4A573CC9053CAEC1B84064278334607BD50C9170B97053394C。

文档：本报告及 docs/postgresql-migration/窗口状态/执行A.md。
修改业务代码：是，仅新增采购 Trace PG 仓储；没有修改旧查询、公共实现、契约、服务或 DI。
无数据库连接、探针、DDL、清库、提交、推送、Git、全量测试、新代理/聊天或跨聊天消息。

## 源与实现边界

实际核对 MAIN PurchaseOrderTraceRepository.cs、Core 接口、六 DTO、Trace 服务及实体约束。源固定 SHA：
4ADFBF342C387073615BF1E86FBBBD863AB67AC81AFDC968F9D4118AA3E3AA80。

1. 预取消先抛出。第一次 SQL 完整缓冲本采购头全部行 UID，包括软删行且不要求有效头。此 scope 只传入同 Session/可空调用者事务的 ValidateScopedAsync，一次门禁定位相关入库整头。
2. 一次 QueryMultipleAsync 发送六语句：有效头、全部历史行、Link、完整未删反馈、正式收货、操作日志。源 ReadSingleOrDefault 头为空立即早返 using Dispose，不显式缓冲尾集合、不跳过 scope/Guard、不加事务。
3. 六语句后续均重新按当前表 PurchaseOrderId 查询，不用 scope 数组冻结后续行或证据，不增第二 Guard、头有效性复验、临时表、实体或锁。
4. 正常路径依原顺序缓冲全部集合后进行原 C# 分配。采购行、Link、Net、Gross 不增加删除或生命周期过滤；只有当前 Product 未删。Link 以 TargetLineId 和 TargetType PO 关联，本单行的错头 Link 仍返回作为证据。
5. SourceDemandVersionSnapshot 对当前 Gross.Version；MD 的源版本双方均 NULL，非 MD 双方非 NULL 且相等。保留原 SQL 三值逻辑、NULLIF 空串及字典指纹比较，不套销售 64 位算法。返回 SourceType/两原始指纹不 trim，不因比较而改写证据。
6. 当前 Product.Unit 先 ASCII trim，缺失时仅根 Net 回落 Gross.Unit；不优先 Net.UnitSnapshot。保留目标头身份、AllocatedQty/ReleasedQty/EffectiveQty、取消证据及 CASE 理由优先：缺记录→缺身份→版本/指纹→单位→错头→贡献量。
7. 行 LatestReadyQty 由该阶段最新整反馈提供，Sequence DESC/SqlGuid DESC；NULL 不回填，不能被稍后独立 History 首项覆盖。历史反馈保持 SQL 原顺序，不追加 C# Guid 重排。
8. 正式收货仅采购 Posted/Reversed、未删头行，SourceLineId 指向采购行；Posted Quantity、Reversed 零。按采购行独立返回全部证据，不按 Link 分摊或倍增。
9. 原 C# string.IsNullOrWhiteSpace 与字符串 != 的单位判断原样保留。大小写、尾空格、Unicode 空白等不一致使 FormalReceivedQty 保持 NULL并覆盖 ReviewReason；否则 Sum，空集合为0，并保留其他已有理由。
10. OriginKind2 无 Link 不造缺来源错误；非2有效行无 Link、任何历史 Link NeedsReview 保留原中文。已删行无 Link 不造缺来源错误。
11. 操作日志按 AggregateUid/未删读取，不增加 Module 条件；OccurredDate DESC/SqlGuid DESC。全部 UUID 排序保留源 SqlGuid 字节序，ASC NULLS FIRST / DESC NULLS LAST。
12. quoted aliases 完整，Quantity 由元数据物理 quantity 提供；固定元数据和标识符引用，无外部列输入。局部命令参数独立克隆，显式 uuid、integer、integer[]；公共 uuid[] Guard 原样复用。
13. 不接运行 DI，技术异常/取消原样传播、没有仓储事务接管。Trace 服务的权限、无效 UID、缺头及 UnverifiedReceiptHistory 错误边界未修改。

## 离线与编译记录

最终命令：
dotnet test ./SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter FullyQualifiedName~PostgreSqlPurchaseOrderTraceMigrationTests --logger "trx;LogFileName=M18-Q03-final-offline.trx" --results-directory .artifacts/M18-Q03-IMPL01

每次在同一进程清空 SWIFTEXPORT_RUN_PG_TESTS、SWIFTEXPORT_RUN_PG_FAILURE_TESTS、SWIFTEXPORT_RUN_PG_COMMAND_TESTS、SWIFTEXPORT_RUN_PG_RBAC_TESTS、SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS。

- 首轮及最终均 40 通过、0 失败、0 跳过。最后相关项目与全部新 PG 测试编译成功，0 错误；7 条既有 MSTest 分析告警来自未修改测试。
- 首轮 .artifacts/M18-Q03-IMPL01/M18-Q03-first-offline.trx。
- 中间新增 PG 夹具编译 .artifacts/M18-Q03-IMPL01/M18-Q03-delivery-offline.trx。
- 最终 .artifacts/M18-Q03-IMPL01/M18-Q03-final-offline.trx。
- 离线使用实际 Dapper、Npgsql 参数容器和内存 ADO 连接，验证三命令/六结果、scope 范围 SQL、Guard 参数、借用事务/token、空头推进与 Dispose、完整 aliases/历史边界、原 C# 单位/理由、独立 Latest 与 History、集合身份不冻结、三入口技术异常、六结果技术异常及预取消/所有边界取消。
- 不把 DataTableReader 返回视为 PG 商业 SQL、服务器阶段变化、驱动批次排空事实。生成源码的内联辅助脚本第一次处理复杂投影失败，发生于文件写入之前；修正投影解析后生产/测试首编通过，未更改断言以掩盖失败。
- 新三个文件 UTF-8 可解码、无替换字符、无行尾空格。已逐文件检查与源对应的阶段/投影/条件/组装；禁止 Git，本轮没有 git diff 或 Git 状态检查。

## 真 PG 用例（仅编译，供主控串行执行）

类：PostgreSqlPurchaseOrderTraceMigrationIntegrationTests。
[TestCategory("PostgreSqlIntegration")]、DoNotParallelize。
沿用 GetFailureConnectionString，固定 192.168.3.12:5432 / dv_next_pg_failure_tests_20261004 与原启用门禁，不输出连接内容。

64 用例：
- 缺头/删头/空行释放3；
- scoped 含删采购行、无有效头行、整头另一坏行、删收货scope行及无关坏历史5；
- 独立无Link/历史删行3；
- 当前产品/仅根回落/Net快照禁止优先/历史删Gross-Net8；
- 技术版本、MD/非MD源版本及字典/NULL指纹13；
- CASE优先、缺记录/身份、错头与合法全额释放7；
- 最新整行NULL及历史软删反馈2；
- Posted/完整Reversed/草稿/他类型/软删头行6；
- 原.NET单位大小写、尾空格、Unicode空白4；
- 日志异Module及同日SqlGuid排序1；
- Guard完成到整批前新增行/反馈不被冻结1；
- 预取消/三个命令/六结果边界10；
- 可空事务连续缺头读1。

夹具说明：
- 独立随机事实、ReadCommitted 外层事务，finally 回滚/释放；无初始化、提交、清库、DDL、禁约束或持久对象。
- 使用元数据原始插入保留指定 UID/版本，不由审计 Add 重置。
- Reversed 具备完整匹配原/反向流水，正确收货行 SourceLineId/CommandItemId、产品、仓库、单位与原流水引用；随机合法 Sequence 不推进全局序列。
- ScopedGuard 坏历史用正 Quantity 与 OriginalLineId 作为明确修正证据，未绕正量 CHECK；整头坏行不必属于本采购行仍须拒绝。删收货scope行仍定位整头，保留另一有效行避免无行头混淆。
- 不造物理禁止的软删 Link、负分配/非法释放、软删不可变日志、非法版本或同一反馈 Sequence；软删历史 Link 返回与摘要仅由离线协议结果覆盖，SQL不新增过滤的静态检查仍有效。
- 产品、Gross/Net 数量/生命周期/行号合法；来源外键是现有逻辑引用，不新增真实单据或外键绕过框架。
- 真实 reader 包装器保留六语句并转发 ReadAsync/NextResultAsync/token。正常/空头返回后验证连接与原事务可读；取消先回滚可能 aborted 的本人事务，再验证同连接可用。
- GuardToBatchUsesCurrentTables 的屏障在整批开始前、无活动 reader 时，只有实际当前表自写；不声称六个服务器语句执行中间的变化或物理跨连接验证。

## 未验收与主控接管

- 64 PG 用例未执行；当前授权仅编写/编译，主控运行。没有实际 SQL Server 基线、数据库探针、全量回归或运行 DI 接线。
- 缺头时 Npgsql 的第一次推进、Dispose 对全部后续服务器结果/异常的处理及外层事务状态未实测。实现保留原早返，不擅自拆 SQL、提前缓冲或吞技术错误；实库若不能等价应由主控登记阻塞事实。
- 六语句中途服务器事实变化与物理跨连接仍未运行；需要真实独立连接及可控屏障，不由客户端 NextResult 或命令前自写代替。
- P03/B10/B19 Unicode 字典比较前置继续开放；本实现沿当前 erp_dictionary_ci、rtrim 尾ASCII及 btrim ASCII空格规则，不新增全局 normalize，不声称ASCII用例解决Unicode全域等价，公共修订归主控。
- 公共 Guard SHA 保持 7F5964503D8FB7BA35DFE2BA2C7941C3D7EEE16F3ADA94D93D589D03AB96925F。
- 已核对前轮 Search SHA BD9D7BCB002C4CA5BB263A8B35D949072B636260B736BF626EA89126C76ABB91；前轮 Detail / 离线 / PG SHA 分别保持 F7D0A338A5606D215D999B1AB34C8879CFD1B11E69D1C341376F62BF518E9711、D82175543A0901942BB7B05E1F8F61D8DA7C4C124387611F1C7A799FF81F549F、F68E80540FC8973DBA6B533650A482C46B59B07DBF018BB2F44D1A7F0B9401F0。
- 保留已有工作区混合改动，未以本轮授权接管前轮写权。无连接字符串修改或凭据输出；文件编码检查通过。
