# 最终复验：P04-TEST-01

2026-10-05 01:15:33 +08:00：主控已把两条业务拒绝路径改用既有 RollbackSafely 并精确同步，本窗口只读校验两边服务 SHA256 同为 E736D678E5AD4F664E4DD30FAFD7E4FCA14EDAA46F531B6B97344A4C8941A3A9；未编辑生产服务。

清除三个 PG 启用标志后，实际运行：
`dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~RbacTransactionPreparationTests' --logger 'console;verbosity=minimal'`

编译成功，最终 **23 通过/0 失败/0 跳过**，73 ms。场景数为首次21项加2个连接关闭故障场景；保留下面20通过/1失败及22通过/1失败的首次证据，不累加重复执行数量。最终代码与此服务版本配套；最后仅补职责注释，不改变行为。

本轮交付独立测试、P04结果和自己的状态三个文件；修改服务由主控独占同步并修正，不能算成本窗口生产写入。生命周期发现的问题已由主控修正并复验通过；本窗口停写等待主控合入测试、真实PG审计/高隔离验收。未执行任何PG或数据库操作，未修改原测试、DDL、公共夹具、A文件，未提交Git。

## 保留本轮首次诊断及命令历史

下面失败描述对应初次同步服务版本，现已按上述最终复验闭合；不是当前残留失败。

# P04 RBAC 事务准备测试结果

- 轮次：P04-TEST-01；执行 B / 01a10790-0ef8-7663-ab9b-aeee9f7f1c0d。
- 目录：C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport；基线 56209d97a00533c938dd527fa9d37683064a11e8，detached HEAD。
- 开始：2026-10-05 01:06:08 +08:00；记录：2026-10-05 01:14:03 +08:00。
- 授权：主仓库 RBAC实施分派.md、窗口协调.md；仅新增 Authentication/RbacTransactionPreparationTests.cs（项目 SwiftExport.Application.Tests）、本结果及自己的状态页；禁止生产、现有测试、公共夹具、DDL、A文件修改、PG执行、Git提交。
- 本轮代码完成，定向验证有一项真实行为失败，等待主控修正/精确同步，不放宽断言。

## 文件归属与版本

本窗口新增 `SwiftExport.Application.Tests/Authentication/RbacTransactionPreparationTests.cs`，使用独立 IDbSession/IDbConnection/事务/仓储/日志/租约假件，不引用既有测试私有夹具，不反射修改生产状态。初始旧源码的新行为尚未验证前，主控已通知将 RbacManagementService.cs 精确同步至本树；本窗口从未编辑或复制该服务。

本轮开始已有 M12 三个源码/测试及结果/状态未提交，全部保留，不修改（本状态例外）。最终 Git 显示修改的生产服务来自主控同步，不属于本窗口写入。实际测试服务 SHA256：747EE7361D0C594C0B15946A8ED1B750B7F28E1BA1F881FB549F2112775754B9。01:14:03 只读比较时主仓库服务已为 E736D678E5AD4F664E4DD30FAFD7E4FCA14EDAA46F531B6B97344A4C8941A3A9，说明主控后续版本已变化；不自行同步、不得把本轮失败描述成更新后主仓库必然仍失败。

## 覆盖

23 个展开场景：成功先准备再 Serializable、原取消令牌传递、同一非空 CommandId 传审计、管理员及版本拒绝、审计异常/零行、准备取消/取得租约后取消/事务内取消、空租约超时、准备技术异常/超时、BeginTransaction 失败、无准备接口兼容、未认证/调用方已有事务先拒绝、事务/连接/租约释放与回滚次生异常、取消不被清理掩盖。

准备等待期间交错两次调用同一服务，第二次先完成后第一调用继续，验证各自审计仍使用各自获取租约身份，防可变 CommandId 字段串用。事务释放且 Session.Transaction null 必须均早于租约 Dispose。Dispose 失败还须先尝试关闭连接，连接关闭再失败也释放租约。此类假件只证明编排，不能证明 PG 锁、40001 或 SQL 原子性。

## 实际命令与结果

每次 dotnet test 前清除当前进程 SWIFTEXPORT_RUN_PG_TESTS、SWIFTEXPORT_RUN_PG_FAILURE_TESTS、SWIFTEXPORT_RUN_PG_COMMAND_TESTS，不打印凭据。

1. `dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~RbacTransactionPreparationTests' --logger 'console;verbosity=minimal'`：CS2012，Application/obj DLL 写入 Access denied，未运行测试。
2. 同命令附 `-p:IntermediateOutputPath=obj/P04-TEST-01/ -p:OutputPath=bin/P04-TEST-01/`：仍 CS2012，DvFileStorage/Contracts 独立输出写入被拒绝，未运行；未清理旧目录或扩大权限。
3. `dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~RbacTransactionPreparationTests' --logger 'console;verbosity=minimal'`：绕过共享编译器后写入正常；首次发现本新增测试的 throw 条件表达式括号缺失 CS8115/CS0029，修正自己的源码；同时修复自己 CS9107 捕获警告，不改无关测试。
4. 同第3命令：编译成功，21场景20通过/1失败；新增连接关闭故障覆盖后，同命令最终23场景22通过/1失败/0跳过。只既有 MSTest 警告；未全量测试、未PG、未TRX，原始输出在聊天工具记录。

## 唯一失败及主控所需最小修正

失败：`CleanupExceptions_DoNotMaskDeterminedResult(False,False,True,True)`，本文件第175行，期望 FORBIDDEN，实际 UNEXPECTED。

条件：管理员锁重验返回 false；事务 Rollback 抛 InvalidOperationException；租约 Dispose 也注入清理异常。当前本树服务第284行在返回 Forbidden 前直接 transaction.Rollback，异常进入通用 catch，于是覆盖管理员拒绝为 Unexpected。其余清理已释放，会话为空。与冻结“清理异常不得掩盖已确定结果”相冲突，不能降低断言或伪称已通过。

相同风险存在 action 已返回失败后第289行直接 Rollback 的业务拒绝分支。建议主控检查这两个失败返回路径，使用现有 RollbackSafely 保留原拒绝；成功 Commit 异常仍应技术失败，不能通过统一吞掉提交异常解决。本窗口仅报告，不修改生产服务。

主控最新服务已经变化，后续请由主控精确同步本树再定向复测；本轮未对不同哈希的主库版本作通过/失败结论。原21项管理/API/命令锁测试通过是主控通知的历史证据，不与本轮23项累加。

## 交付与剩余

本窗口仅独立测试、本结果和状态写入；主控同步生产服务另属主控。未修改业务代码、数据库、DDL、共享夹具、既有测试或其他窗口文件；未提交、推送、部署。必要构建产物可正常产生，未清理目录。最终范围、中文注释、编码/空白检查后停写。当前等待主控精确同步修正及定向复验，PG生命周期、实际高隔离锁及故障协议仍由主控串行验收。
