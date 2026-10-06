# M26-S Excel 销售读取交付（IMPL-01 / FIX-01）

执行 B，2026-10-05；worktree：`C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport`，detached HEAD 基线 `56209d97a00533c938dd527fa9d37683064a11e8`。独占切片已完成，真实 PG 验收交主控串行执行。

## 本轮文件

- `SwiftExport.Infrastructure/Repositories/ForExcel/PostgreSqlForExcelSalesOrderRepository.cs`：新增三个销售导入业务读取方法。
- `SwiftExport.Application.Tests/Database/PostgreSqlForExcelSalesMigrationTests.cs`：12 个独立无网络离线用例，复用真实 Dapper 冻结夹具。
- `SwiftExport.Application.Tests/Database/PostgreSqlForExcelSalesMigrationTests.Integration.cs`：FIX 后 13 个真实 PG 场景代码，只编译未执行。
- 本结果及本人 `docs/postgresql-migration/窗口状态/执行B.md`。

没有修改既有业务服务、SQL Server 实现、Core、Contracts、DI、共享夹具、映射、DDL 或采购导入仓储。没有修改主仓库文件、提交、推送或部署。

## 保留语义

三个读取均接受无外层事务，有事务则全部命令传递同一事务与取消令牌；不自建、提交、回滚或释放事务，不加 HOLDLOCK 或新锁协议。非空请求预取消抛异常；源空数组和双空商业编号短路在检查令牌前发生，即使预取消也直接空结果。

客户输入用 `.Trim().ToUpperInvariant()`；数据库 `upper(btrim(customer_code,' '))` 只修剪首尾 ASCII 空格。返回全部 `is_deleted=FALSE` 候选，保留停用、多候选和 NULL 名称。

产品数组作为单个显式 `text[]` 参数通过 ANY 子查询等值匹配；FIX 在 SQL 的 `unnest(@Skus)` 右侧数组项上 `rtrim(input.sku,' ')`，补源 SQL Server 等值忽略尾 ASCII 空格的行为。C# 不二次修剪、大写、去重或拼接输入，NULL 项仍为 NULL。客户关系及 LEFT JOIN 产品均保留源 `is_deleted=FALSE` 范围，不将 NULL 软删值擅自解释为 false。断链/软删产品使主表字段为 NULL，仍返回客户关系。关系启用、产品启用及 ASCII 修剪后非空单位仅计算 IsUsable，不作为 WHERE 资格；不引入供货方式、物料类型或 SKU 合法性检查。客户 LinkSku ASCII 修剪后非空优先，否则返回产品原值，包括 NULL、产品首尾空格和 Unicode 空白。

商业编号先判断双 .NET 空白短路，其余各自 Trim/大写，NULL 转为空参数。同客户、未删、任一非空发票号或 PO 号 OR 匹配，保留所有匹配及状态/nullable 原值，不假设商业编号唯一或限制单据状态。

文字等值及排序复用 `public.erp_dictionary_ci`；客户按 UUID、关系按原客户型号/UUID、订单按系统订单号/UUID 稳定排序。PG UUID 字节比较与 SQL Server uniqueidentifier 优先次序不同，不能声称跨库逐行顺序完全相同。实库客户顺序断言按 PG 十六进制 UUID 字节次序计算。

## 实际验证

两次定向测试均成功，最终命令：

```powershell
$env:SWIFTEXPORT_RUN_PG_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_FAILURE_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_COMMAND_TESTS=$null
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ExcelSales_|FullyQualifiedName~ForExcelSalesOrderTests|FullyQualifiedName~ForExcelSalesOrderContractTests|FullyQualifiedName~PackingListImportAndAssociationTests' --verbosity minimal
```

结果：27 通过、0 失败、0 跳过（本轮 12；既有销售服务 8、契约 2、装箱导入及关联 5）。相关 Contracts/Core/Application/Infrastructure/WebApi/Tests 编译通过，新增实库场景也已编译。复用已有 NuGet 资产，关闭共享编译避开旧轮 obj DLL 占用。既有无关 MSTest 分析器警告保留，不修改无关文件。

离线用例验证三个真实 Dapper 投影、nullable 字段、停用/断链多候选不被内存过滤、归一化参数、原数组不变和 text[]、两个编号 OR/客户边界/空值参数、可空外层事务及零事务副作用、预取消和源短路。不用无网络夹具宣称真实行过滤通过。

核实当前 worktree 的源销售仓储、接口、销售导入服务及 PackingListImportChecker 文件 SHA 与主仓库一致，不因旧 worktree 静默使用过期调用协议。核对了实体/状态约束/唯一索引和本轮三个新增文件内容；无尾部空白，`git diff --check` 无错误。新增文件尚未跟踪，因此没有将 tracked diff 当作新增源码全覆盖证据。

## 主控实库验收

分类：`PostgreSqlForExcelSalesIntegration`；类名：`PostgreSqlForExcelSalesMigrationIntegrationTests`。FIX 后共 13 场景：客户停用/歧义/ASCII 边界，产品范围/歧义/显示/LinkSku 回退，断链/软删/启用/空单位/Unicode 单位，Unicode LinkSku/NULL 回退，两商业编号 OR/重复/状态/NULL，五组特殊字符及内部空格，Unicode 数据库存储边界，无事务和取消/短路，以及新增数组尾 ASCII 空格/前导空格/内部空格/Tab/NBSP/NULL 场景。

严格复用固定入口 `PostgreSqlIntegrationEnvironment.GetFailureConnectionString()`，只允许 `192.168.3.12:5432/dv_next_pg_failure_tests_20261004` 的现有 public。主控按原机制提供连接并开启 `SWIFTEXPORT_RUN_PG_FAILURE_TESTS=1` 后串行筛选：

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'TestCategory=PostgreSqlForExcelSalesIntegration' --verbosity minimal
```

有数据场景使用单个 ReadCommitted 事务随机实体并 finally 回滚；零事务场景只读取随机不命中的编码/身份，完全不写入。无结构初始化、清库、提交或旧数据清理。歧义通过不同原编码的 ASCII 前导空格形成修剪后多候选，避免违反当前只去尾 ASCII 空格的唯一键；不删除或暂时关闭索引。断链用随机缺失逻辑身份，沿用当前逻辑外键模型。

真实 PG 语法、ICU 等值及 Unicode/排序边界未执行验收；执行中网络取消与跨连接一致性亦未验证，不放宽断言或宣称已接入运行。采购导入 HOLDLOCK 协议仍不在本轮范围。

## 保留与跨边界事项

主控通知 M13-Q 实库为 11 通过、1 价格精度失败（1.234567 落库为 1.2346），公共精度由主控处理。本轮没有改旧 M13 文件或放宽其断言。旧 M12/P04/M13 成果保留，主控同步的 RbacManagementService SHA256 仍为 `E736D678E5AD4F664E4DD30FAFD7E4FCA14EDAA46F531B6B97344A4C8941A3A9`。

工作树含旧轮及本轮未提交文件，主控按本轮四个交付文件精确接回，不整体覆盖。本轮没有读出/写入连接密钥，没有发现编码异常，没有启动其他窗口、子 agent 或向其他聊天发送消息。

## M26-S-FIX-01 源常量证据及修正

2026-10-05 01:58:18 开始。主控指出原 `ProductMatches_KeepAmbiguityScopeProjectionAndLinkFallback` 的小写 key 加尾空格应沿用 SQL Server 等值填充语义；原预期 0 错误，已更正为源实测的 2 个歧义候选，并保留数组不改写断言。

本轮通过 DBX `master` / SQL Server `192.168.3.12:1433/dv_next` 执行两条 SELECT 常量，显式 `Chinese_PRC_CI_AS`；没有查询业务表或写库。核心表达式 `UPPER(LTRIM(RTRIM(N'PGKEY'))) COLLATE Chinese_PRC_CI_AS = N'pgkey ' COLLATE Chinese_PRC_CI_AS` 为 true；前导 ASCII 空格 false，尾 Tab false，`00 A` 对 `00 A ` true，对 `00A` false，仅 NULL 的 IN false。由此只在 SQL 数组右侧去尾 ASCII 空格，保持大小写通过冻结 ICU 比较、前导/内部字符及原数组数据。

额外探针：`PGKEY` 对尾全角 U+3000 为 true，对尾 NBSP U+00A0 为 false。**源全角等值是新的已实测边界，尚未验证冻结 PG ICU 的对应结果；本轮不扩大 ASCII 修正或宣称全量 Unicode 等价。** 新用例不对尾全角空格写错误的 false 断言；原带前导 Tab 的 Unicode 边界断言保留。此项交主控在串行 PG 验收时明确差异及目标，不静默放宽原断言。

FIX 实际再运行上述同一离线命令：27 通过、0 失败、0 跳过，相关项目及 13 个实库场景编译通过。没有执行 PG。新增离线数组包含 NULL 项，并断言 SQL 的 rtrim/unnest 而非 btrim/upper 右侧，仍验证参数原文和重复项不变。新增实库场景包含小写尾多 ASCII 空格真、前导 ASCII 空格假、内部空格不删除、Tab/NBSP 假、NULL-only 假；只编译等待主控执行。源码三文件无尾部空白，`git diff --check` 无错误。

## M13 执行资格链只读评估（无实现授权）

读取原 `ExecutionEligibilityService`、`IExecutionEligibilityRepository`、SQL Server 仓储及两个相关测试，并核对主仓库：三个文件只有 CRLF/LF 字节差异，Git 比较无内容差异。主仓库 PG 审计已更新，不能将本 worktree 旧审计实现当作现状。

1. **高隔离审计明确阻塞。** 当前服务先 BeginTransaction(Serializable)，执行授权/目标写入后才在日志初始化器 `Guid.NewGuid()`；主仓库 `PostgreSqlBusinessOperationLogRepository` 已实现 `ICommandTransactionPreparation`，`PostgreSqlCommandLock.AcquireAsync` 无匹配事务前租约时拒绝 Serializable。直接接 PG 审计会在 InsertAsync 失败并回滚资格更新。最小建议沿用主控 P04 已落地能力：认证/输入/已有事务守卫后生成本次服务器 CommandId；可选准备接口在 BeginTransaction 前取得租约，日志使用同一 ID；保留 Serializable，事务释放并清空后释放租约。无需把 CommandId 加入客户端 DTO 或引入重放机制。

2. **清理与失败路径需随上述服务改动独立测试。** 当前 FailAndRollback 失败进入 catch 再回滚，回滚异常可能覆盖业务拒绝/原取消；直接 Rollback/using Dispose 也可能掩盖原异常。最小建议复用 P04 已验证的本层安全清理次序，不新增通用框架；覆盖准备失败/超时/取消、BeginTransaction 失败、授权/版本/同状态拒绝、审计零行/异常、更新失败、提交失败和事务/租约清理次生异常。当前预取消没有入口检查，宜在业务早期守卫后、开始准备/事务前检查，但不能擅自改变无身份/非法命令的原返回边界。

3. **授权锁协议尚未冻结。** 源 HasExecutionEligibilityPermissionAsync 的 HOLDLOCK 覆盖活动账号、用户角色、角色以及角色权限/权限定义，false 的缺失范围同样受源 Serializable 影响；三个仓储入口目前不拒绝缺失事务。源系统管理员分支仅校验 RoleCode，不要求 IsSystem，不可借用 RBAC 管理员检查偷偷增加资格。PG 普通 EXISTS/SSI 谓词不等于源阻塞撤权的 HOLDLOCK，只有现存行的 FOR SHARE 也不保护缺失关联。

4. **局部锁最小候选，必须由主控验证写入方覆盖再冻结。** 主仓库 RBAC 写入已在首个 SELECT/DML 前锁 sys_role SHARE ROW EXCLUSIVE，再取管理员角色/操作者锁。执行资格可以评估首语句对同一角色目录取兼容读取、冲突该写锁的 SHARE 锁，再按一致角色→操作者/相关授权顺序读取锁定；首语句等待结束后才建立 Serializable 快照，避免先读旧授权再等待。若全部撤权路径都参与此角色目录协议，可复用既有局部串行边界。直接账号/权限定义/关联写入是否也受该边界保护尚未核实，不能仅凭 RBAC 管理入口就宣称所有 HOLDLOCK 等价。需锁定关键现存行，或为不参与协议的写入方明确一致锁规则；不能采用新全局业务锁、随意扩大全表锁或降低服务隔离。

5. **目标与版本锁单独处理。** 固定 Product/Customer/Supplier 白名单，GetForUpdate 用行 FOR UPDATE，更新继续 ExpectedVersion/未删谓词、Version+1 及既有审计打戳。缺失目标在 PG 行锁下不受 SQL Server 空范围锁保护；须明确是否仅返回 NotFound 即结束或要求缺失身份阻止并发插入，不引入占位实体/表。接口“未知对象返回空”与源 GetTable 实际抛 ArgumentOutOfRangeException 不一致；服务已守卫枚举，迁移应保留实际异常边界并由主控登记契约偏差。

6. **并发验收与错误边界不可省略。** 首读取前竞争撤权先赢/后赢、用户停用、角色/权限定义停用、普通角色丢键与内置管理员、缺失授权范围、目标版本竞争、审计失败回滚资格、等待中取消及 SSI 40001，均须真实双连接验证。不把 40001 伪装成普通版本冲突、Forbidden 或自动重试；既有 SQL Server 权限测试只覆盖事务内静态切换，不能证明跨连接阻塞和 PG 首快照次序。

以上仅记建议与阻塞；没有修改执行资格服务、接口、仓储、审计或公共锁文件，也未执行其 SQL Server 写入测试。后续应先主控冻结事务准备及授权锁协议，再单独分派实现和相关测试。

## 主控补验：Unicode 比较完整缺口

FIX 停写后，主控提供以下只读矩阵。此表明确标注为主控补验结果，执行 B 没有再次执行 PG 或扩大源码修正。比较均指 `PGKEY` 与 `pgkey` 加一个尾字符，并分别检查该字符与 ASCII 空格的等值。

| 字符 | SQL Server 尾字符仍匹配短串 | SQL Server 字符等于 ASCII 空格 | 当前 PG ICU 字符等于 ASCII 空格 | PG 未裁剪尾字符匹配短串 |
|---|---|---|---|---|
| ASCII U+0020（32） | 真 | 真 | 真 | 假 |
| 窄不换行空格 U+202F（8239） | 真 | 真 | 真 | 假 |
| 中数学空格 U+205F（8287） | 真 | 真 | 真 | 假 |
| 全角空格 U+3000（12288） | 真 | 真 | 真 | 假 |
| Tab U+0009（9） | 假 | 假 | 假 | 假 |
| NBSP U+00A0（160） | 假 | 假 | 真 | 假 |
| U+2000（8192） | 假 | 假 | 真 | 假 |
| U+2001（8193） | 假 | 假 | 真 | 假 |
| U+2002（8194） | 假 | 假 | 真 | 假 |
| U+2003（8195） | 假 | 假 | 真 | 假 |
| U+2009（8201） | 假 | 假 | 真 | 假 |

当前 SQL 数组右侧只 rtrim U+0020，已补 ASCII 尾填充；U+202F/U+205F/U+3000 的源尾字符匹配仍有未补缺口。NBSP/U+2000/U+2001/U+2002/U+2003/U+2009 在冻结 PG ICU 中会与 ASCII 空格相等，另有等值范围扩大差异；不能仅根据这些字符未裁剪尾串为假就认为整体兼容。

不采用 `IsWhiteSpace`、全 Unicode Trim 或将全部空白替换为 ASCII 的方案，这会把源假值改成真值。若主控后续决定保留源尾填充，候选修剪集合只能从已实测真值 U+0020/U+202F/U+205F/U+3000 出发；这仍不能独立解决 NBSP 类与 ASCII 的 ICU 等值范围差异，须另行明确精确比较边界及追加实测，不能声称仅扩大 rtrim 集合即可完成兼容。

本轮仍只交付已冻结 ASCII 修正，不修改公共 ICU、DDL、服务或其他仓储，也不新增未授权比较框架。**M26 整体比较兼容未通过**，比较门槛、是否接受差异及必要用户确认由主控单独决定；本窗口源码保持停写。
